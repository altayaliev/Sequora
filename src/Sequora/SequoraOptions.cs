namespace Sequora;

/// <summary>
/// Queue-level configuration for the in-process job queue.
/// </summary>
/// <remarks>
/// <para>
/// Configuration uses a three-layer precedence model. More specific values
/// replace less specific ones for the same setting:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// Global defaults — the property initializers and constants on this type.
/// <see cref="SequoraServiceCollectionExtensions.AddSequora(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>
/// uses these values so no configuration is required to start processing.
/// </description>
/// </item>
/// <item>
/// <description>
/// Queue configuration — callbacks passed to
/// <see cref="SequoraServiceCollectionExtensions.AddSequora(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{SequoraOptions}?)"/>
/// or <see cref="ISequoraBuilder.Configure"/>. Later callbacks run after earlier
/// ones on the same options instance, so a later assignment to a property wins.
/// </description>
/// </item>
/// <item>
/// <description>
/// Job-level configuration — <see cref="EnqueueOptions"/> on
/// <see cref="IJobQueue.EnqueueAsync{TJob}(TJob, Action{EnqueueOptions}, CancellationToken)"/>.
/// A null job property inherits the queue value. Job-level values never change
/// queue-only settings such as worker count or capacity.
/// </description>
/// </item>
/// </list>
/// <para>
/// Overridable per job: <see cref="RetryCount"/>, <see cref="RetryDelay"/>,
/// <see cref="MaxRetryDelay"/>, <see cref="RetryBackoff"/>, and <see cref="Priority"/>.
/// Queue-only: <see cref="WorkerCount"/>, <see cref="Capacity"/>,
/// <see cref="QueueFullBehavior"/>, <see cref="ShutdownBehavior"/>, and
/// <see cref="PriorityFairnessLimit"/>.
/// Delay and job id have no queue-level setting; omit them on a job for
/// immediate, anonymous enqueue.
/// </para>
/// <para>
/// Invalid values fail when options are validated (host start or first resolve),
/// not by silently clamping. See each property for the accepted range.
/// </para>
/// </remarks>
public sealed class SequoraOptions
{
    /// <summary>
    /// Capacity value that creates an unbounded in-memory queue.
    /// </summary>
    public const int Unbounded = -1;

    /// <summary>
    /// Default worker count. A single worker is enough for
    /// <see cref="SequoraServiceCollectionExtensions.AddSequora(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
    /// </summary>
    public const int DefaultWorkerCount = 1;

    /// <summary>
    /// Default number of retries after the first failed attempt.
    /// A value of 3 means: attempt 1, then retry 1, retry 2, retry 3
    /// (4 executions maximum).
    /// </summary>
    public const int DefaultRetryCount = 3;

    /// <summary>
    /// Default job priority. Higher values run before lower values.
    /// Equal priorities keep FIFO order.
    /// </summary>
    public const int DefaultPriority = 0;

    /// <summary>
    /// Default number of higher-priority jobs that may skip an older
    /// lower-priority job before fairness inserts that older job.
    /// </summary>
    public const int DefaultPriorityFairnessLimit = 32;

