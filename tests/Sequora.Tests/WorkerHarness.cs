using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sequora.Internal;

namespace Sequora.Tests;

internal sealed class WorkerHarness : IAsyncDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private WorkerHarness(ServiceProvider provider, IJobQueue queue, JobWorker worker)
    {
        Provider = provider;
        Queue = queue;
        Worker = worker;
    }

    public ServiceProvider Provider { get; }

    public IJobQueue Queue { get; }

    public JobWorker Worker { get; }

    public JobQueue ConcreteQueue => Assert.IsType<JobQueue>(Queue);

    public static WorkerHarness Create(
        Action<SequoraOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null,
        Action<ISequoraBuilder>? configureBuilder = null)
    {
        ServiceCollection services = new();
        ISequoraBuilder builder = services.AddSequora(configure);
        configureBuilder?.Invoke(builder);
        configureServices?.Invoke(services);
        ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        JobWorker worker = provider.GetServices<IHostedService>().OfType<JobWorker>().Single();
        return new WorkerHarness(provider, provider.GetRequiredService<IJobQueue>(), worker);
    }

    public Task StartAsync() => Worker.StartAsync(CancellationToken.None);

    public async Task StopAsync()
    {
        using CancellationTokenSource timeout = new(Timeout);
        await Worker.StopAsync(timeout.Token).ConfigureAwait(true);
    }

    public async ValueTask DisposeAsync()
    {
        using CancellationTokenSource timeout = new(Timeout);
        try
        {
            await Worker.StopAsync(timeout.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }

        await Provider.DisposeAsync().ConfigureAwait(true);
    }
}
