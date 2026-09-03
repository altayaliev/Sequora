namespace Sequora.Internal;

/// <summary>
/// Asynchronous delay used for retry backoff and delayed jobs.
/// Implementations must not block threads or poll.
/// </summary>
internal interface IRetryDelay
{
    /// <summary>
    /// Waits for <paramref name="delay"/> without blocking a thread.
    /// </summary>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
