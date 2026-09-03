namespace Sequora;

/// <summary>
/// Thrown when a bounded queue is full and the job is rejected
/// (<see cref="QueueFullBehavior.Throw"/>).
/// </summary>
/// <remarks>
/// <see cref="QueueFullBehavior.Wait"/> and <see cref="QueueFullBehavior.Drop"/>
/// do not throw this exception. Already accepted jobs are not removed.
/// </remarks>
public sealed class SequoraQueueFullException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new exception for a full queue.
    /// </summary>
    /// <param name="capacity">The configured bounded capacity.</param>
    public SequoraQueueFullException(int capacity)
        : base($"The in-memory job queue is full (capacity {capacity}).")
    {
        Capacity = capacity;
    }

    /// <summary>
    /// Gets the bounded capacity that was reached.
    /// </summary>
    public int Capacity { get; }
}
