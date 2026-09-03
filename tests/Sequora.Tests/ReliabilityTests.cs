using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sequora.Internal;

namespace Sequora.Tests;

public sealed class ReliabilityTests
{
    [Fact]
    public async Task ManyConcurrentProducersAndWorkers_ProcessEveryJobOnce()
    {
        const int producerCount = 8;
        const int jobsPerProducer = 250;
        const int count = producerCount * jobsPerProducer;
        WorkSink sink = new();
        using CountdownEvent remaining = new(count);

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.WorkerCount = 8,
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        await harness.StartAsync();

        await Task.WhenAll(Enumerable.Range(0, producerCount).Select(producer =>
            Task.WhenAll(Enumerable.Range(0, jobsPerProducer).Select(index =>
                harness.Queue.EnqueueAsync(new WorkJob((producer * jobsPerProducer) + index))))))
            .WaitAsync(WorkerHarness.Timeout);

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(count, sink.Completed.Count);
        Assert.Equal(count, sink.Completed.Distinct().Count());
        Assert.Equal(0, harness.ConcreteQueue.PendingCount);
        Assert.Equal(0, harness.ConcreteQueue.TrackedJobIdCount);
    }

    [Fact]
    public async Task ThousandsOfJobs_CompleteWithoutLeavingTrackedState()
    {
        const int count = 2000;
        WorkSink sink = new();
        using CountdownEvent remaining = new(count);

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.WorkerCount = 8,
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        await harness.StartAsync();
        await Task.WhenAll(Enumerable.Range(0, count).Select(id => harness.Queue.EnqueueAsync(new WorkJob(id))))
            .WaitAsync(WorkerHarness.Timeout);

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(count, sink.Completed.Distinct().Count());
        Assert.Equal(0, harness.ConcreteQueue.DelayedCount);
        Assert.Equal(0, harness.ConcreteQueue.DelayedTaskCount);
        Assert.Equal(0, harness.ConcreteQueue.TrackedJobIdCount);
    }

    [Fact]
    public async Task ConcurrentDuplicateEnqueue_AcceptsExactlyOneJob()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        const int attempts = 64;
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrived = 0;
        int accepted = 0;
        int rejected = 0;

        Task[] tasks = [.. Enumerable.Range(0, attempts).Select(_ => Task.Run(async () =>
        {
            if (Interlocked.Increment(ref arrived) == attempts)
            {
                ready.TrySetResult();
            }

            await ready.Task.WaitAsync(WorkerHarness.Timeout);
            try
            {
                await queue.EnqueueAsync(new WorkJob(1), options => options.JobId = "same");
                Interlocked.Increment(ref accepted);
            }
            catch (SequoraDuplicateJobException)
            {
                Interlocked.Increment(ref rejected);
            }
        }))];

        await Task.WhenAll(tasks).WaitAsync(WorkerHarness.Timeout);

