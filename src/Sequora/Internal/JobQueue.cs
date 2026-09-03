using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sequora.Internal;

internal sealed class JobQueue : IJobQueue, IAsyncDisposable, IDisposable
{
    private readonly SequoraOptions _options;
    private readonly IRetryDelay _delay;
    private readonly ILogger<JobQueue> _logger;
    private readonly JobIdTracker _jobIds = new();
    private readonly ReadyQueue _ready;
    private readonly ConcurrentDictionary<JobEnvelope, byte> _delayed = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<JobEnvelope, Task> _delayedTasks = new(ReferenceEqualityComparer.Instance);
    private readonly SemaphoreSlim? _slots;
    private readonly CancellationTokenSource _stopping = new();
    private int _completed;
    private int _disposed;

    public JobQueue(IOptions<SequoraOptions> options, IRetryDelay delay)
        : this(options, delay, NullLogger<JobQueue>.Instance)
    {
    }

    [ActivatorUtilitiesConstructor]
    public JobQueue(IOptions<SequoraOptions> options, IRetryDelay delay, ILogger<JobQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _delay = delay;
        _logger = logger;
        _ready = new ReadyQueue(_options.PriorityFairnessLimit);
        if (_options.IsBounded)
        {
            _slots = new SemaphoreSlim(_options.Capacity, _options.Capacity);
        }
    }

    internal int PendingCount => _ready.Count;

    internal int DelayedCount => _delayed.Count;

    internal int TrackedJobIdCount => _jobIds.Count;

    internal int DelayedTaskCount => _delayedTasks.Count;

    internal bool IsCompleted => Volatile.Read(ref _completed) != 0;

