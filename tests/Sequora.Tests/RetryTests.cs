using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sequora.Internal;

namespace Sequora.Tests;

public sealed class RetryTests
{
    [Fact]
    public async Task SuccessOnFirstAttempt_DoesNotRetry()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options => options.RetryCount = 3,
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailUntilThreshold(1));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, FailUntilHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(1, tracker.Attempts[1]);
        Assert.Empty(delay.SnapshotDelays());
    }

    [Fact]
    public async Task FailureThenSuccess_RetriesOnce()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.RetryCount = 3;
                options.RetryDelay = TimeSpan.FromMilliseconds(100);
                options.RetryBackoff = RetryBackoffStrategy.Constant;
            },
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailUntilThreshold(2));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, FailUntilHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(2, tracker.Attempts[1]);
        Assert.Equal([TimeSpan.FromMilliseconds(100)], delay.SnapshotDelays());
    }

    [Fact]
    public async Task PermanentFailure_StopsAfterRetryCountAndDoesNotBlockLaterJobs()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        JobWorkerLogCapture log = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.RetryCount = 2;
                options.RetryDelay = TimeSpan.FromMilliseconds(50);
                options.RetryBackoff = RetryBackoffStrategy.Constant;
            },
            configureServices: services =>
            {
                services.AddSingleton<ILogger<JobWorker>>(log);
                services.AddSingleton(tracker);
                services.AddSingleton(new FailingJobIds(1));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SelectiveFailHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(3, tracker.Attempts[1]);
        Assert.Equal(1, tracker.Attempts[2]);
        Assert.Equal(2, delay.SnapshotDelays().Length);
        Assert.Contains(log.Exceptions, exception => exception is InvalidOperationException);
    }

    [Fact]
    public async Task RetryCount_IsRetriesAfterTheInitialAttempt()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.RetryCount = 3;
                options.RetryDelay = TimeSpan.FromMilliseconds(25);
                options.RetryBackoff = RetryBackoffStrategy.Constant;
            },
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailingJobIds(1));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SelectiveFailHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(4, tracker.Attempts[1]);
        Assert.Equal(3, delay.SnapshotDelays().Length);
    }

    [Fact]
    public async Task ZeroRetryCount_DoesNotRetry()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options => options.RetryCount = 0,
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailingJobIds(1));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SelectiveFailHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(1, tracker.Attempts[1]);
        Assert.Empty(delay.SnapshotDelays());
    }

    [Fact]
    public async Task FixedBackoff_UsesTheSameDelayForEveryRetry()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.RetryCount = 3;
                options.RetryDelay = TimeSpan.FromMilliseconds(100);
                options.RetryBackoff = RetryBackoffStrategy.Constant;
            },
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailingJobIds(1));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SelectiveFailHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(
            [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100)],
            delay.SnapshotDelays());
    }

    [Fact]
    public async Task ExponentialBackoff_DoublesTheDelayEachRetry()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.RetryCount = 3;
                options.RetryDelay = TimeSpan.FromMilliseconds(100);
                options.RetryBackoff = RetryBackoffStrategy.Exponential;
                options.MaxRetryDelay = TimeSpan.FromSeconds(10);
            },
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailingJobIds(1));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SelectiveFailHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(
            [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400)],
            delay.SnapshotDelays());
    }

    [Fact]
    public async Task MaxRetryDelay_CapsComputedBackoff()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.RetryCount = 4;
                options.RetryDelay = TimeSpan.FromMilliseconds(100);
                options.RetryBackoff = RetryBackoffStrategy.Exponential;
                options.MaxRetryDelay = TimeSpan.FromMilliseconds(250);
            },
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailingJobIds(1));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SelectiveFailHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(
            [
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(250)
            ],
            delay.SnapshotDelays());
    }

    [Fact]
    public async Task JobLevelOverrides_ReplaceGlobalRetrySettings()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.RetryCount = 5;
                options.RetryDelay = TimeSpan.FromMilliseconds(500);
                options.RetryBackoff = RetryBackoffStrategy.Exponential;
                options.MaxRetryDelay = TimeSpan.FromSeconds(5);
            },
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailingJobIds(1));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SelectiveFailHandler>());

        await harness.Queue.EnqueueAsync(
            new WorkJob(1),
            options =>
            {
                options.RetryCount = 1;
                options.RetryDelay = TimeSpan.FromMilliseconds(10);
                options.RetryBackoff = RetryBackoffStrategy.Constant;
                options.MaxRetryDelay = TimeSpan.FromMilliseconds(10);
            });
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(2, tracker.Attempts[1]);
        Assert.Equal([TimeSpan.FromMilliseconds(10)], delay.SnapshotDelays());
    }

    [Fact]
    public async Task CancelShutdown_DoesNotRetryDuringDelay()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRetryDelay delay = new(hold);
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.ShutdownBehavior = ShutdownBehavior.Cancel;
                options.RetryCount = 3;
                options.RetryDelay = TimeSpan.FromMilliseconds(100);
            },
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailingJobIds(1));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SelectiveFailHandler>());

        await harness.StartAsync();
        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await delay.FirstCall.Task.WaitAsync(WorkerHarness.Timeout);

        await harness.StopAsync();

        Assert.Equal(1, tracker.Attempts[1]);
        Assert.Single(delay.SnapshotDelays());
        Assert.False(remaining.IsSet);
    }

    [Fact]
    public async Task DrainShutdown_CompletesRetryAfterDelay()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRetryDelay delay = new(hold);
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.ShutdownBehavior = ShutdownBehavior.Drain;
                options.RetryCount = 3;
                options.RetryDelay = TimeSpan.FromMilliseconds(100);
            },
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailUntilThreshold(2));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, FailUntilHandler>());

        await harness.StartAsync();
        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await delay.FirstCall.Task.WaitAsync(WorkerHarness.Timeout);

        Task stopping = harness.Worker.StopAsync(CancellationToken.None);
        Assert.False(stopping.IsCompleted);
        Assert.Equal(1, tracker.Attempts[1]);

        hold.TrySetResult();
        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        await stopping.WaitAsync(WorkerHarness.Timeout);

        Assert.Equal(2, tracker.Attempts[1]);
    }

    [Fact]
    public async Task ScopeIsDisposedBeforeRetryDelay()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRetryDelay delay = new(hold);
        using ExecutionProbe probe = new(remainingCount: 1, disposedCount: 1);
        probe.Remaining.Signal();

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.ShutdownBehavior = ShutdownBehavior.Cancel;
                options.RetryCount = 3;
            },
            configureServices: services =>
            {
                services.AddSingleton(probe);
                services.AddScoped<DisposableContext>();
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, DisposeThenFailHandler>());

        await harness.StartAsync();
        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await delay.FirstCall.Task.WaitAsync(WorkerHarness.Timeout);

        Assert.True(probe.Disposed.Wait(WorkerHarness.Timeout));

        await harness.StopAsync();
    }

    [Fact]
    public async Task EachRetryAttempt_UsesANewScope()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options => options.RetryCount = 2,
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailUntilThreshold(3));
                services.AddSingleton(remaining);
                services.AddScoped<ScopeMarker>();
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, ScopedFailUntilHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(3, tracker.Attempts[1]);
        Assert.Equal(3, tracker.ScopeIds.Distinct().Count());
    }

    [Fact]
    public async Task MissingHandler_IsNotRetried()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        JobWorkerLogCapture log = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options => options.RetryCount = 3,
            configureServices: services =>
            {
                services.AddSingleton<ILogger<JobWorker>>(log);
                services.AddSingleton(tracker);
                services.AddSingleton(new FailUntilThreshold(1));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, FailUntilHandler>());

        await harness.Queue.EnqueueAsync(new UnhandledJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        await log.HandlerNotFound.Task.WaitAsync(WorkerHarness.Timeout);
        Assert.Empty(delay.SnapshotDelays());
        Assert.Equal(1, tracker.Attempts[2]);
    }

    [Fact]
    public async Task ConcurrentJobs_KeepIndependentRetryState()
    {
        RecordingRetryDelay delay = new();
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(2);

        await using WorkerHarness harness = CreateHarness(
            delay,
            configure: options =>
            {
                options.WorkerCount = 2;
                options.RetryCount = 2;
                options.RetryDelay = TimeSpan.FromMilliseconds(25);
                options.RetryBackoff = RetryBackoffStrategy.Constant;
            },
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailUntilThreshold(3));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, FailUntilHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(3, tracker.Attempts[1]);
        Assert.Equal(3, tracker.Attempts[2]);
        Assert.Equal(4, delay.SnapshotDelays().Length);
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
}
