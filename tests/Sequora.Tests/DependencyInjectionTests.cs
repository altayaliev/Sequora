using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sequora.Internal;

namespace Sequora.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddSequora_NullServices_Throws()
    {
        IServiceCollection services = null!;

        Assert.Throws<ArgumentNullException>(() => services.AddSequora());
        Assert.Throws<ArgumentNullException>(() => services.AddSequora(options => options.WorkerCount = 2));
    }

    [Fact]
    public void AddSequora_ReturnsBuilderBoundToTheSameCollection()
    {
        ServiceCollection services = new();

        ISequoraBuilder builder = services.AddSequora();

        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddSequora_IsIdempotentForQueueRegistration()
    {
        ServiceCollection services = new();
        services.AddSequora();
        services.AddSequora(options => options.WorkerCount = 2);

        ServiceDescriptor[] queues = [.. services.Where(descriptor => descriptor.ServiceType == typeof(IJobQueue))];
        Assert.Single(queues);
    }

    [Fact]
    public void AddSequora_DoesNotReplaceUserRegisteredQueue()
    {
        ServiceCollection services = new();
        services.TryAddSingleton<IJobQueue, ReplacementQueue>();
        services.AddSequora();

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsType<ReplacementQueue>(provider.GetRequiredService<IJobQueue>());
    }

    [Fact]
    public void SimplePath_AddSequora_ResolvesQueue()
    {
        ServiceCollection services = new();
        services.AddSequora();

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IJobQueue>());
        Assert.NotNull(provider.GetRequiredService<IOptions<SequoraOptions>>().Value);
        Assert.IsType<TaskRetryDelay>(provider.GetRequiredService<IRetryDelay>());
    }

    [Fact]
    public void AddSequora_DoesNotReplaceUserRegisteredRetryDelay()
    {
        ServiceCollection services = new();
        RecordingRetryDelay delay = new();
        services.AddSingleton<IRetryDelay>(delay);
        services.AddSequora();

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Same(delay, provider.GetRequiredService<IRetryDelay>());
    }

    private sealed class ReplacementQueue : IJobQueue
    {
        public Task EnqueueAsync<TJob>(TJob job, CancellationToken cancellationToken = default)
            where TJob : notnull
            => Task.CompletedTask;

        public Task EnqueueAsync<TJob>(
            TJob job,
            Action<EnqueueOptions> configure,
            CancellationToken cancellationToken = default)
            where TJob : notnull
            => Task.CompletedTask;
    }
}
