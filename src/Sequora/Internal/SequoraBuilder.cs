using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sequora.Internal;

internal sealed class SequoraBuilder : ISequoraBuilder
{
    public SequoraBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }

    public ISequoraBuilder Configure(Action<SequoraOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }

    public ISequoraBuilder AddHandler<TJob, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where TJob : notnull
        where THandler : class, IJobHandler<TJob>
        => AddHandler<TJob, THandler>(ServiceLifetime.Transient);

    public ISequoraBuilder AddHandler<TJob, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        ServiceLifetime lifetime)
        where TJob : notnull
        where THandler : class, IJobHandler<TJob>
    {
        if (lifetime is not (ServiceLifetime.Singleton or ServiceLifetime.Scoped or ServiceLifetime.Transient))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "A valid service lifetime is required.");
        }

        ServiceDescriptor? existing = Services.FirstOrDefault(
            descriptor => descriptor.ServiceType == typeof(IJobHandler<TJob>));
        if (existing is not null)
        {
            throw new SequoraHandlerAlreadyRegisteredException(
                typeof(TJob),
                existing.ImplementationType ?? existing.ImplementationInstance?.GetType() ?? existing.ServiceType,
                typeof(THandler));
        }

        Services.Add(new ServiceDescriptor(typeof(IJobHandler<TJob>), typeof(THandler), lifetime));
        return this;
    }
}
