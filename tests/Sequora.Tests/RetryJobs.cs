using System.Collections.Concurrent;
using Sequora.Internal;

namespace Sequora.Tests;

internal sealed class RecordingRetryDelay : IRetryDelay
{
    private readonly TaskCompletionSource? _hold;

    public RecordingRetryDelay(TaskCompletionSource? hold = null)
    {
        _hold = hold;
    }

    public ConcurrentQueue<TimeSpan> RequestedDelays { get; } = new();

    public TaskCompletionSource FirstCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TimeSpan[] SnapshotDelays() => [.. RequestedDelays];

    public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        RequestedDelays.Enqueue(delay);
        FirstCall.TrySetResult();
        if (_hold is not null)
        {
            await _hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed class AttemptTracker
{
    public ConcurrentDictionary<int, int> Attempts { get; } = new();

    public ConcurrentBag<Guid> ScopeIds { get; } = [];

    public int Increment(int jobId) =>
        Attempts.AddOrUpdate(jobId, 1, static (_, count) => count + 1);
}

internal sealed class FailUntilThreshold(int attemptCount)
{
    public int AttemptCount { get; } = attemptCount;
}

internal sealed class FailingJobIds
{
    public FailingJobIds(params int[] ids) => Ids = [.. ids];

    public HashSet<int> Ids { get; }
}

internal sealed class FailUntilHandler(
    AttemptTracker tracker,
    FailUntilThreshold threshold,
    CountdownEvent remaining) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        int attempt = tracker.Increment(job.Id);
        if (attempt < threshold.AttemptCount)
        {
            throw new InvalidOperationException($"Job {job.Id} failed on attempt {attempt}.");
        }

        remaining.Signal();
        return Task.CompletedTask;
    }
}

internal sealed class SelectiveFailHandler(
    AttemptTracker tracker,
    FailingJobIds failing,
    CountdownEvent remaining) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        tracker.Increment(job.Id);
        if (failing.Ids.Contains(job.Id))
        {
            throw new InvalidOperationException($"Job {job.Id} failed on purpose.");
        }

        remaining.Signal();
        return Task.CompletedTask;
    }
}

internal sealed class ScopedFailUntilHandler(
    AttemptTracker tracker,
    FailUntilThreshold threshold,
    ScopeMarker marker,
    CountdownEvent remaining) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        tracker.ScopeIds.Add(marker.Id);
        int attempt = tracker.Increment(job.Id);
        if (attempt < threshold.AttemptCount)
        {
            throw new InvalidOperationException($"Job {job.Id} failed on attempt {attempt}.");
        }

        remaining.Signal();
        return Task.CompletedTask;
    }
}

internal sealed class DisposeThenFailHandler(DisposableContext context) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        _ = context.Id;
        throw new InvalidOperationException("Job failed so the scope can be disposed before retry delay.");
    }
}
