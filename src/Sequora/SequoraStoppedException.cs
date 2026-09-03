namespace Sequora;

/// <summary>
/// Thrown when a job is enqueued after the in-memory queue has been stopped,
/// or when an enqueue wait is aborted because the queue stopped.
/// </summary>
public sealed class SequoraStoppedException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new exception with the default message.
    /// </summary>
    public SequoraStoppedException()
        : this("The in-memory job queue has been stopped and is no longer accepting work.")
    {
    }

    /// <summary>
    /// Initializes a new exception with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public SequoraStoppedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new exception with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public SequoraStoppedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