    public Task EnqueueAsync<TJob>(TJob job, CancellationToken cancellationToken = default)
        where TJob : notnull
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        return EnqueueCoreAsync(job, configure: null, cancellationToken);
    }

    public Task EnqueueAsync<TJob>(
        TJob job,
        Action<EnqueueOptions> configure,
        CancellationToken cancellationToken = default)
        where TJob : notnull
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(configure);
        cancellationToken.ThrowIfCancellationRequested();
        return EnqueueCoreAsync(job, configure, cancellationToken);
    }

    internal void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        _stopping.Cancel();
        _ready.Complete();
        CancelDelayedJobs();
    }

    internal void AbandonPending()
    {
        while (_ready.TryDequeue(out JobEnvelope? ready))
        {
            ready.Lifecycle.MoveTo(JobLifecycleState.Cancelled);
            ReleaseJobId(ready);
            ReleaseSlot();
        }

        CancelDelayedJobs();
    }

    internal async Task WaitForBackgroundWorkAsync()
    {
        while (true)
        {
            Task[] tasks = [.. _delayedTasks.Values];
            if (tasks.Length == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Complete();
        await WaitForBackgroundWorkAsync().ConfigureAwait(false);
        DisposeResources();
    }

    public void Dispose()
    {
        Complete();
        try
        {
            WaitForBackgroundWorkAsync().GetAwaiter().GetResult();
        }
        catch (ObjectDisposedException)
        {
        }

        DisposeResources();
        GC.SuppressFinalize(this);
    }

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _slots?.Dispose();
        _stopping.Dispose();
    }

    private void CancelDelayedJobs()
    {
        foreach (JobEnvelope delayed in _delayed.Keys)
        {
            if (_delayed.TryRemove(delayed, out _))
            {
                delayed.Lifecycle.MoveTo(JobLifecycleState.Cancelled);
                ReleaseJobId(delayed);
                ReleaseSlot();
            }
        }
    }

    internal void ReleaseJobId(JobEnvelope envelope)
    {
        if (envelope.JobId is string jobId)
        {
            _ = _jobIds.TryRemove(jobId);
        }
    }

    internal bool IsJobIdActive(string jobId)
        => _jobIds.TryGet(jobId, out _);

    internal bool TryGetLifecycle(string jobId, [NotNullWhen(true)] out JobLifecycle? lifecycle)
        => _jobIds.TryGet(jobId, out lifecycle);

    internal async ValueTask<JobEnvelope?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        JobEnvelope? envelope = await _ready.DequeueAsync(cancellationToken).ConfigureAwait(false);
        if (envelope is not null)
        {
            ReleaseSlot();
        }

        return envelope;
    }

    internal bool TryPeekPending([NotNullWhen(true)] out JobEnvelope? envelope)
        => _ready.TryPeek(out envelope);

    internal bool TryReadPending([NotNullWhen(true)] out JobEnvelope? envelope)
    {
        if (!_ready.TryDequeue(out envelope))
        {
            return false;
        }

        ReleaseSlot();
        return true;
    }

    private Task EnqueueCoreAsync<TJob>(
        TJob job,
        Action<EnqueueOptions>? configure,
        CancellationToken cancellationToken)
        where TJob : notnull
    {
        ThrowIfCompleted();
        JobEnvelope envelope = CreateEnvelope(job, configure);
        RegisterJobId(envelope);
        return AcceptAsync(envelope, cancellationToken);
    }

    private void RegisterJobId(JobEnvelope envelope)
    {
        if (envelope.JobId is not string jobId)
        {
            return;
        }

        if (!_jobIds.TryAdd(jobId, envelope.Lifecycle))
        {
            throw new SequoraDuplicateJobException(jobId);
        }
    }

    private Task AcceptAsync(JobEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_slots is null && envelope.Delay <= TimeSpan.Zero)
        {
            if (IsCompleted)
            {
                ReleaseJobId(envelope);
                throw new SequoraStoppedException();
            }

            if (!_ready.TryEnqueue(envelope))
            {
                ReleaseJobId(envelope);
                throw new SequoraStoppedException();
            }

            return Task.CompletedTask;
        }

        return AcceptSlowAsync(envelope, cancellationToken);
    }

    private async Task AcceptSlowAsync(JobEnvelope envelope, CancellationToken cancellationToken)
    {
        bool reserved;
        try
        {
            reserved = await TryReserveSlotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            ReleaseJobId(envelope);
            throw;
        }

        if (!reserved)
        {
            ReleaseJobId(envelope);
            return;
        }

        if (IsCompleted)
        {
            ReleaseSlot();
            ReleaseJobId(envelope);
            throw new SequoraStoppedException();
        }

        if (envelope.Delay > TimeSpan.Zero)
        {
            _delayed[envelope] = 0;
            if (IsCompleted)
            {
                AbandonDelayed(envelope);
                throw new SequoraStoppedException();
            }

            ScheduleDelayed(envelope);
            return;
        }

        if (!_ready.TryEnqueue(envelope))
        {
            ReleaseSlot();
            ReleaseJobId(envelope);
            throw new SequoraStoppedException();
        }
    }

    private void ScheduleDelayed(JobEnvelope envelope)
    {
        Task work = RunDelayedSafeAsync(envelope);
        _delayedTasks[envelope] = work;
        if (work.IsCompleted)
        {
            _delayedTasks.TryRemove(envelope, out _);
        }
    }

    private async Task RunDelayedSafeAsync(JobEnvelope envelope)
    {
        try
        {
            await RunDelayedCoreAsync(envelope).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SequoraLog.DelayedSchedulerFailed(_logger, exception, envelope.JobType);
        }
        finally
        {
            _delayedTasks.TryRemove(envelope, out _);
        }
    }

    private async Task RunDelayedCoreAsync(JobEnvelope envelope)
    {
        try
        {
            await _delay.DelayAsync(envelope.Delay, _stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AbandonDelayed(envelope);
            return;
        }
        catch (Exception)
        {
            AbandonDelayed(envelope);
            return;
        }

        if (!_delayed.TryRemove(envelope, out _))
        {
            return;
        }

        envelope.Lifecycle.MoveTo(JobLifecycleState.Queued);
        if (!_ready.TryEnqueue(envelope))
        {
            envelope.Lifecycle.MoveTo(JobLifecycleState.Cancelled);
            ReleaseJobId(envelope);
            ReleaseSlot();
        }
    }

    private void AbandonDelayed(JobEnvelope envelope)
    {
        if (!_delayed.TryRemove(envelope, out _))
        {
            return;
        }

        envelope.Lifecycle.MoveTo(JobLifecycleState.Cancelled);
        ReleaseJobId(envelope);
        ReleaseSlot();
    }

    private async Task<bool> TryReserveSlotAsync(CancellationToken cancellationToken)
    {
        if (_slots is null)
        {
            return true;
        }

        if (_options.QueueFullBehavior == QueueFullBehavior.Throw)
        {
            if (_slots.Wait(0, cancellationToken))
            {
                return true;
            }

            ThrowIfCompleted();
            throw new SequoraQueueFullException(_options.Capacity);
        }

        if (_options.QueueFullBehavior == QueueFullBehavior.Drop)
        {
            ThrowIfCompleted();
            return _slots.Wait(0, cancellationToken);
        }

        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _stopping.Token);
            await _slots.WaitAsync(linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (IsCompleted)
        {
            throw new SequoraStoppedException();
        }
    }

    private void ReleaseSlot()
    {
        SemaphoreSlim? slots = _slots;
        if (slots is null)
        {
            return;
        }

        try
        {
            slots.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ThrowIfCompleted()
    {
        if (IsCompleted)
        {
            throw new SequoraStoppedException();
        }
    }

    private JobEnvelope CreateEnvelope<TJob>(TJob job, Action<EnqueueOptions>? configure)
        where TJob : notnull
    {
        EnqueueOptions? enqueueOptions = null;
        if (configure is not null)
        {
            enqueueOptions = new EnqueueOptions();
            configure(enqueueOptions);
        }

        EffectiveJobSettings settings = JobSettingsResolver.Resolve(_options, enqueueOptions);

        JobLifecycleState initial = settings.Delay > TimeSpan.Zero
            ? JobLifecycleState.Delayed
            : JobLifecycleState.Queued;

        return new JobEnvelope(
            job,
            typeof(TJob),
            settings.JobId,
            settings.RetryCount,
            settings.RetryDelay,
            settings.RetryBackoff,
            settings.MaxRetryDelay,
            settings.Priority,
            settings.Delay,
            new JobLifecycle(initial),
            (services, cancellationToken) =>
                JobHandlerInvoker.InvokeAsync(services, job, cancellationToken));
    }
}
