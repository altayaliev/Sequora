using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sequora.Internal;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
internal sealed class JobWorker : BackgroundService
{
    private readonly JobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SequoraOptions _options;
    private readonly IRetryDelay _retryDelay;
    private readonly ILogger<JobWorker> _logger;

    internal int WorkerCount => _options.WorkerCount;

    public JobWorker(
        IJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<SequoraOptions> options,
        IRetryDelay retryDelay,
        ILogger<JobWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(retryDelay);
        ArgumentNullException.ThrowIfNull(logger);

        _queue = queue as JobQueue
            ?? throw new InvalidOperationException(
                $"The registered {nameof(IJobQueue)} must be the Sequora in-memory queue.");
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _retryDelay = retryDelay;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenRegistration registration = stoppingToken.Register(_queue.Complete);

        int workerCount = _options.WorkerCount;
        SequoraLog.WorkersStarted(_logger, workerCount);

        Task[] workers = new Task[workerCount];
        for (int i = 0; i < workers.Length; i++)
        {
            workers[i] = Task.Run(() => RunWorkerAsync(stoppingToken), CancellationToken.None);
        }

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SequoraLog.WorkerUnexpectedError(_logger, exception);
        }
        finally
        {
            _queue.Complete();
            await _queue.WaitForBackgroundWorkAsync().ConfigureAwait(false);
            _queue.AbandonPending();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Complete();
        try
        {
            await _queue.WaitForBackgroundWorkAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        _queue.Complete();
        base.Dispose();
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        CancellationToken waitToken = _options.ShutdownBehavior == ShutdownBehavior.Cancel
            ? stoppingToken
            : CancellationToken.None;
        CancellationToken executionToken = waitToken;

        while (true)
        {
            JobEnvelope? envelope;
            try
            {
                envelope = await _queue.DequeueAsync(waitToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (waitToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                SequoraLog.WorkerUnexpectedError(_logger, exception);
                if (_queue.IsCompleted)
                {
                    return;
                }

                continue;
            }

            if (envelope is null)
            {
                return;
            }

            try
            {
                await ExecuteJobAsync(envelope, executionToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                SequoraLog.WorkerUnexpectedError(_logger, exception);
                if (_queue.IsCompleted || executionToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task ExecuteJobAsync(JobEnvelope envelope, CancellationToken cancellationToken)
    {
        envelope.Lifecycle.MoveTo(JobLifecycleState.Processing);
        try
        {
            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                    await envelope.ExecuteAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
                    envelope.Lifecycle.MoveTo(JobLifecycleState.Completed);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    envelope.Lifecycle.MoveTo(JobLifecycleState.Cancelled);
                    throw;
                }
                catch (SequoraHandlerNotFoundException exception)
                {
                    envelope.Lifecycle.MoveTo(JobLifecycleState.Failed);
                    SequoraLog.HandlerNotFound(_logger, exception, envelope.JobType);
                    return;
                }
                catch (Exception exception)
                {
                    if (attempt > envelope.RetryCount)
                    {
                        envelope.Lifecycle.MoveTo(JobLifecycleState.Failed);
                        SequoraLog.JobFailed(_logger, exception, envelope.JobType, attempt);
                        return;
                    }

                    int retryNumber = attempt;
                    TimeSpan delay = RetryDelayCalculator.Compute(
                        envelope.RetryDelay,
                        envelope.RetryBackoff,
                        retryNumber,
                        envelope.MaxRetryDelay);

                    envelope.Lifecycle.MoveTo(JobLifecycleState.Retrying);
                    SequoraLog.JobRetry(
                        _logger,
                        exception,
                        envelope.JobType,
                        attempt,
                        retryNumber,
                        envelope.RetryCount,
                        delay);

                    try
                    {
                        await _retryDelay.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        envelope.Lifecycle.MoveTo(JobLifecycleState.Cancelled);
                        SequoraLog.RetryCanceled(_logger, envelope.JobType);
                        throw;
                    }

                    envelope.Lifecycle.MoveTo(JobLifecycleState.Processing);
                }
            }
        }
        finally
        {
            _queue.ReleaseJobId(envelope);
        }
    }
}