        Assert.Equal(1, accepted);
        Assert.Equal(attempts - 1, rejected);
        Assert.Equal(1, queue.PendingCount);
        Assert.Equal(1, queue.TrackedJobIdCount);
    }

    [Fact]
    public async Task BoundedQueueContention_DrainsEveryAcceptedJob()
    {
        const int count = 400;
        WorkSink sink = new();
        using CountdownEvent remaining = new(count);

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options =>
            {
                options.WorkerCount = 4;
                options.Capacity = 16;
                options.QueueFullBehavior = QueueFullBehavior.Wait;
            },
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        await harness.StartAsync();
        await Task.WhenAll(Enumerable.Range(0, count).Select(id => harness.Queue.EnqueueAsync(new WorkJob(id))))
            .WaitAsync(WorkerHarness.Timeout);

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(count, sink.Completed.Distinct().Count());
        Assert.Equal(0, harness.ConcreteQueue.PendingCount);
    }

    [Fact]
    public async Task RetryUnderConcurrency_DoesNotCorruptAttemptState()
    {
        const int count = 200;
        WorkSink sink = new();
        using CountdownEvent remaining = new(count);
        AttemptTracker tracker = new();

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options =>
            {
                options.WorkerCount = 8;
                options.RetryCount = 1;
                options.RetryDelay = TimeSpan.Zero;
                options.MaxRetryDelay = TimeSpan.Zero;
            },
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
                services.AddSingleton(tracker);
                services.AddSingleton(new FailUntilThreshold(2));
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, FailUntilThenCompleteHandler>());

        await harness.StartAsync();
        await Task.WhenAll(Enumerable.Range(0, count).Select(id => harness.Queue.EnqueueAsync(new WorkJob(id))))
            .WaitAsync(WorkerHarness.Timeout);

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(count, sink.Completed.Distinct().Count());
        Assert.All(Enumerable.Range(0, count), id => Assert.Equal(2, tracker.Attempts[id]));
        Assert.Equal(0, harness.ConcreteQueue.TrackedJobIdCount);
    }

    [Fact]
    public async Task HandlerException_DoesNotKillTheWorker()
    {
        WorkSink sink = new();
        using CountdownEvent remaining = new(8);

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options =>
            {
                options.WorkerCount = 1;
                options.RetryCount = 0;
            },
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, FailEvenIdsHandler>());

        await harness.StartAsync();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(id => harness.Queue.EnqueueAsync(new WorkJob(id))))
            .WaitAsync(WorkerHarness.Timeout);

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal([1, 3, 5, 7], sink.Completed.OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task OneWorkerFailure_DoesNotStopOtherWorkers()
    {
        using ConcurrencyGate gate = new(2);
        WorkSink sink = new();
        using CountdownEvent remaining = new(6);

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options =>
            {
                options.WorkerCount = 2;
                options.RetryCount = 0;
            },
            configureServices: services =>
            {
                services.AddSingleton(gate);
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, BarrierThenFailFirstHandler>());

        await Task.WhenAll(Enumerable.Range(0, 6).Select(id => harness.Queue.EnqueueAsync(new WorkJob(id))))
            .WaitAsync(WorkerHarness.Timeout);
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Contains(1, sink.Completed);
        Assert.DoesNotContain(0, sink.Completed);
        Assert.Equal(5, sink.Completed.Distinct().Count());
    }

    [Fact]
    public async Task CancelShutdown_CancelsAllInFlightHandlers()
    {
        using CancelStartGate started = new(4);
        using CancelFinishGate cancelled = new(4);

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options =>
            {
                options.WorkerCount = 4;
                options.ShutdownBehavior = ShutdownBehavior.Cancel;
            },
            configureServices: services =>
            {
                services.AddSingleton(started);
                services.AddSingleton(cancelled);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, CountingCancelHandler>());

        await harness.StartAsync();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(id => harness.Queue.EnqueueAsync(new WorkJob(id))))
            .WaitAsync(WorkerHarness.Timeout);

        Assert.True(started.Event.Wait(WorkerHarness.Timeout));
        await harness.StopAsync();
        Assert.True(cancelled.Event.Wait(WorkerHarness.Timeout));
        Assert.Equal(0, harness.ConcreteQueue.PendingCount);
        Assert.Equal(0, harness.ConcreteQueue.DelayedTaskCount);
    }

    [Fact]
    public async Task SimultaneousShutdown_UnblocksProducersAndFinishesBackgroundWork()
    {
        WorkSink sink = new();
        int accepted = 0;

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options =>
            {
                options.WorkerCount = 4;
                options.Capacity = 32;
                options.QueueFullBehavior = QueueFullBehavior.Wait;
            },
            configureServices: services => services.AddSingleton(sink),
            configureBuilder: builder => builder.AddHandler<WorkJob, CompletingHandler>());

        await harness.StartAsync();

        Task[] producers = [.. Enumerable.Range(0, 8).Select(producer => Task.Run(async () =>
        {
            for (int index = 0; index < 80; index++)
            {
                try
                {
                    await harness.Queue.EnqueueAsync(new WorkJob((producer * 80) + index))
                        .WaitAsync(WorkerHarness.Timeout);
                    Interlocked.Increment(ref accepted);
                }
                catch (SequoraStoppedException)
                {
                    return;
                }
            }
        }))];

        Task stopping = harness.StopAsync();
        await Task.WhenAll(producers).WaitAsync(WorkerHarness.Timeout);
        await stopping.WaitAsync(WorkerHarness.Timeout);

        Assert.InRange(accepted, 0, 640);
        Assert.True(harness.ConcreteQueue.IsCompleted);
        Assert.Equal(0, harness.ConcreteQueue.DelayedCount);
        Assert.Equal(0, harness.ConcreteQueue.DelayedTaskCount);
        await Assert.ThrowsAsync<SequoraStoppedException>(() => harness.Queue.EnqueueAsync(new WorkJob(-1)));
    }

    [Fact]
    public async Task Complete_CancelsDelayedJobsAndObservesSchedulerTasks()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(
            new WorkJob(1),
            options => options.Delay = TimeSpan.FromMinutes(5));

        Assert.Equal(1, queue.DelayedCount);

        queue.Complete();
        await queue.WaitForBackgroundWorkAsync().WaitAsync(WorkerHarness.Timeout);

        Assert.Equal(0, queue.DelayedCount);
        Assert.Equal(0, queue.DelayedTaskCount);
        Assert.Equal(0, queue.TrackedJobIdCount);
    }

    [Fact]
    public async Task Logging_DoesNotIncludeJobPayloads()
    {
        JobWorkerLogCapture log = new();
        WorkSink sink = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.RetryCount = 0,
            configureServices: services =>
            {
                services.AddSingleton<ILogger<JobWorker>>(log);
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder =>
            {
                builder.AddHandler<SecretJob, SecretFailingHandler>();
                builder.AddHandler<WorkJob, SignalingHandler>();
            });

        await harness.Queue.EnqueueAsync(new SecretJob("super-secret-token", "password"));
        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Contains(log.Exceptions, exception => exception is InvalidOperationException);
        Assert.All(log.Messages, message =>
        {
            Assert.DoesNotContain("super-secret-token", message, StringComparison.Ordinal);
            Assert.DoesNotContain("password", message, StringComparison.Ordinal);
        });
        Assert.Contains(log.Messages, message => message.Contains(nameof(SecretJob), StringComparison.Ordinal));
    }
}

