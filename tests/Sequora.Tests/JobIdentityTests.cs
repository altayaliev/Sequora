using Microsoft.Extensions.DependencyInjection;
using Sequora.Internal;

namespace Sequora.Tests;

public sealed class JobIdentityTests
{
    [Fact]
    public async Task UniqueJobIds_AreBothAccepted()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1), options => options.JobId = "invoice-1");
        await queue.EnqueueAsync(new WorkJob(2), options => options.JobId = "invoice-2");

        Assert.Equal(2, queue.PendingCount);
        Assert.Equal(2, queue.TrackedJobIdCount);
        Assert.True(queue.IsJobIdActive("invoice-1"));
        Assert.True(queue.IsJobIdActive("invoice-2"));
    }

    [Fact]
    public async Task DuplicateJobId_ThrowsAndDoesNotEnqueueTheSecondJob()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1), options => options.JobId = "invoice-email-123");

        SequoraDuplicateJobException exception = await Assert.ThrowsAsync<SequoraDuplicateJobException>(() =>
            queue.EnqueueAsync(new WorkJob(2), options => options.JobId = "invoice-email-123"));

        Assert.Equal("invoice-email-123", exception.JobId);
        Assert.Contains("invoice-email-123", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, queue.PendingCount);
        Assert.Equal(1, queue.TrackedJobIdCount);
        Assert.True(queue.TryPeekPending(out JobEnvelope? envelope));
        Assert.Equal(1, Assert.IsType<WorkJob>(envelope.Job).Id);
    }

    [Fact]
    public async Task DuplicateJobId_IsOrdinalAndCaseSensitive()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1), options => options.JobId = "Invoice");
        await queue.EnqueueAsync(new WorkJob(2), options => options.JobId = "invoice");

        Assert.Equal(2, queue.PendingCount);
        Assert.Equal(2, queue.TrackedJobIdCount);
    }

    [Fact]
    public async Task DuplicateJobId_IsRejectedAcrossJobTypes()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1), options => options.JobId = "shared");

        await Assert.ThrowsAsync<SequoraDuplicateJobException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options => options.JobId = "shared"));

        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public async Task AnonymousJobs_AreNotTrackedAndMayRepeat()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1));
        await queue.EnqueueAsync(new WorkJob(1));

        Assert.Equal(2, queue.PendingCount);
        Assert.Equal(0, queue.TrackedJobIdCount);
    }

    [Fact]
    public async Task ConcurrentDuplicateEnqueue_AcceptsOnlyOneJob()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        TaskCompletionSource bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrived = 0;

        async Task<bool> TryEnqueueAsync()
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                bothReady.TrySetResult();
            }

            await bothReady.Task.WaitAsync(WorkerHarness.Timeout);
            try
            {
                await queue.EnqueueAsync(new WorkJob(1), options => options.JobId = "same");
                return true;
            }
            catch (SequoraDuplicateJobException)
            {
                return false;
            }
        }

        bool[] results = await Task.WhenAll(TryEnqueueAsync(), TryEnqueueAsync());

        Assert.Equal(1, results.Count(accepted => accepted));
        Assert.Equal(1, queue.PendingCount);
        Assert.Equal(1, queue.TrackedJobIdCount);
        Assert.True(queue.IsJobIdActive("same"));
    }

    [Fact]
    public async Task QueueFullThrow_ReleasesTheJobIdSoItCanBeReused()
    {
        using ServiceProvider provider = SequoraProvider.Create(options =>
        {
            options.Capacity = 1;
            options.QueueFullBehavior = QueueFullBehavior.Throw;
        });
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1));

        await Assert.ThrowsAsync<SequoraQueueFullException>(() =>
            queue.EnqueueAsync(new WorkJob(2), options => options.JobId = "retry-me"));

        Assert.Equal(0, queue.TrackedJobIdCount);
        Assert.True(queue.TryReadPending(out _));

        await queue.EnqueueAsync(new WorkJob(3), options => options.JobId = "retry-me");
        Assert.True(queue.IsJobIdActive("retry-me"));
    }

    [Fact]
    public async Task SuccessfulCompletion_ReleasesTheJobIdForReuse()
    {
        WorkSink sink = new();
        using CountdownEvent remaining = new(2);
        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1), options => options.JobId = "done-1");
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.False(harness.ConcreteQueue.IsJobIdActive("done-1"));
        Assert.Equal(0, harness.ConcreteQueue.TrackedJobIdCount);

        remaining.Reset(1);
        await harness.Queue.EnqueueAsync(new WorkJob(3), options => options.JobId = "done-1");
        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Contains(3, sink.Completed);
    }

    [Fact]
    public async Task FinalFailure_ReleasesTheJobId()
    {
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);
        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.RetryCount = 0,
            configureServices: services =>
            {
                services.AddSingleton(tracker);
                services.AddSingleton(new FailingJobIds(1));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SelectiveFailHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1), options => options.JobId = "fail-1");
        Assert.True(harness.ConcreteQueue.TryGetLifecycle("fail-1", out JobLifecycle? lifecycle));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(1, tracker.Attempts[1]);
        Assert.Equal(JobLifecycleState.Failed, lifecycle.State);
        Assert.False(harness.ConcreteQueue.IsJobIdActive("fail-1"));
        Assert.Equal(0, harness.ConcreteQueue.TrackedJobIdCount);
    }

    [Fact]
    public async Task Retries_RetainTheSameJobId()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRetryDelay delay = new(hold);
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options =>
            {
                options.RetryCount = 3;
                options.RetryDelay = TimeSpan.FromMilliseconds(50);
            },
            configureServices: services =>
            {
                services.AddSingleton<IRetryDelay>(delay);
                services.AddSingleton(tracker);
                services.AddSingleton(new FailUntilThreshold(2));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, FailUntilHandler>());

        await harness.StartAsync();
        await harness.Queue.EnqueueAsync(new WorkJob(1), options => options.JobId = "retry-id");
        await delay.FirstCall.Task.WaitAsync(WorkerHarness.Timeout);

        Assert.True(harness.ConcreteQueue.TryGetLifecycle("retry-id", out JobLifecycle? lifecycle));
        Assert.Equal(JobLifecycleState.Retrying, lifecycle.State);
        Assert.Equal("retry-id", GetActiveJobId(harness, "retry-id"));

        await Assert.ThrowsAsync<SequoraDuplicateJobException>(() =>
            harness.Queue.EnqueueAsync(new WorkJob(99), options => options.JobId = "retry-id"));

        hold.TrySetResult();
        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        remaining.Reset(1);
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(2, tracker.Attempts[1]);
        Assert.False(harness.ConcreteQueue.IsJobIdActive("retry-id"));
    }

    [Fact]
    public async Task Lifecycle_TransitionsQueuedProcessingRetryingThenReleases()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRetryDelay delay = new(hold);
        HandlerStarted started = new();
        HandlerAllowComplete allowComplete = new();
        AttemptTracker tracker = new();
        using CountdownEvent remaining = new(1);

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options =>
            {
                options.RetryCount = 3;
                options.RetryDelay = TimeSpan.FromMilliseconds(25);
            },
            configureServices: services =>
            {
                services.AddSingleton<IRetryDelay>(delay);
                services.AddSingleton(started);
                services.AddSingleton(allowComplete);
                services.AddSingleton(tracker);
                services.AddSingleton(new FailUntilThreshold(2));
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, LifecycleFailUntilHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1), options => options.JobId = "life");
        Assert.True(harness.ConcreteQueue.TryGetLifecycle("life", out JobLifecycle? queued));
        Assert.Equal(JobLifecycleState.Queued, queued.State);
        Assert.Equal([JobLifecycleState.Queued], queued.SnapshotHistory());

        await harness.StartAsync();
        await started.Task.WaitAsync(WorkerHarness.Timeout);

        Assert.True(harness.ConcreteQueue.TryGetLifecycle("life", out JobLifecycle? processing));
        Assert.Equal(JobLifecycleState.Processing, processing.State);
        allowComplete.TrySetResult();

        await delay.FirstCall.Task.WaitAsync(WorkerHarness.Timeout);
        Assert.True(harness.ConcreteQueue.TryGetLifecycle("life", out JobLifecycle? retrying));
        Assert.Equal(JobLifecycleState.Retrying, retrying.State);
        Assert.Equal(
            [
                JobLifecycleState.Queued,
                JobLifecycleState.Processing,
                JobLifecycleState.Retrying
            ],
            retrying.SnapshotHistory());

        hold.TrySetResult();
        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        remaining.Reset(1);
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(JobLifecycleState.Completed, retrying.State);
        Assert.Equal(
            [
                JobLifecycleState.Queued,
                JobLifecycleState.Processing,
                JobLifecycleState.Retrying,
                JobLifecycleState.Processing,
                JobLifecycleState.Completed
            ],
            retrying.SnapshotHistory());
        Assert.False(harness.ConcreteQueue.IsJobIdActive("life"));
    }

    [Fact]
    public async Task Cancellation_ReleasesTheJobId()
    {
        HandlerStarted started = new();
        HandlerCancelled cancelled = new();

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.ShutdownBehavior = ShutdownBehavior.Cancel,
            configureServices: services =>
            {
                services.AddSingleton(started);
                services.AddSingleton(cancelled);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, CancelAwareHandler>());

        await harness.StartAsync();
        await harness.Queue.EnqueueAsync(new WorkJob(1), options => options.JobId = "cancel-me");
        await started.Task.WaitAsync(WorkerHarness.Timeout);
        Assert.True(harness.ConcreteQueue.TryGetLifecycle("cancel-me", out JobLifecycle? lifecycle));
        Assert.Equal(JobLifecycleState.Processing, lifecycle.State);

        await harness.StopAsync();

        await cancelled.Task.WaitAsync(WorkerHarness.Timeout);
        Assert.Equal(JobLifecycleState.Cancelled, lifecycle.State);
        Assert.False(harness.ConcreteQueue.IsJobIdActive("cancel-me"));
        Assert.Equal(0, harness.ConcreteQueue.TrackedJobIdCount);
    }

    [Fact]
    public async Task AbandonPending_CancelsQueuedJobIds()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1), options => options.JobId = "left-behind");
        Assert.True(queue.TryGetLifecycle("left-behind", out JobLifecycle? lifecycle));
        Assert.Equal(1, queue.TrackedJobIdCount);

        queue.Complete();
        queue.AbandonPending();

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(0, queue.TrackedJobIdCount);
        Assert.False(queue.IsJobIdActive("left-behind"));
        Assert.Equal(JobLifecycleState.Cancelled, lifecycle.State);
    }

    [Fact]
    public async Task CompletedJobIds_AreNotRetained()
    {
        WorkSink sink = new();
        using CountdownEvent remaining = new(21);
        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        for (int id = 0; id < 20; id++)
        {
            await harness.Queue.EnqueueAsync(new WorkJob(id), options => options.JobId = $"job-{id}");
        }

        await harness.Queue.EnqueueAsync(new WorkJob(100));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(0, harness.ConcreteQueue.TrackedJobIdCount);
        Assert.Equal(21, sink.Completed.Count);
    }

    private static string GetActiveJobId(WorkerHarness harness, string jobId)
    {
        Assert.True(harness.ConcreteQueue.IsJobIdActive(jobId));
        return jobId;
    }
}

internal sealed class LifecycleFailUntilHandler(
    AttemptTracker tracker,
    FailUntilThreshold threshold,
    HandlerStarted started,
    HandlerAllowComplete allowComplete,
    CountdownEvent remaining) : IJobHandler<WorkJob>
{
    public async Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        if (job.Id != 1)
        {
            remaining.Signal();
            return;
        }

        int attempt = tracker.Increment(job.Id);
        if (attempt == 1)
        {
            started.TrySetResult();
            await allowComplete.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (attempt < threshold.AttemptCount)
        {
            throw new InvalidOperationException($"Job {job.Id} failed on attempt {attempt}.");
        }

        remaining.Signal();
    }
}
