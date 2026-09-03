using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Sequora.Internal;

namespace Sequora.Tests;

public sealed class SchedulingTests
{
    [Fact]
    public async Task DefaultEnqueue_IsFifo()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1));
        await queue.EnqueueAsync(new WorkJob(2));
        await queue.EnqueueAsync(new WorkJob(3));

        Assert.Equal(1, ReadId(queue));
        Assert.Equal(2, ReadId(queue));
        Assert.Equal(3, ReadId(queue));
    }

    [Fact]
    public async Task HigherPriority_IsDequeuedFirst()
    {
        using ServiceProvider provider = SequoraProvider.Create(options => options.PriorityFairnessLimit = 0);
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1), options => options.Priority = 1);
        await queue.EnqueueAsync(new WorkJob(2), options => options.Priority = 10);
        await queue.EnqueueAsync(new WorkJob(3), options => options.Priority = 5);

        Assert.Equal(2, ReadId(queue));
        Assert.Equal(3, ReadId(queue));
        Assert.Equal(1, ReadId(queue));
    }

    [Fact]
    public async Task EqualPriority_KeepsFifoOrder()
    {
        using ServiceProvider provider = SequoraProvider.Create(options => options.PriorityFairnessLimit = 0);
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1), options => options.Priority = 4);
        await queue.EnqueueAsync(new WorkJob(2), options => options.Priority = 4);
        await queue.EnqueueAsync(new WorkJob(3), options => options.Priority = 4);

        Assert.Equal(1, ReadId(queue));
        Assert.Equal(2, ReadId(queue));
        Assert.Equal(3, ReadId(queue));
    }

    [Fact]
    public async Task Fairness_InsertsOldestLowPriorityJob()
    {
        using ServiceProvider provider = SequoraProvider.Create(options => options.PriorityFairnessLimit = 2);
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1), options => options.Priority = 0);
        await queue.EnqueueAsync(new WorkJob(2), options => options.Priority = 10);
        await queue.EnqueueAsync(new WorkJob(3), options => options.Priority = 10);
        await queue.EnqueueAsync(new WorkJob(4), options => options.Priority = 10);
        await queue.EnqueueAsync(new WorkJob(5), options => options.Priority = 10);

        Assert.Equal(2, ReadId(queue));
        Assert.Equal(3, ReadId(queue));
        Assert.Equal(1, ReadId(queue));
        Assert.Equal(4, ReadId(queue));
        Assert.Equal(5, ReadId(queue));
    }

    [Fact]
    public async Task LargeFifoWorkload_PreservesOrderAtScale()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        const int count = 5000;

        for (int i = 0; i < count; i++)
        {
            await queue.EnqueueAsync(new WorkJob(i));
        }

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i, ReadId(queue));
        }
    }

    [Fact]
    public async Task LargeStrictPriorityWorkload_DequeuesNonIncreasingPriorityAndKeepsFifoWithinTies()
    {
        using ServiceProvider provider = SequoraProvider.Create(options => options.PriorityFairnessLimit = 0);
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        const int count = 3000;
        Random random = new(42);

        for (int i = 0; i < count; i++)
        {
            int priority = random.Next(0, 5);
            await queue.EnqueueAsync(new WorkJob(i), options => options.Priority = priority);
        }

        int? lastPriority = null;
        int lastId = -1;
        for (int i = 0; i < count; i++)
        {
            Assert.True(queue.TryReadPending(out JobEnvelope? envelope));
            int priority = envelope.Priority;
            int id = Assert.IsType<WorkJob>(envelope.Job).Id;

            if (lastPriority is int previous)
            {
                Assert.True(priority <= previous, "Priority must be non-increasing across dequeues.");
                if (priority == previous)
                {
                    Assert.True(id > lastId, "Equal-priority jobs must stay in FIFO order.");
                }
            }

            lastPriority = priority;
            lastId = id;
        }

        Assert.False(queue.TryReadPending(out _));
    }

    [Fact]
    public async Task Fairness_NeverStarvesTheOldestJobBeyondTheLimit()
    {
        using ServiceProvider provider = SequoraProvider.Create(options => options.PriorityFairnessLimit = 3);
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(0), options => options.Priority = 0);
        for (int i = 1; i <= 500; i++)
        {
            await queue.EnqueueAsync(new WorkJob(i), options => options.Priority = 10);
        }

        int skipsBeforeOldestRan = 0;
        bool oldestRan = false;
        while (queue.TryReadPending(out JobEnvelope? envelope))
        {
            int id = Assert.IsType<WorkJob>(envelope.Job).Id;
            if (id == 0)
            {
                oldestRan = true;
                break;
            }

            skipsBeforeOldestRan++;
        }

        Assert.True(oldestRan, "The oldest low-priority job must eventually run.");
        Assert.True(skipsBeforeOldestRan <= 3, $"Fairness limit of 3 was violated: {skipsBeforeOldestRan} higher-priority jobs ran first.");
    }

    [Fact]
    public async Task DelayedJob_DoesNotRunUntilDelayCompletes()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRetryDelay delay = new(hold);
        WorkSink sink = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        await harness.StartAsync();
        await harness.Queue.EnqueueAsync(
            new WorkJob(1),
            options => options.Delay = TimeSpan.FromMinutes(5));

        await delay.FirstCall.Task.WaitAsync(WorkerHarness.Timeout);
        Assert.Equal([TimeSpan.FromMinutes(5)], delay.SnapshotDelays());
        Assert.Equal(0, harness.ConcreteQueue.PendingCount);
        Assert.Equal(1, harness.ConcreteQueue.DelayedCount);
        Assert.Empty(sink.Completed);

        hold.TrySetResult();
        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(1, Assert.Single(sink.Completed));
        Assert.Equal(0, harness.ConcreteQueue.DelayedCount);
    }

    [Fact]
    public async Task DelayedJob_IsCancelledWhenShutdownOccursBeforeDue()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRetryDelay delay = new(hold);
        WorkSink sink = new();

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options => options.ShutdownBehavior = ShutdownBehavior.Cancel,
            configureServices: services => services.AddSingleton(sink),
            configureBuilder: builder => builder.AddHandler<WorkJob, CompletingHandler>());

        await harness.StartAsync();
        await harness.Queue.EnqueueAsync(
            new WorkJob(1),
            options =>
            {
                options.Delay = TimeSpan.FromMinutes(5);
                options.JobId = "later";
            });

        await delay.FirstCall.Task.WaitAsync(WorkerHarness.Timeout);
        Assert.True(harness.ConcreteQueue.IsJobIdActive("later"));
        Assert.True(harness.ConcreteQueue.TryGetLifecycle("later", out JobLifecycle? lifecycle));
        Assert.Equal(JobLifecycleState.Delayed, lifecycle.State);

        await harness.StopAsync();

        Assert.Equal(JobLifecycleState.Cancelled, lifecycle.State);
        Assert.False(harness.ConcreteQueue.IsJobIdActive("later"));
        Assert.Empty(sink.Completed);
        Assert.Equal(0, harness.ConcreteQueue.DelayedCount);
    }

    [Fact]
    public async Task DrainShutdown_ProcessesReadyJobsAndCancelsDelayedJobs()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRetryDelay delay = new(hold);
        WorkSink sink = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options => options.ShutdownBehavior = ShutdownBehavior.Drain,
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        await harness.Queue.EnqueueAsync(
            new WorkJob(1),
            options => options.Delay = TimeSpan.FromMinutes(5));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        await harness.StopAsync();

        Assert.Equal(2, Assert.Single(sink.Completed));
        Assert.DoesNotContain(1, sink.Completed);
        Assert.Equal(0, harness.ConcreteQueue.DelayedCount);
    }

    [Fact]
    public async Task DelayedHighPriority_DoesNotOvertakeReadyLowPriorityUntilDue()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRetryDelay delay = new(hold);
        using ServiceProvider provider = CreateProvider(delay, options => options.PriorityFairnessLimit = 0);
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1), options => options.Priority = 0);
        await queue.EnqueueAsync(
            new WorkJob(2),
            options =>
            {
                options.Priority = 10;
                options.Delay = TimeSpan.FromMinutes(5);
            });

        Assert.Equal(1, queue.PendingCount);
        Assert.Equal(1, queue.DelayedCount);
        Assert.Equal(1, ReadId(queue));
        Assert.Equal(0, queue.PendingCount);

        hold.TrySetResult();
        JobEnvelope? promoted = await queue.DequeueAsync().AsTask().WaitAsync(WorkerHarness.Timeout);
        Assert.Equal(2, Assert.IsType<WorkJob>(promoted!.Job).Id);
    }

    [Fact]
    public async Task RetryDelay_IsSeparateFromEnqueueDelay()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.RetryCount = 1;
                options.RetryDelay = TimeSpan.FromMilliseconds(25);
                options.RetryBackoff = RetryBackoffStrategy.Constant;
            },
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailUntilThreshold(2));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, FailUntilHandler>());

        await harness.Queue.EnqueueAsync(
            new WorkJob(1),
            options => options.Delay = TimeSpan.FromMilliseconds(40));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(
            [TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(25)],
            delay.SnapshotDelays());
        Assert.Equal(2, tracker.Attempts[1]);
    }

    [Fact]
    public async Task RetryingJob_IsNotRequeuedAndKeepsTheWorker()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRetryDelay delay = new(hold);
        AttemptTracker tracker = new();
        ExecutionLog log = new();
        using CountdownEvent remaining = new(2);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.WorkerCount = 1;
                options.RetryCount = 1;
                options.RetryDelay = TimeSpan.FromMilliseconds(10);
                options.PriorityFairnessLimit = 0;
            },
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(log);
                services.AddSingleton(new FailUntilThreshold(2));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, PriorityRetryHandler>());

        await harness.StartAsync();
        await harness.Queue.EnqueueAsync(new WorkJob(1), options => options.Priority = 10);
        await delay.FirstCall.Task.WaitAsync(WorkerHarness.Timeout);
        await harness.Queue.EnqueueAsync(new WorkJob(2), options => options.Priority = 0);

        hold.TrySetResult();
        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal([1, 1, 2], log.Items.ToArray());
        Assert.Equal(2, tracker.Attempts[1]);
    }

    [Fact]
    public async Task DelayedJob_CountsTowardBoundedCapacity()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRetryDelay delay = new(hold);
        using ServiceProvider provider = CreateProvider(
            delay,
            options =>
            {
                options.Capacity = 1;
                options.QueueFullBehavior = QueueFullBehavior.Throw;
            });
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(
            new WorkJob(1),
            options => options.Delay = TimeSpan.FromMinutes(5));

        await delay.FirstCall.Task.WaitAsync(WorkerHarness.Timeout);
        await Assert.ThrowsAsync<SequoraQueueFullException>(() => queue.EnqueueAsync(new WorkJob(2)));
        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(1, queue.DelayedCount);
    }

    [Fact]
    public async Task ConcurrentProducers_WithPriorityAndDelay_AcceptAllJobs()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRetryDelay delay = new(hold);
        using ServiceProvider provider = CreateProvider(delay);
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        const int count = 80;

        await Task.WhenAll(Enumerable.Range(0, count).Select(id =>
            queue.EnqueueAsync(
                new WorkJob(id),
                options =>
                {
                    options.Priority = id % 3;
                    if (id % 2 == 0)
                    {
                        options.Delay = TimeSpan.FromMilliseconds(1);
                    }
                }))).WaitAsync(WorkerHarness.Timeout);

        int delayed = count / 2;
        Assert.Equal(delayed, queue.DelayedCount);
        Assert.Equal(count - delayed, queue.PendingCount);
    }

    private static int ReadId(JobQueue queue)
    {
        Assert.True(queue.TryReadPending(out JobEnvelope? envelope));
        return Assert.IsType<WorkJob>(envelope.Job).Id;
    }

    private static WorkerHarness CreateHarness(
        RecordingRetryDelay delay,
        Action<SequoraOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null,
        Action<ISequoraBuilder>? configureBuilder = null)
    {
        return WorkerHarness.Create(
            configure,
            services =>
            {
                services.AddSingleton<IRetryDelay>(delay);
                configureServices?.Invoke(services);
            },
            configureBuilder);
    }

    private static ServiceProvider CreateProvider(
        RecordingRetryDelay delay,
        Action<SequoraOptions>? configure = null)
    {
        ServiceCollection services = new();
        services.AddSequora(configure);
        services.AddSingleton<IRetryDelay>(delay);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }
}

internal sealed class ExecutionLog
{
    public ConcurrentQueue<int> Items { get; } = new();
}

internal sealed class PriorityRetryHandler(
    AttemptTracker tracker,
    FailUntilThreshold threshold,
    ExecutionLog log,
    CountdownEvent remaining) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        log.Items.Enqueue(job.Id);
        if (job.Id == 1)
        {
            int attempt = tracker.Increment(job.Id);
            if (attempt < threshold.AttemptCount)
            {
                throw new InvalidOperationException("retry");
            }
        }

        remaining.Signal();
        return Task.CompletedTask;
    }
}
