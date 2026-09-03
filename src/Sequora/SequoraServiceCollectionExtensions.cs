using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sequora.Internal;

namespace Sequora;

/// <summary>
/// Registers Sequora on an <see cref="IServiceCollection"/>.
/// </summary>
public static class SequoraServiceCollectionExtensions
{
    /// <summary>
    /// Adds Sequora with documented defaults so the queue can be used immediately.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>A builder for queue configuration and handler registration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <remarks>
    /// Hosted workers start with the generic host. Every
    /// <see cref="SequoraOptions"/> property has a safe default; no callback
    /// is required. Invalid options fail at validation, typically when the host
    /// starts or when the queue is first resolved.
    /// </remarks>
    public static ISequoraBuilder AddSequora(this IServiceCollection services)
        => AddSequora(services, configure: null);

    /// <summary>
    /// Adds Sequora and applies queue configuration on top of the defaults.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional queue configuration callback. When null, defaults are used.
    /// Additional callbacks can be added later with
    /// <see cref="ISequoraBuilder.Configure"/>.
    /// </param>
    /// <returns>A builder for queue configuration and handler registration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <remarks>
    /// This is the queue-configuration layer of the precedence model.
    /// Job-level <see cref="EnqueueOptions"/> still override matching properties
    /// when a job is enqueued.
    /// </remarks>
    public static ISequoraBuilder AddSequora(
        this IServiceCollection services,
        Action<SequoraOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);

        OptionsBuilder<SequoraOptions> optionsBuilder = services.AddOptions<SequoraOptions>();
        optionsBuilder.ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<SequoraOptions>, SequoraOptionsValidator>());

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddLogging();
        services.TryAddSingleton<IRetryDelay, TaskRetryDelay>();
        services.TryAddSingleton<IJobQueue, JobQueue>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JobWorker>());

        return new SequoraBuilder(services);
    }

    /// <summary>
    /// Registers every <see cref="IJobHandler{TJob}"/> implemented by <typeparamref name="THandler"/>
    /// as a transient service.
    /// </summary>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="builder">The Sequora builder.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="THandler"/> does not implement <see cref="IJobHandler{TJob}"/>.
    /// </exception>
    /// <exception cref="SequoraHandlerAlreadyRegisteredException">
    /// A handler is already registered for one of the job types <typeparamref name="THandler"/> implements.
    /// </exception>
    public static ISequoraBuilder AddHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] THandler>(
        this ISequoraBuilder builder)
        where THandler : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        return AddHandler<THandler>(builder, ServiceLifetime.Transient);
    }

    /// <summary>
    /// Registers every <see cref="IJobHandler{TJob}"/> implemented by <typeparamref name="THandler"/>
    /// with the specified DI lifetime.
    /// </summary>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="builder">The Sequora builder.</param>
    /// <param name="lifetime">
    /// The handler lifetime. Scoped handlers are resolved from the per-job scope.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is invalid.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="THandler"/> does not implement <see cref="IJobHandler{TJob}"/>.
    /// </exception>
    /// <exception cref="SequoraHandlerAlreadyRegisteredException">
    /// A handler is already registered for one of the job types <typeparamref name="THandler"/> implements.
    /// </exception>
    public static ISequoraBuilder AddHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] THandler>(
        this ISequoraBuilder builder,
        ServiceLifetime lifetime)
        where THandler : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (lifetime is not (ServiceLifetime.Singleton or ServiceLifetime.Scoped or ServiceLifetime.Transient))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "A valid service lifetime is required.");
        }

        Type[] jobHandlerInterfaces = [.. typeof(THandler).GetInterfaces()
            .Where(iface => iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IJobHandler<>))];

        if (jobHandlerInterfaces.Length == 0)
        {
            throw new InvalidOperationException(
                $"{typeof(THandler).FullName} does not implement {typeof(IJobHandler<>).Name}.");
        }

        foreach (Type iface in jobHandlerInterfaces)
        {
            ServiceDescriptor? existing = builder.Services.FirstOrDefault(descriptor => descriptor.ServiceType == iface);
            if (existing is not null)
            {
                throw new SequoraHandlerAlreadyRegisteredException(
                    iface.GetGenericArguments()[0],
                    existing.ImplementationType ?? existing.ImplementationInstance?.GetType() ?? existing.ServiceType,
                    typeof(THandler));
            }
        }

        foreach (Type iface in jobHandlerInterfaces)
        {
            builder.Services.Add(new ServiceDescriptor(iface, typeof(THandler), lifetime));
        }

        return builder;
    }
}
