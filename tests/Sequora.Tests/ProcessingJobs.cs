using System.Collections.Concurrent;

namespace Sequora.Tests;

public sealed record WorkJob(int Id);

public sealed class WorkSink
{
    public ConcurrentBag<int> Completed { get; } = [];

    public ConcurrentBag<Guid> ScopeIds { get; } = [];
}

public sealed class CompletingHandler(WorkSink sink) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        sink.Completed.Add(job.Id);
        return Task.CompletedTask;
    }
}

public sealed class SignalingHandler(CountdownEvent remaining, WorkSink sink) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        sink.Completed.Add(job.Id);
        remaining.Signal();
        return Task.CompletedTask;
    }
}

public sealed class FailingThenCompletingHandler(WorkSink sink, CountdownEvent remaining) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        sink.Completed.Add(job.Id);
        remaining.Signal();
        if (job.Id == 1)
        {
            throw new InvalidOperationException("Job 1 failed on purpose.");
        }

        return Task.CompletedTask;
    }
}

public sealed class ConcurrencyGate : IDisposable
{
    public ConcurrencyGate(int workerCount)
    {
        Barrier = new Barrier(workerCount);
        Entered = new CountdownEvent(workerCount);
        Finished = new CountdownEvent(workerCount);
    }

    public Barrier Barrier { get; }

    public CountdownEvent Entered { get; }

    public CountdownEvent Finished { get; }

    public void Dispose()
    {
        Barrier.Dispose();
        Entered.Dispose();
        Finished.Dispose();
    }
}

public sealed class BarrierHandler(ConcurrencyGate gate) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        gate.Entered.Signal();
        gate.Barrier.SignalAndWait(cancellationToken);
        gate.Finished.Signal();
        return Task.CompletedTask;
    }
}

public sealed class ScopeMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}

public sealed class ScopedHandler(ScopeMarker marker, WorkSink sink, CountdownEvent remaining) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        sink.ScopeIds.Add(marker.Id);
        sink.Completed.Add(job.Id);
        remaining.Signal();
        return Task.CompletedTask;
    }
}

public sealed class HandlerStarted : TaskCompletionSource
{
    public HandlerStarted()
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
    }
}

public sealed class HandlerCancelled : TaskCompletionSource
{
    public HandlerCancelled()
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
    }
}

public sealed class HandlerAllowComplete : TaskCompletionSource
{
    public HandlerAllowComplete()
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
    }
}

public sealed class ObservedExecutionToken : TaskCompletionSource<CancellationToken>
{
    public ObservedExecutionToken()
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
    }
}

public sealed class BlockingHandler(
    HandlerStarted started,
    HandlerAllowComplete allowComplete,
    ObservedExecutionToken observedToken) : IJobHandler<WorkJob>
{
    public async Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        observedToken.TrySetResult(cancellationToken);
        started.TrySetResult();
        await allowComplete.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class CancelAwareHandler(
    HandlerStarted started,
    HandlerCancelled cancelled) : IJobHandler<WorkJob>
{
    public async Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        started.TrySetResult();
        try
        {
            TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await never.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled.TrySetResult();
            throw;
        }
    }
}

public sealed record UnhandledJob(int Id);
