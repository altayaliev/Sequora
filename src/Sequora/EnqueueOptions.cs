namespace Sequora;

/// <summary>
/// Per-job overrides applied when a job is enqueued.
/// </summary>
/// <remarks>
/// <para>
/// This is the most specific layer of the configuration hierarchy
/// (global defaults → queue configuration → job-level configuration).
/// Unset (null) properties inherit the corresponding
/// <see cref="SequoraOptions"/> value. Setting a property replaces the
/// queue value for that job only.
/// </para>
/// <para>
/// Job-only settings have no queue-level counterpart:
/// <see cref="JobId"/> (omit for an anonymous job) and <see cref="Delay"/>
/// (omit or use <see cref="TimeSpan.Zero"/> for immediate readiness).
/// </para>
/// <para>
/// Worker count, queue capacity, queue-full behavior, fairness, and shutdown
/// behavior are queue-only and cannot be set here.
/// </para>
/// </remarks>
public sealed class EnqueueOptions
{
    /// <summary>
    /// Maximum length of <see cref="JobId"/>.
    /// </summary>
    public const int MaxJobIdLength = 256;

    /// <summary>
    /// Gets or sets an optional stable job id used for in-process duplicate detection.
    /// When null, the job is anonymous and is not tracked by id.
    /// Comparison is ordinal and case-sensitive.
    /// </summary>
    /// <remarks>
    /// A job id is reserved when enqueue claims it in this process, including
    /// while waiting for capacity. It stays active while the job is delayed,
    /// queued, processing, or retrying. Retries keep the same id. The id is
    /// released if enqueue does not accept the job, and when the job completes,
    /// fails, or is cancelled, after which it may be reused. This is not
    /// exactly-once execution and does not survive a crash.
    /// </remarks>
    public string? JobId { get; set; }

    /// <summary>
    /// Gets or sets how long to wait after enqueue before the job becomes ready.
    /// When null or <see cref="TimeSpan.Zero"/>, the job is ready immediately.
    /// Must be zero or greater when set. There is no queue-level delay setting.
    /// </summary>
    /// <remarks>
    /// Enqueue returns after the job is accepted; it does not wait for the delay.
    /// Retry delays are separate and start only after a failed attempt.
    /// Shutdown cancels delayed jobs that are not yet due.
    /// </remarks>
    public TimeSpan? Delay { get; set; }

    /// <summary>
    /// Gets or sets the job priority. Higher values are dequeued first.
    /// When null, <see cref="SequoraOptions.Priority"/> is used.
    /// Any <see cref="int"/> value is valid.
    /// </summary>
    /// <remarks>
    /// Equal priorities keep FIFO order. Continuous higher-priority work can delay
    /// older lower-priority jobs; <see cref="SequoraOptions.PriorityFairnessLimit"/>
    /// periodically inserts the oldest waiting job. A retrying job stays with its
    /// worker and is not re-ranked against the ready queue.
    /// </remarks>
    public int? Priority { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retries after the first failed attempt.
    /// This is the number of retries, not the total number of attempts.
    /// When null, <see cref="SequoraOptions.RetryCount"/> is used.
    /// Must be zero or greater when set.
    /// </summary>
    public int? RetryCount { get; set; }

    /// <summary>
    /// Gets or sets the base delay between retries.
    /// When null, <see cref="SequoraOptions.RetryDelay"/> is used.
    /// Must be zero or greater when set.
    /// </summary>
    public TimeSpan? RetryDelay { get; set; }

    /// <summary>
    /// Gets or sets the maximum wait between retries after backoff is applied.
    /// When null, <see cref="SequoraOptions.MaxRetryDelay"/> is used.
    /// Must be zero or greater when set.
    /// </summary>
    public TimeSpan? MaxRetryDelay { get; set; }

    /// <summary>
    /// Gets or sets the retry backoff strategy.
    /// When null, <see cref="SequoraOptions.RetryBackoff"/> is used.
    /// Must be a defined <see cref="RetryBackoffStrategy"/> value when set.
    /// </summary>
    public RetryBackoffStrategy? RetryBackoff { get; set; }
}
