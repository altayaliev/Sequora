using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sequora.Internal;

namespace Sequora.Tests;

internal sealed class JobWorkerLogCapture : ILogger<JobWorker>
{
    public ConcurrentBag<Exception> Exceptions { get; } = [];

    public ConcurrentBag<string> Messages { get; } = [];

    public ConcurrentBag<LogLevel> Levels { get; } = [];

    public TaskCompletionSource<SequoraHandlerNotFoundException> HandlerNotFound { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (exception is SequoraHandlerNotFoundException notFound)
        {
            HandlerNotFound.TrySetResult(notFound);
        }

        if (exception is not null)
        {
            Exceptions.Add(exception);
        }

        Levels.Add(logLevel);
        Messages.Add(formatter(state, exception));
    }
}

public sealed class ExecutionProbe : IDisposable
{
    public ExecutionProbe(int remainingCount, int disposedCount)
    {
        Remaining = new CountdownEvent(remainingCount);
        Disposed = new CountdownEvent(disposedCount);
    }

    public ConcurrentBag<Guid> HandlerIds { get; } = [];

    public ConcurrentBag<Guid> DependencyIds { get; } = [];

    public ConcurrentBag<string> Processed { get; } = [];

    public CountdownEvent Remaining { get; }

    public CountdownEvent Disposed { get; }

    public void Dispose()
    {
        Remaining.Dispose();
        Disposed.Dispose();
    }
}

public sealed class TrackingHandler : IJobHandler<WorkJob>
{
    public TrackingHandler(ExecutionProbe probe)
    {
        Probe = probe;
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }

    public ExecutionProbe Probe { get; }

    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        Probe.HandlerIds.Add(Id);
        Probe.Remaining.Signal();
        return Task.CompletedTask;
    }
}

public sealed class SingletonStamp
{
    public Guid Id { get; } = Guid.NewGuid();
}

public sealed class TransientStamp
{
    public Guid Id { get; } = Guid.NewGuid();
}

public sealed class LifetimeHandler(
    ExecutionProbe probe,
    SingletonStamp singleton,
    TransientStamp transient) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        probe.DependencyIds.Add(singleton.Id);
        probe.HandlerIds.Add(transient.Id);
        probe.Remaining.Signal();
        return Task.CompletedTask;
    }
}

public sealed class DisposableContext : IDisposable
{
    public DisposableContext(ExecutionProbe probe)
    {
        Probe = probe;
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }

    public ExecutionProbe Probe { get; }

    public void Dispose() => Probe.Disposed.Signal();
}

public sealed class AsyncDisposableContext : IAsyncDisposable
{
    public AsyncDisposableContext(ExecutionProbe probe)
    {
        Probe = probe;
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }

    public ExecutionProbe Probe { get; }

    public ValueTask DisposeAsync()
    {
        Probe.Disposed.Signal();
        return ValueTask.CompletedTask;
    }
}

public sealed class DbContextHandler(ExecutionProbe probe, DisposableContext context) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        probe.DependencyIds.Add(context.Id);
        probe.Remaining.Signal();
        return Task.CompletedTask;
    }
}

public sealed class AsyncDbContextHandler(ExecutionProbe probe, AsyncDisposableContext context) : IJobHandler<WorkJob>
{
    public Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        probe.DependencyIds.Add(context.Id);
        probe.Remaining.Signal();
        return Task.CompletedTask;
    }
}

public sealed record GenerateReportJob(string Name);

public sealed record SendNotificationJob(string User);

public sealed class GenerateReportHandler(ExecutionProbe probe) : IJobHandler<GenerateReportJob>
{
    public Task HandleAsync(GenerateReportJob job, CancellationToken cancellationToken)
    {
        probe.Processed.Add($"report:{job.Name}");
        probe.Remaining.Signal();
        return Task.CompletedTask;
    }
}

public sealed class SendNotificationHandler(ExecutionProbe probe) : IJobHandler<SendNotificationJob>
{
    public Task HandleAsync(SendNotificationJob job, CancellationToken cancellationToken)
    {
        probe.Processed.Add($"notify:{job.User}");
        probe.Remaining.Signal();
        return Task.CompletedTask;
    }
}

public sealed class EmailDispatchHandler(ExecutionProbe probe) : IJobHandler<SendEmailJob>
{
    public Task HandleAsync(SendEmailJob job, CancellationToken cancellationToken)
    {
        probe.Processed.Add($"email:{job.To}");
        probe.Remaining.Signal();
        return Task.CompletedTask;
    }
}

public sealed class ConcurrentEmailHandler(ConcurrencyGate gate) : IJobHandler<SendEmailJob>
{
    public Task HandleAsync(SendEmailJob job, CancellationToken cancellationToken)
    {
        gate.Entered.Signal();
        gate.Barrier.SignalAndWait(cancellationToken);
        gate.Finished.Signal();
        return Task.CompletedTask;
    }
}

public sealed class ConcurrentNotificationHandler(ConcurrencyGate gate) : IJobHandler<SendNotificationJob>
{
    public Task HandleAsync(SendNotificationJob job, CancellationToken cancellationToken)
    {
        gate.Entered.Signal();
        gate.Barrier.SignalAndWait(cancellationToken);
        gate.Finished.Signal();
        return Task.CompletedTask;
    }
}

public sealed class CancelAwareScopedHandler(
    HandlerStarted started,
    HandlerCancelled cancelled,
    DisposableContext context) : IJobHandler<WorkJob>
{
    public async Task HandleAsync(WorkJob job, CancellationToken cancellationToken)
    {
        _ = context.Id;
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
