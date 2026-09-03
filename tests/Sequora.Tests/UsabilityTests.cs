using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sequora.Internal;

namespace Sequora.Tests;

public sealed class UsabilityTests
{
    [Fact]
    public async Task DocumentedSimplePath_CompilesAndEnqueues()
    {
        ServiceCollection services = new();
        services.AddSequora()
            .AddHandler<SendEmailJob, SendEmailHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        IJobQueue queue = provider.GetRequiredService<IJobQueue>();

        await queue.EnqueueAsync(new SendEmailJob("user@example.com", "Hi", "Hello"));

        IJobHandler<SendEmailJob> handler = provider.GetRequiredService<IJobHandler<SendEmailJob>>();
        Assert.IsType<SendEmailHandler>(handler);
    }

    [Fact]
    public async Task DocumentedQuickStart_ResolvesQueueFromProvider()
    {
        ServiceCollection services = new();
        services.AddSequora()
            .AddHandler<SendEmailJob, SendEmailHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        IJobQueue queue = provider.GetRequiredService<IJobQueue>();

        await queue.EnqueueAsync(new SendEmailJob("user@example.com", "Hi", "Hello"));
        Assert.Equal(1, Assert.IsType<JobQueue>(queue).PendingCount);
    }

    [Fact]
    public void DocumentedHandlerDiscoveryAndLifetime_Register()
    {
        ServiceCollection services = new();
        services.AddSequora()
            .AddHandler<SendEmailJob, SendEmailHandler>()
            .AddHandler<SmsHandler>(ServiceLifetime.Scoped);

        ServiceDescriptor email = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IJobHandler<SendEmailJob>));
        Assert.Equal(ServiceLifetime.Transient, email.Lifetime);

        ServiceDescriptor sms = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IJobHandler<SmsJob>));
        Assert.Equal(ServiceLifetime.Scoped, sms.Lifetime);
    }

    [Fact]
    public void DocumentedAddSequoraCallback_AppliesQueueConfiguration()
    {
        ServiceCollection services = new();
        services.AddSequora(options => options.WorkerCount = 4)
            .AddHandler<SendEmailJob, SendEmailHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Equal(4, provider.GetRequiredService<IOptions<SequoraOptions>>().Value.WorkerCount);
    }

    [Fact]
    public async Task DocumentedAdvancedPath_AppliesGlobalAndJobConfiguration()
    {
        ServiceCollection services = new();
        services.AddSequora()
            .Configure(options =>
            {
                options.WorkerCount = 4;
                options.Capacity = 1024;
                options.RetryCount = 5;
                options.RetryDelay = TimeSpan.FromSeconds(1);
                options.MaxRetryDelay = TimeSpan.FromMinutes(1);
                options.RetryBackoff = RetryBackoffStrategy.Exponential;
                options.Priority = 0;
                options.PriorityFairnessLimit = 32;
                options.QueueFullBehavior = QueueFullBehavior.Wait;
                options.ShutdownBehavior = ShutdownBehavior.Drain;
            })
            .AddHandler<SendEmailJob, SendEmailHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        IJobQueue queue = provider.GetRequiredService<IJobQueue>();
        SequoraOptions configured = provider.GetRequiredService<IOptions<SequoraOptions>>().Value;

        Assert.Equal(4, configured.WorkerCount);
        Assert.Equal(1024, configured.Capacity);
        Assert.Equal(5, configured.RetryCount);
        Assert.Equal(0, configured.Priority);
        Assert.Equal(RetryBackoffStrategy.Exponential, configured.RetryBackoff);
        Assert.Equal(QueueFullBehavior.Wait, configured.QueueFullBehavior);
        Assert.Equal(ShutdownBehavior.Drain, configured.ShutdownBehavior);

        await queue.EnqueueAsync(
            new SendEmailJob("user@example.com", "Invoice", "Your invoice is ready."),
            options =>
            {
                options.RetryCount = 5;
                options.RetryDelay = TimeSpan.FromMilliseconds(200);
                options.RetryBackoff = RetryBackoffStrategy.Constant;
                options.JobId = "invoice-email-123";
                options.Delay = TimeSpan.Zero;
                options.Priority = 10;
            });

        JobQueue internalQueue = Assert.IsType<JobQueue>(queue);
        Assert.True(internalQueue.TryPeekPending(out JobEnvelope? envelope));
        Assert.Equal(5, envelope.RetryCount);
        Assert.Equal(TimeSpan.FromMilliseconds(200), envelope.RetryDelay);
        Assert.Equal(RetryBackoffStrategy.Constant, envelope.RetryBackoff);
        Assert.Equal("invoice-email-123", envelope.JobId);
        Assert.Equal(10, envelope.Priority);
        Assert.Equal(TimeSpan.Zero, envelope.Delay);

        await Assert.ThrowsAsync<SequoraDuplicateJobException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("user@example.com", "Invoice", "Your invoice is ready."),
                options => options.JobId = "invoice-email-123"));
    }

    [Fact]
    public async Task DocumentedEnqueue_HonorsCancellationTokenBeforeAccept()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        IJobQueue queue = provider.GetRequiredService<IJobQueue>();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            queue.EnqueueAsync(new SendEmailJob("user@example.com", "Hi", "Hello"), cts.Token));
    }
}
