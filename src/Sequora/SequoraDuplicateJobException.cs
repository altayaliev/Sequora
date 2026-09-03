namespace Sequora;

/// <summary>
/// Thrown when a job is enqueued with a <see cref="EnqueueOptions.JobId"/> that is
/// already delayed, queued, processing, or retrying in this process.
/// </summary>
/// <remarks>
/// Duplicate detection is in-memory and process-local. It is not exactly-once
/// delivery or exactly-once execution, and it does not survive a process crash.
/// After a job completes, fails, or is cancelled, the same id may be reused.
/// </remarks>
public sealed class SequoraDuplicateJobException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new exception for a duplicate job id.
    /// </summary>
    /// <param name="jobId">The job id that is already active in this process.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="jobId"/> is null, empty, or whitespace.
    /// </exception>
    public SequoraDuplicateJobException(string jobId)
        : base($"A job with id '{jobId}' is already queued or running in this process.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        JobId = jobId;
    }

    /// <summary>
    /// Gets the job id that was already registered.
    /// </summary>
    public string JobId { get; }
}
