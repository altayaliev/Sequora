namespace Sequora;

/// <summary>
/// Thrown when a queued job has no registered <see cref="IJobHandler{TJob}"/>.
/// </summary>
/// <remarks>
/// Missing handlers are logged and are not retried. The worker continues with
/// later jobs. Register a handler with
/// <see cref="ISequoraBuilder.AddHandler{TJob, THandler}()"/> before enqueueing
/// that job type, or the exception is thrown when the job is processed.
/// </remarks>
public sealed class SequoraHandlerNotFoundException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new exception for the specified job type.
    /// </summary>
    /// <param name="jobType">The job type that has no handler.</param>
    /// <exception cref="ArgumentNullException"><paramref name="jobType"/> is null.</exception>
    public SequoraHandlerNotFoundException(Type jobType)
        : base($"No IJobHandler<{jobType.Name}> is registered for job type '{jobType}'.")
    {
        ArgumentNullException.ThrowIfNull(jobType);
        JobType = jobType;
    }

    /// <summary>
    /// Gets the job type that could not be handled.
    /// </summary>
    public Type JobType { get; }
}
