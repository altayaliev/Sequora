using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sequora.Tests;

public sealed class SequoraOptionsTests
{
    [Fact]
    public void Defaults_AreSafeAndDocumented()
    {
        SequoraOptions options = new();

        Assert.Equal(SequoraOptions.DefaultWorkerCount, options.WorkerCount);
        Assert.Equal(1, options.WorkerCount);
        Assert.Equal(SequoraOptions.Unbounded, options.Capacity);
        Assert.False(options.IsBounded);
        Assert.Equal(SequoraOptions.DefaultRetryCount, options.RetryCount);
        Assert.Equal(3, options.RetryCount);
        Assert.Equal(SequoraOptions.DefaultRetryDelay, options.RetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(1), options.RetryDelay);
        Assert.Equal(SequoraOptions.DefaultMaxRetryDelay, options.MaxRetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(1), options.MaxRetryDelay);
        Assert.Equal(RetryBackoffStrategy.Exponential, options.RetryBackoff);
        Assert.Equal(QueueFullBehavior.Wait, options.QueueFullBehavior);
        Assert.Equal(ShutdownBehavior.Drain, options.ShutdownBehavior);
        Assert.Equal(SequoraOptions.DefaultPriorityFairnessLimit, options.PriorityFairnessLimit);
        Assert.Equal(32, options.PriorityFairnessLimit);
        Assert.Equal(SequoraOptions.DefaultPriority, options.Priority);
        Assert.Equal(0, options.Priority);
    }

    [Fact]
    public void Defaults_AreRegisteredThroughDependencyInjection()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        SequoraOptions options = provider.GetRequiredService<IOptions<SequoraOptions>>().Value;

        Assert.Equal(1, options.WorkerCount);
        Assert.Equal(SequoraOptions.Unbounded, options.Capacity);
        Assert.Equal(3, options.RetryCount);
        Assert.Equal(TimeSpan.FromSeconds(1), options.RetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(1), options.MaxRetryDelay);
        Assert.Equal(RetryBackoffStrategy.Exponential, options.RetryBackoff);
        Assert.Equal(QueueFullBehavior.Wait, options.QueueFullBehavior);
        Assert.Equal(ShutdownBehavior.Drain, options.ShutdownBehavior);
        Assert.Equal(32, options.PriorityFairnessLimit);
        Assert.Equal(0, options.Priority);
    }

    [Fact]
    public void Configure_OverridesGlobalSettings()
    {
        using ServiceProvider provider = SequoraProvider.Create(options =>
        {
            options.WorkerCount = 4;
            options.Capacity = 32;
            options.RetryCount = 7;
            options.RetryDelay = TimeSpan.FromMilliseconds(250);
            options.MaxRetryDelay = TimeSpan.FromSeconds(8);
            options.RetryBackoff = RetryBackoffStrategy.Linear;
            options.QueueFullBehavior = QueueFullBehavior.Throw;
            options.ShutdownBehavior = ShutdownBehavior.Cancel;
            options.PriorityFairnessLimit = 4;
            options.Priority = 7;
        });

        SequoraOptions options = provider.GetRequiredService<IOptions<SequoraOptions>>().Value;

        Assert.Equal(4, options.WorkerCount);
        Assert.Equal(32, options.Capacity);
        Assert.True(options.IsBounded);
        Assert.Equal(7, options.RetryCount);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.RetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(8), options.MaxRetryDelay);
        Assert.Equal(RetryBackoffStrategy.Linear, options.RetryBackoff);
        Assert.Equal(QueueFullBehavior.Throw, options.QueueFullBehavior);
        Assert.Equal(ShutdownBehavior.Cancel, options.ShutdownBehavior);
        Assert.Equal(4, options.PriorityFairnessLimit);
        Assert.Equal(7, options.Priority);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-40)]
    public void InvalidWorkerCount_Throws(int workerCount)
    {
        Assert.Throws<OptionsValidationException>(() => ResolveOptions(options => options.WorkerCount = workerCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(-40)]
    public void InvalidCapacity_Throws(int capacity)
    {
        Assert.Throws<OptionsValidationException>(() => ResolveOptions(options => options.Capacity = capacity));
    }

    [Fact]
    public void UnboundedCapacity_IsValid()
    {
        SequoraOptions options = ResolveOptions(static o => o.Capacity = SequoraOptions.Unbounded);
        Assert.Equal(SequoraOptions.Unbounded, options.Capacity);
        Assert.False(options.IsBounded);
    }

    [Fact]
    public void NegativeRetryCount_Throws()
    {
        Assert.Throws<OptionsValidationException>(() => ResolveOptions(options => options.RetryCount = -1));
    }

    [Fact]
    public void ZeroRetryCount_IsValid()
    {
        SequoraOptions options = ResolveOptions(static o => o.RetryCount = 0);
        Assert.Equal(0, options.RetryCount);
    }

    [Fact]
    public void NegativeRetryDelay_Throws()
    {
        Assert.Throws<OptionsValidationException>(() =>
            ResolveOptions(options => options.RetryDelay = TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void NegativeMaxRetryDelay_Throws()
    {
        Assert.Throws<OptionsValidationException>(() =>
            ResolveOptions(options => options.MaxRetryDelay = TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void ZeroMaxRetryDelay_IsValid()
    {
        SequoraOptions options = ResolveOptions(static o => o.MaxRetryDelay = TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, options.MaxRetryDelay);
    }

    [Fact]
    public void UndefinedRetryBackoff_Throws()
    {
        Assert.Throws<OptionsValidationException>(() =>
            ResolveOptions(options => options.RetryBackoff = (RetryBackoffStrategy)42));
    }

    [Fact]
    public void UndefinedQueueFullBehavior_Throws()
    {
        Assert.Throws<OptionsValidationException>(() =>
            ResolveOptions(options => options.QueueFullBehavior = (QueueFullBehavior)42));
    }

    [Fact]
    public void UndefinedShutdownBehavior_Throws()
    {
        Assert.Throws<OptionsValidationException>(() =>
            ResolveOptions(options => options.ShutdownBehavior = (ShutdownBehavior)42));
    }

    [Fact]
    public void NegativePriorityFairnessLimit_Throws()
    {
        Assert.Throws<OptionsValidationException>(() =>
            ResolveOptions(options => options.PriorityFairnessLimit = -1));
    }

    [Fact]
    public void ZeroPriorityFairnessLimit_IsValid()
    {
        SequoraOptions options = ResolveOptions(static o => o.PriorityFairnessLimit = 0);
        Assert.Equal(0, options.PriorityFairnessLimit);
    }

    [Fact]
    public void InvalidConfiguration_PreventsResolvingQueue()
    {
        using ServiceProvider provider = SequoraProvider.Create(options => options.WorkerCount = 0);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IJobQueue>());

        Assert.Contains(nameof(SequoraOptions.WorkerCount), exception.Message, StringComparison.Ordinal);
    }

    private static SequoraOptions ResolveOptions(Action<SequoraOptions> configure)
    {
        using ServiceProvider provider = SequoraProvider.Create(configure);
        return provider.GetRequiredService<IOptions<SequoraOptions>>().Value;
    }
}