internal sealed record SecretJob(string Token, string Password);

internal sealed class SecretFailingHandler : IJobHandler<SecretJob>
{
    public Task HandleAsync(SecretJob job, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Handler failed.");
}

internal sealed class FailEvenIdsHandler(WorkSink sink, CountdownEvent remaining) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        remaining.Signal();
        if (job.Id % 2 == 0)
        {
            throw new InvalidOperationException($"Job {job.Id} failed on purpose.");
        }

        sink.Completed.Add(job.Id);
        return Task.CompletedTask;
    }
}

internal sealed class BarrierThenFailFirstHandler(
    ConcurrencyGate gate,
    WorkSink sink,
    CountdownEvent remaining) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        if (job.Id is 0 or 1)
        {
            gate.Entered.Signal();
            gate.Barrier.SignalAndWait(cancellationToken);
            gate.Finished.Signal();
        }

        remaining.Signal();
        if (job.Id == 0)
        {
            throw new InvalidOperationException("Worker isolation probe.");
        }

        sink.Completed.Add(job.Id);
        return Task.CompletedTask;
    }
}

internal sealed class CancelStartGate(int count) : IDisposable
{
    public CountdownEvent Event { get; } = new(count);

    public void Dispose() => Event.Dispose();
}

internal sealed class CancelFinishGate(int count) : IDisposable
{
    public CountdownEvent Event { get; } = new(count);

    public void Dispose() => Event.Dispose();
}

internal sealed class CountingCancelHandler(CancelStartGate started, CancelFinishGate cancelled) : IJobHandler<WorkJob>
{
    public async Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        started.Event.Signal();
        try
        {
            TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await never.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled.Event.Signal();
            throw;
        }
    }
}

internal sealed class FailUntilThenCompleteHandler(
    AttemptTracker tracker,
    FailUntilThreshold threshold,
    WorkSink sink,
    CountdownEvent remaining) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        int attempt = tracker.Increment(job.Id);
        if (attempt < threshold.AttemptCount)
        {
            throw new InvalidOperationException($"Job {job.Id} failed on attempt {attempt}.");
        }

        sink.Completed.Add(job.Id);
        remaining.Signal();
        return Task.CompletedTask;
    }
}
