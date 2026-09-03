namespace Sequora;

/// <summary>
/// Selects how successive retry delays grow after a failed job attempt.
/// </summary>
/// <remarks>
/// Default is <see cref="Exponential"/>. Computed delays are capped by
/// <see cref="SequoraOptions.MaxRetryDelay"/> (or the job-level override).
/// Retry delay is not applied after a successful attempt or after the final failure.
/// </remarks>
public enum RetryBackoffStrategy
{
    /// <summary>
    /// Use the configured delay on every retry (fixed delay).
    /// </summary>
    Constant = 0,

    /// <summary>
    /// Multiply the configured delay by the retry number (1×, 2×, 3×, …).
    /// </summary>
    Linear = 1,

    /// <summary>
    /// Double the delay on each successive retry (1×, 2×, 4×, …).
    /// This is the default. Growth is capped by the configured maximum retry delay.
    /// </summary>
    Exponential = 2
}
