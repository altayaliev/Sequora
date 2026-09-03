namespace Sequora;

/// <summary>
/// Controls how queued and in-flight work is treated when the host shuts down.
/// </summary>
/// <remarks>
/// Queue-only. Default is <see cref="Drain"/>. Delayed jobs that are not yet
/// due are cancelled for both values.
/// </remarks>
public enum ShutdownBehavior
{
    /// <summary>
    /// Finish in-flight jobs and drain remaining ready work until shutdown is
    /// canceled. Handlers do not receive the host stopping token.
    /// Delayed jobs that are not yet due are still cancelled.
    /// This is the default.
    /// </summary>
    Drain = 0,

    /// <summary>
    /// Cancel in-flight work by signaling the handler cancellation token, and
    /// stop without draining the remaining ready queue.
    /// Remaining queued jobs are discarded when the process exits.
    /// Delayed jobs that are not yet due are cancelled.
    /// </summary>
    Cancel = 1
}
