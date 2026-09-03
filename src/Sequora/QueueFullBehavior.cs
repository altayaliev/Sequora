namespace Sequora;

/// <summary>
/// Controls what happens when a bounded in-memory queue is at capacity.
/// </summary>
/// <remarks>
/// Queue-only. Ignored when <see cref="SequoraOptions.Capacity"/> is
/// <see cref="SequoraOptions.Unbounded"/>. Capacity counts ready and delayed
/// jobs; in-flight work does not count. Default is <see cref="Wait"/>.
/// </remarks>
public enum QueueFullBehavior
{
    /// <summary>
    /// Wait until space is available or the enqueue <see cref="CancellationToken"/>
    /// is canceled. This is the default. Canceling the token does not remove jobs
    /// that were already accepted.
    /// </summary>
    Wait = 0,

    /// <summary>
    /// Reject the job by throwing <see cref="SequoraQueueFullException"/>.
    /// Already accepted jobs are not removed.
    /// </summary>
    Throw = 1,

    /// <summary>
    /// Drop the incoming job and complete enqueue successfully.
    /// Already queued jobs are not removed.
    /// </summary>
    Drop = 2
}