    /// <summary>
    /// Gets the default base delay between retries (one second).
    /// </summary>
    public static TimeSpan DefaultRetryDelay { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the default cap applied to computed retry delays (one minute).
    /// Prevents unbounded growth when exponential backoff is used.
    /// </summary>
    public static TimeSpan DefaultMaxRetryDelay { get; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the number of concurrent workers that process queued jobs.
    /// Must be at least 1. Default is <see cref="DefaultWorkerCount"/>.
    /// Queue-only; cannot be overridden per job.
    /// </summary>
    public int WorkerCount { get; set; } = DefaultWorkerCount;

    /// <summary>
    /// Gets or sets the maximum number of jobs that may sit in memory waiting
    /// to run, including delayed jobs that have been accepted but are not yet due.
    /// In-flight work does not count. Use <see cref="Unbounded"/> for no limit.
    /// Must be <see cref="Unbounded"/> or at least 1. Default is <see cref="Unbounded"/>.
    /// Queue-only; cannot be overridden per job.
    /// </summary>
    public int Capacity { get; set; } = Unbounded;

    /// <summary>
    /// Gets or sets how many times a failed job is retried after the first attempt.
    /// This is the number of retries, not the total number of attempts.
    /// A value of 0 means a single attempt and no retries.
    /// A value of 3 means: attempt 1, then retry 1, retry 2, retry 3.
    /// Must be zero or greater. Default is <see cref="DefaultRetryCount"/>.
    /// Overridable per job via <see cref="EnqueueOptions.RetryCount"/>.
    /// </summary>
    public int RetryCount { get; set; } = DefaultRetryCount;

    /// <summary>
    /// Gets or sets the base delay between retries.
    /// Must be zero or greater. Default is one second
    /// (<see cref="DefaultRetryDelay"/>).
    /// The delay is not applied after a successful attempt or after the final failure.
    /// Overridable per job via <see cref="EnqueueOptions.RetryDelay"/>.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = DefaultRetryDelay;

    /// <summary>
    /// Gets or sets the maximum wait between retries after backoff is applied.
    /// Must be zero or greater. Default is one minute
    /// (<see cref="DefaultMaxRetryDelay"/>).
    /// A value of <see cref="TimeSpan.Zero"/> skips retry delays.
    /// Overridable per job via <see cref="EnqueueOptions.MaxRetryDelay"/>.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = DefaultMaxRetryDelay;

    /// <summary>
    /// Gets or sets how retry delays grow after each failed attempt.
    /// Default is <see cref="RetryBackoffStrategy.Exponential"/>.
    /// Computed delays are capped by <see cref="MaxRetryDelay"/>.
    /// Overridable per job via <see cref="EnqueueOptions.RetryBackoff"/>.
    /// </summary>
    public RetryBackoffStrategy RetryBackoff { get; set; } = RetryBackoffStrategy.Exponential;

    /// <summary>
    /// Gets or sets the default priority for jobs that do not set
    /// <see cref="EnqueueOptions.Priority"/>. Higher values run first.
    /// Default is <see cref="DefaultPriority"/> (0), which is FIFO among
    /// unprioritized jobs. Any <see cref="int"/> value is valid.
    /// Overridable per job via <see cref="EnqueueOptions.Priority"/>.
    /// </summary>
    public int Priority { get; set; } = DefaultPriority;

    /// <summary>
    /// Gets or sets what happens when a bounded queue is at capacity.
    /// Ignored when <see cref="Capacity"/> is <see cref="Unbounded"/>.
    /// Applies when a job is accepted, including delayed jobs.
    /// Default is <see cref="QueueFullBehavior.Wait"/>.
    /// Queue-only; cannot be overridden per job.
    /// </summary>
    public QueueFullBehavior QueueFullBehavior { get; set; } = QueueFullBehavior.Wait;

    /// <summary>
    /// Gets or sets how in-flight and queued work is treated during host shutdown.
    /// Default is <see cref="ShutdownBehavior.Drain"/>.
    /// Delayed jobs that are not yet due are cancelled on shutdown for both
    /// <see cref="ShutdownBehavior.Drain"/> and <see cref="ShutdownBehavior.Cancel"/>.
    /// Drain still finishes ready and in-flight work, including retries.
    /// Queue-only; cannot be overridden per job.
    /// </summary>
    public ShutdownBehavior ShutdownBehavior { get; set; } = ShutdownBehavior.Drain;

    /// <summary>
    /// Gets or sets how many higher-priority jobs may skip an older lower-priority
    /// job before that older job is dequeued. Must be zero or greater.
    /// Zero disables fairness (strict priority). Default is
    /// <see cref="DefaultPriorityFairnessLimit"/>. Has no effect when every job
    /// uses the same priority. Queue-only; cannot be overridden per job.
    /// </summary>
    public int PriorityFairnessLimit { get; set; } = DefaultPriorityFairnessLimit;

    /// <summary>
    /// Gets a value indicating whether the queue has a finite capacity.
    /// Derived from <see cref="Capacity"/>; it is not independently configurable.
    /// </summary>
    public bool IsBounded => Capacity != Unbounded;
}
