namespace Sequora;

/// <summary>
/// Enqueues strongly typed jobs for in-process background processing.
/// </summary>
/// <remarks>
/// <para>
/// Queued work lives in process memory. A process crash or unexpected restart
/// can lose queued jobs. This API does not provide exactly-once delivery or
/// exactly-once execution.
/// </para>
/// <para>
/// Enqueue accepts the job into the in-memory queue; it does not wait for the
/// handler to run. Delayed jobs are accepted immediately and become ready after
/// <see cref="EnqueueOptions.Delay"/>.
/// </para>
/// </remarks>
public interface IJobQueue
{
    /// <summary>
    /// Enqueues a job using queue configuration (defaults plus any
    /// <see cref="SequoraOptions"/> callbacks). Job-level overrides are not applied.
    /// </summary>
    /// <typeparam name="TJob">The job payload type. Must not be null.</typeparam>
    /// <param name="job">The job to enqueue.</param>
    /// <param name="cancellationToken">
    /// Cancels a wait for capacity when <see cref="QueueFullBehavior.Wait"/> is
    /// configured. Does not cancel a job that has already been accepted, and does
    /// not cancel handler execution. A canceled token throws before the job is written.
    /// </param>
    /// <returns>
    /// A task that completes when the job has been accepted. Completion does not
    /// mean the handler has run.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="job"/> is null.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    /// <exception cref="SequoraStoppedException">The queue has been stopped.</exception>
    /// <exception cref="SequoraQueueFullException">
    /// The queue is bounded, full, and <see cref="QueueFullBehavior.Throw"/> is configured.
    /// </exception>
    Task EnqueueAsync<TJob>(TJob job, CancellationToken cancellationToken = default)
        where TJob : notnull;

    /// <summary>
    /// Enqueues a job with per-job option overrides. Unset
    /// <see cref="EnqueueOptions"/> properties inherit queue configuration.
    /// </summary>
    /// <typeparam name="TJob">The job payload type. Must not be null.</typeparam>
    /// <param name="job">The job to enqueue.</param>
    /// <param name="configure">
    /// Configures job-level options. Null properties inherit the queue value.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels a wait for capacity when <see cref="QueueFullBehavior.Wait"/> is
    /// configured. Does not cancel a job that has already been accepted, and does
    /// not cancel handler execution. A canceled token throws before the job is written.
    /// </param>
    /// <returns>
    /// A task that completes when the job has been accepted. For delayed jobs,
    /// acceptance is not the same as becoming ready.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="job"/> or <paramref name="configure"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="configure"/> produced an invalid option value
    /// (negative retry count or delay, undefined backoff, or a job id that is too long).
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <see cref="EnqueueOptions.JobId"/> was empty or whitespace.
    /// </exception>
    /// <exception cref="SequoraDuplicateJobException">
    /// A job with the same <see cref="EnqueueOptions.JobId"/> is already delayed,
    /// queued, processing, or retrying in this process.
    /// </exception>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    /// <exception cref="SequoraStoppedException">The queue has been stopped.</exception>
    /// <exception cref="SequoraQueueFullException">
    /// The queue is bounded, full, and <see cref="QueueFullBehavior.Throw"/> is configured.
    /// </exception>
    Task EnqueueAsync<TJob>(
        TJob job,
        Action<EnqueueOptions> configure,
        CancellationToken cancellationToken = default)
        where TJob : notnull;
}
