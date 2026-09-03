using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sequora.Internal;

namespace Sequora.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void AddSequora_WithoutCallback_UsesDocumentedDefaults()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        SequoraOptions options = provider.GetRequiredService<IOptions<SequoraOptions>>().Value;

        Assert.Equal(1, options.WorkerCount);
        Assert.Equal(SequoraOptions.Unbounded, options.Capacity);
        Assert.Equal(3, options.RetryCount);
        Assert.Equal(TimeSpan.FromSeconds(1), options.RetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(1), options.MaxRetryDelay);
        Assert.Equal(RetryBackoffStrategy.Exponential, options.RetryBackoff);
        Assert.Equal(SequoraOptions.DefaultPriority, options.Priority);
        Assert.Equal(0, options.Priority);
        Assert.Equal(QueueFullBehavior.Wait, options.QueueFullBehavior);
        Assert.Equal(ShutdownBehavior.Drain, options.ShutdownBehavior);
        Assert.Equal(32, options.PriorityFairnessLimit);
        Assert.False(options.IsBounded);
    }

    [Fact]
    public void QueueConfiguration_OverridesDefaults()
    {
        using ServiceProvider provider = SequoraProvider.Create(options =>
        {
            options.WorkerCount = 4;
            options.RetryCount = 9;
            options.Priority = 6;
        });

        SequoraOptions options = provider.GetRequiredService<IOptions<SequoraOptions>>().Value;
        Assert.Equal(4, options.WorkerCount);
        Assert.Equal(9, options.RetryCount);
        Assert.Equal(6, options.Priority);
        Assert.Equal(TimeSpan.FromSeconds(1), options.RetryDelay);
    }

    [Fact]
    public void BuilderConfigure_OverlaysAddSequoraCallback()
    {
        ServiceCollection services = new();
        services.AddSequora(options =>
            {
                options.WorkerCount = 2;
                options.RetryCount = 9;
                options.Priority = 3;
            })
            .Configure(options => options.WorkerCount = 8);

        using ServiceProvider provider = services.BuildServiceProvider();
        SequoraOptions options = provider.GetRequiredService<IOptions<SequoraOptions>>().Value;

        Assert.Equal(8, options.WorkerCount);
        Assert.Equal(9, options.RetryCount);
        Assert.Equal(3, options.Priority);
    }

    [Fact]
    public void Configure_Null_Throws()
    {
        ServiceCollection services = new();
        ISequoraBuilder builder = services.AddSequora();

        Assert.Throws<ArgumentNullException>(() => builder.Configure(null!));
    }

    [Fact]
    public void Resolver_NullQueue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => JobSettingsResolver.Resolve(null!, job: null));
    }

    [Fact]
    public void Resolver_NullJob_UsesQueueIncludingPriority()
    {
        SequoraOptions queue = new()
        {
            RetryCount = 1,
            RetryDelay = TimeSpan.FromMilliseconds(40),
            MaxRetryDelay = TimeSpan.FromSeconds(8),
            RetryBackoff = RetryBackoffStrategy.Linear,
            Priority = 5
        };

        EffectiveJobSettings settings = JobSettingsResolver.Resolve(queue, job: null);

        Assert.Equal(1, settings.RetryCount);
        Assert.Equal(TimeSpan.FromMilliseconds(40), settings.RetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(8), settings.MaxRetryDelay);
        Assert.Equal(RetryBackoffStrategy.Linear, settings.RetryBackoff);
        Assert.Equal(5, settings.Priority);
        Assert.Equal(TimeSpan.Zero, settings.Delay);
        Assert.Null(settings.JobId);
    }

    [Fact]
    public void Resolver_JobOverridesOnlySpecifiedProperties()
    {
        SequoraOptions queue = new()
        {
            RetryCount = 9,
            RetryDelay = TimeSpan.FromSeconds(2),
            MaxRetryDelay = TimeSpan.FromSeconds(10),
            RetryBackoff = RetryBackoffStrategy.Linear,
            Priority = 3
        };
        EnqueueOptions job = new()
        {
            RetryCount = 1,
            Priority = 8,
            JobId = "invoice-1"
        };

        EffectiveJobSettings settings = JobSettingsResolver.Resolve(queue, job);

        Assert.Equal(1, settings.RetryCount);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.RetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.MaxRetryDelay);
        Assert.Equal(RetryBackoffStrategy.Linear, settings.RetryBackoff);
        Assert.Equal(8, settings.Priority);
        Assert.Equal(TimeSpan.Zero, settings.Delay);
        Assert.Equal("invoice-1", settings.JobId);
    }

    [Fact]
    public void Resolver_InvalidJob_Throws()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            JobSettingsResolver.Resolve(new SequoraOptions(), new EnqueueOptions { RetryCount = -1 }));

        Assert.Equal(nameof(EnqueueOptions.RetryCount), exception.ParamName);
        Assert.Contains(nameof(EnqueueOptions.RetryCount), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Precedence_JobOverridesQueue_QueueOverridesDefaults()
    {
        ServiceCollection services = new();
        services.AddSequora(options =>
            {
                options.RetryCount = 9;
                options.RetryDelay = TimeSpan.FromMilliseconds(40);
                options.Priority = 3;
            })
            .Configure(options => options.RetryCount = 6);

        using ServiceProvider provider = services.BuildServiceProvider();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(
            new SendEmailJob("a@b.c", "S", "B"),
            options => options.RetryCount = 1);

        Assert.True(queue.TryPeekPending(out JobEnvelope? envelope));
        Assert.Equal(1, envelope.RetryCount);
        Assert.Equal(TimeSpan.FromMilliseconds(40), envelope.RetryDelay);
        Assert.Equal(3, envelope.Priority);
        Assert.Equal(RetryBackoffStrategy.Exponential, envelope.RetryBackoff);
    }

    [Fact]
    public async Task UnsetJobPriority_InheritsQueuePriority()
    {
        using ServiceProvider provider = SequoraProvider.Create(options => options.Priority = 11);
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new SendEmailJob("a@b.c", "S", "B"));

        Assert.True(queue.TryPeekPending(out JobEnvelope? envelope));
        Assert.Equal(11, envelope.Priority);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task AnyPriority_IsValidAtQueueAndJobLevel(int priority)
    {
        using ServiceProvider provider = SequoraProvider.Create(options => options.Priority = priority);
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(
            new SendEmailJob("a@b.c", "S", "B"),
            options => options.Priority = priority);

        Assert.True(queue.TryPeekPending(out JobEnvelope? envelope));
        Assert.Equal(priority, envelope.Priority);
        Assert.Equal(priority, provider.GetRequiredService<IOptions<SequoraOptions>>().Value.Priority);
    }

    [Fact]
    public void InvalidWorkerCount_MessageNamesTheProperty()
    {
        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => ResolveOptions(options => options.WorkerCount = 0));

        Assert.Contains(nameof(SequoraOptions.WorkerCount), exception.Message, StringComparison.Ordinal);
        Assert.Contains("at least 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidCapacity_MessageNamesTheProperty()
    {
        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => ResolveOptions(options => options.Capacity = 0));

        Assert.Contains(nameof(SequoraOptions.Capacity), exception.Message, StringComparison.Ordinal);
        Assert.Contains("0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NegativeRetryCount_MessageNamesTheProperty()
    {
        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => ResolveOptions(options => options.RetryCount = -4));

        Assert.Contains(nameof(SequoraOptions.RetryCount), exception.Message, StringComparison.Ordinal);
        Assert.Contains("-4", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NegativeRetryDelay_MessageNamesTheProperty()
    {
        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => ResolveOptions(options => options.RetryDelay = TimeSpan.FromMilliseconds(-1)));

        Assert.Contains(nameof(SequoraOptions.RetryDelay), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NegativeJobDelay_MessageNamesTheProperty()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        IJobQueue queue = provider.GetRequiredService<IJobQueue>();

        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options => options.Delay = TimeSpan.FromSeconds(-1)));

        Assert.Equal(nameof(EnqueueOptions.Delay), exception.ParamName);
        Assert.Contains(nameof(EnqueueOptions.Delay), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, Assert.IsType<JobQueue>(queue).PendingCount);
    }

    [Fact]
    public async Task InvalidJobId_MessageNamesTheProperty()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        IJobQueue queue = provider.GetRequiredService<IJobQueue>();

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options => options.JobId = " "));

        Assert.Equal(nameof(EnqueueOptions.JobId), exception.ParamName);
    }

    [Fact]
    public async Task SimpleUsage_AddSequoraWithoutOptions_Enqueues()
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
    public async Task AdvancedUsage_FluentConfigureThenJobOverride()
    {
        ServiceCollection services = new();
        services.AddSequora()
            .Configure(options =>
            {
                options.WorkerCount = 4;
                options.Capacity = 1024;
                options.RetryCount = 5;
                options.Priority = 2;
            })
            .AddHandler<SendEmailJob, SendEmailHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(
            new SendEmailJob("user@example.com", "Hi", "Hello"),
            options => options.RetryCount = 1);

        Assert.True(queue.TryPeekPending(out JobEnvelope? envelope));
        Assert.Equal(1, envelope.RetryCount);
        Assert.Equal(2, envelope.Priority);
        Assert.Equal(4, provider.GetRequiredService<IOptions<SequoraOptions>>().Value.WorkerCount);
    }

    [Fact]
    public async Task Enqueue_CanceledToken_DoesNotApplyJobConfiguration()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options =>
                {
                    options.JobId = "should-not-register";
                    options.Priority = 9;
                },
                cts.Token));

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(0, queue.TrackedJobIdCount);
    }

    private static SequoraOptions ResolveOptions(Action<SequoraOptions> configure)
    {
        using ServiceProvider provider = SequoraProvider.Create(configure);
        return provider.GetRequiredService<IOptions<SequoraOptions>>().Value;
    }
}
