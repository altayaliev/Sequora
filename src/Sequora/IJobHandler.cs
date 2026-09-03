namespace Sequora;

/// <summary>
/// Handles a strongly typed job from the in-process queue.
/// </summary>
/// <typeparam name="TJob">The job payload type. Must not be null.</typeparam>
/// <remarks>
/// <para>
/// Register implementations with
/// <see cref="ISequoraBuilder.AddHandler{TJob, THandler}()"/> or
/// <see cref="SequoraServiceCollectionExtensions.AddHandler{THandler}(ISequoraBuilder)"/>.
/// Each execution, including each retry attempt, uses a new dependency-injection
/// scope. Scoped services are resolved from that scope, not from the root provider.
/// </para>
/// <para>
/// An exception from <see cref="HandleAsync"/> is logged and retried according to
/// the job's retry settings. <see cref="OperationCanceledException"/> caused by
/// shutdown cancellation is not retried.
/// <see cref="SequoraHandlerNotFoundException"/> is not produced by this
/// interface; it is thrown when no handler is registered for the job type, and
/// that failure is not retried.
/// </para>
/// </remarks>
public interface IJobHandler<in TJob>
    where TJob : notnull
{
    /// <summary>
    /// Processes a single job attempt.
    /// </summary>
    /// <param name="job">The job payload. Never null.</param>
    /// <param name="cancellationToken">
    /// Signaled when <see cref="ShutdownBehavior.Cancel"/> is configured and the
    /// host is shutting down. With <see cref="ShutdownBehavior.Drain"/> (the default),
    /// in-flight handlers are not canceled by shutdown and this token typically
    /// remains unused unless the handler cooperates with its own cancellation.
    /// Honor the token when it is signaled; throwing
    /// <see cref="OperationCanceledException"/> for shutdown is not retried.
    /// </param>
    /// <returns>A task that completes when this attempt has finished.</returns>
    /// <remarks>
    /// Returning successfully completes the job. Throwing any exception other than
    /// shutdown cancellation schedules a retry if retries remain.
    /// </remarks>
    Task HandleAsync(TJob job, CancellationToken cancellationToken);
}
