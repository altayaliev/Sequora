using Microsoft.Extensions.DependencyInjection;

namespace Sequora.Tests;

internal static class SequoraProvider
{
    public static ServiceProvider Create(Action<SequoraOptions>? configure = null)
    {
        ServiceCollection services = new();
        services.AddSequora(configure);
        return services.BuildServiceProvider();
    }

    public static ServiceProvider Create(
        Action<SequoraOptions>? configure,
        Action<ISequoraBuilder> configureBuilder)
    {
        ServiceCollection services = new();
        ISequoraBuilder builder = services.AddSequora(configure);
        configureBuilder(builder);
        return services.BuildServiceProvider();
    }
}
