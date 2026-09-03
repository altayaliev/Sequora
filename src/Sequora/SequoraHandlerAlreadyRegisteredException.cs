namespace Sequora;

/// <summary>
/// Thrown when <see cref="ISequoraBuilder.AddHandler{TJob, THandler}()"/> (or an
/// overload) registers a handler for a job type that already has one.
/// </summary>
/// <remarks>
/// Sequora dispatches each job type to exactly one handler. Registering a
/// second handler for the same job type is rejected instead of being silently
/// ignored, so a duplicate registration is caught at startup rather than
/// producing a handler that quietly never runs.
/// </remarks>
public sealed class SequoraHandlerAlreadyRegisteredException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new exception for a job type that already has a registered handler.
    /// </summary>
    /// <param name="jobType">The job type that already has a handler registered.</param>
    /// <param name="existingHandlerType">The handler type already registered for <paramref name="jobType"/>.</param>
    /// <param name="newHandlerType">The handler type that could not be registered.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="jobType"/>, <paramref name="existingHandlerType"/>, or <paramref name="newHandlerType"/> is null.
    /// </exception>
    public SequoraHandlerAlreadyRegisteredException(Type jobType, Type existingHandlerType, Type newHandlerType)
        : base(
            $"A handler is already registered for job type '{jobType}': '{existingHandlerType}'. " +
            $"Cannot also register '{newHandlerType}' for the same job type. " +
            "Each job type must have exactly one registered IJobHandler<TJob>.")
    {
        ArgumentNullException.ThrowIfNull(jobType);
        ArgumentNullException.ThrowIfNull(existingHandlerType);
        ArgumentNullException.ThrowIfNull(newHandlerType);

        JobType = jobType;
        ExistingHandlerType = existingHandlerType;
        NewHandlerType = newHandlerType;
    }

    /// <summary>
    /// Gets the job type that already has a registered handler.
    /// </summary>
    public Type JobType { get; }

    /// <summary>
    /// Gets the handler type already registered for <see cref="JobType"/>.
    /// </summary>
    public Type ExistingHandlerType { get; }

    /// <summary>
    /// Gets the handler type that could not be registered.
    /// </summary>
    public Type NewHandlerType { get; }
}
