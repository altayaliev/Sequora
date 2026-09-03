using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Sequora;

/// <summary>
/// Configures Sequora after
/// <see cref="SequoraServiceCollectionExtensions.AddSequora(IServiceCollection)"/>.
/// </summary>
/// <remarks>
/// The simple path is <c>services.AddSequora().AddHandler&lt;TJob, THandler&gt;()</c>.
/// Use <see cref="Configure"/> only when queue settings must change.
/// <see cref="Services"/> is for advanced DI registration; most applications
/// do not need it.
/// </remarks>
public interface ISequoraBuilder
{
    /// <summary>
    /// Gets the service collection being configured.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Applies additional queue configuration on top of
    /// <see cref="SequoraOptions"/> defaults and any callback passed to
    /// <see cref="SequoraServiceCollectionExtensions.AddSequora(IServiceCollection, Action{SequoraOptions}?)"/>.
    /// </summary>
    /// <param name="configure">
    /// Mutates queue configuration. Later callbacks run after earlier ones
    /// on the same options instance.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    ISequoraBuilder Configure(Action<SequoraOptions> configure);

    /// <summary>
    /// Registers a transient handler for <typeparamref name="TJob"/>.
    /// </summary>
    /// <typeparam name="TJob">The job payload type.</typeparam>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="SequoraHandlerAlreadyRegisteredException">
    /// A handler is already registered for <typeparamref name="TJob"/>.
    /// </exception>
    ISequoraBuilder AddHandler<TJob, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where TJob : notnull
        where THandler : class, IJobHandler<TJob>;

    /// <summary>
    /// Registers a handler for <typeparamref name="TJob"/> with the specified DI lifetime.
    /// </summary>
    /// <typeparam name="TJob">The job payload type.</typeparam>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    /// <param name="lifetime">
    /// The handler lifetime. Scoped handlers are resolved from the per-job scope,
    /// not from the root provider. Default registrations use
    /// <see cref="ServiceLifetime.Transient"/>.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="lifetime"/> is not a defined <see cref="ServiceLifetime"/> value.
    /// </exception>
    /// <exception cref="SequoraHandlerAlreadyRegisteredException">
    /// A handler is already registered for <typeparamref name="TJob"/>.
    /// </exception>
    ISequoraBuilder AddHandler<TJob, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        ServiceLifetime lifetime)
        where TJob : notnull
        where THandler : class, IJobHandler<TJob>;
}
