using Microsoft.Extensions.DependencyInjection;

namespace Sequora.Internal;

internal static class JobHandlerInvoker
{
    public static Task InvokeAsync<TJob>(
        IServiceProvider services,
        TJob job,
        CancellationToken cancellationToken)
        where TJob : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(job);

        IJobHandler<TJob>? handler = services.GetService<IJobHandler<TJob>>();
        if (handler is null)
        {
            throw new SequoraHandlerNotFoundException(typeof(TJob));
        }

        return handler.HandleAsync(job, cancellationToken);
    }
}
