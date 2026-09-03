using Microsoft.Extensions.DependencyInjection;
using Sequora.Internal;

namespace Sequora.Tests;

public sealed class EnqueueOptionsTests
{
    [Fact]
    public void Defaults_AreUnsetAndInheritGlobalConfiguration()
    {
        EnqueueOptions options = new();

        Assert.Null(options.RetryCount);
        Assert.Null(options.RetryDelay);
        Assert.Null(options.MaxRetryDelay);
        Assert.Null(options.RetryBackoff);
        Assert.Null(options.JobId);
        Assert.Null(options.Delay);
        Assert.Null(options.Priority);
    }

    [Fact]
    public async Task UnsetJobOptions_InheritGlobalValues()
    {
        using ServiceProvider provider = SequoraProvider.Create(options =>
        {
            options.RetryCount = 9;
            options.RetryDelay = TimeSpan.FromMilliseconds(40);
            options.MaxRetryDelay = TimeSpan.FromSeconds(12);
            options.RetryBackoff = RetryBackoffStrategy.Constant;
            options.Priority = 4;
        });

        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        SendEmailJob job = new("a@b.c", "S", "B");

        await queue.EnqueueAsync(job);

        Assert.True(queue.TryPeekPending(out JobEnvelope? envelope));
        Assert.Equal(9, envelope.RetryCount);
        Assert.Equal(TimeSpan.FromMilliseconds(40), envelope.RetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(12), envelope.MaxRetryDelay);
        Assert.Equal(RetryBackoffStrategy.Constant, envelope.RetryBackoff);
        Assert.Same(job, envelope.Job);
        Assert.Equal(typeof(SendEmailJob), envelope.JobType);
        Assert.Null(envelope.JobId);
        Assert.Equal(TimeSpan.Zero, envelope.Delay);
        Assert.Equal(4, envelope.Priority);
        Assert.Equal(JobLifecycleState.Queued, envelope.Lifecycle.State);
    }

    [Fact]
    public async Task ConfigureCallback_OverridesOnlySpecifiedSettings()
    {
        using ServiceProvider provider = SequoraProvider.Create(options =>
        {
            options.RetryCount = 2;
            options.RetryDelay = TimeSpan.FromSeconds(3);
            options.MaxRetryDelay = TimeSpan.FromSeconds(9);
            options.RetryBackoff = RetryBackoffStrategy.Linear;
        });

        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(
            new SendEmailJob("a@b.c", "S", "B"),
            options => options.RetryCount = 5);

        Assert.True(queue.TryReadPending(out JobEnvelope? envelope));
        Assert.Equal(5, envelope.RetryCount);
        Assert.Equal(TimeSpan.FromSeconds(3), envelope.RetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(9), envelope.MaxRetryDelay);
        Assert.Equal(RetryBackoffStrategy.Linear, envelope.RetryBackoff);
        Assert.Equal(SequoraOptions.DefaultPriority, envelope.Priority);
    }

    [Fact]
    public async Task ConfigureCallback_CanOverrideAllRetrySettings()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(
            new SendEmailJob("a@b.c", "S", "B"),
            options =>
            {
                options.RetryCount = 1;
                options.RetryDelay = TimeSpan.Zero;
                options.MaxRetryDelay = TimeSpan.FromMilliseconds(5);
                options.RetryBackoff = RetryBackoffStrategy.Constant;
            });

        Assert.True(queue.TryReadPending(out JobEnvelope? envelope));
        Assert.Equal(1, envelope.RetryCount);
        Assert.Equal(TimeSpan.Zero, envelope.RetryDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(5), envelope.MaxRetryDelay);
        Assert.Equal(RetryBackoffStrategy.Constant, envelope.RetryBackoff);
    }

    [Fact]
    public async Task NegativeJobRetryCount_Throws()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        IJobQueue queue = provider.GetRequiredService<IJobQueue>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options => options.RetryCount = -1));
    }

    [Fact]
    public async Task NegativeJobRetryDelay_Throws()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        IJobQueue queue = provider.GetRequiredService<IJobQueue>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options => options.RetryDelay = TimeSpan.FromSeconds(-2)));
    }

    [Fact]
    public async Task NegativeJobMaxRetryDelay_Throws()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        IJobQueue queue = provider.GetRequiredService<IJobQueue>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options => options.MaxRetryDelay = TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public async Task UndefinedJobBackoff_Throws()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        IJobQueue queue = provider.GetRequiredService<IJobQueue>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options => options.RetryBackoff = (RetryBackoffStrategy)99));
    }

    [Fact]
    public async Task InvalidJobOptions_DoNotEnqueueTheJob()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options => options.RetryCount = -3));

        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task JobId_IsStoredOnTheEnvelope()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(
            new SendEmailJob("a@b.c", "S", "B"),
            options => options.JobId = "invoice-email-123");

        Assert.True(queue.TryPeekPending(out JobEnvelope? envelope));
        Assert.Equal("invoice-email-123", envelope.JobId);
        Assert.Equal(JobLifecycleState.Queued, envelope.Lifecycle.State);
        Assert.True(queue.IsJobIdActive("invoice-email-123"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task InvalidJobId_ThrowsAndDoesNotEnqueue(string jobId)
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options => options.JobId = jobId));

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(0, queue.TrackedJobIdCount);
    }

    [Fact]
    public async Task JobIdLongerThanMaximum_Throws()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        string jobId = new('x', EnqueueOptions.MaxJobIdLength + 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options => options.JobId = jobId));

        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task DelayAndPriority_AreStoredOnTheEnvelope()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(
            new SendEmailJob("a@b.c", "S", "B"),
            options =>
            {
                options.Delay = TimeSpan.Zero;
                options.Priority = 7;
            });

        Assert.True(queue.TryPeekPending(out JobEnvelope? envelope));
        Assert.Equal(TimeSpan.Zero, envelope.Delay);
        Assert.Equal(7, envelope.Priority);
    }

    [Fact]
    public async Task NegativeDelay_ThrowsAndDoesNotEnqueue()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options => options.Delay = TimeSpan.FromSeconds(-1)));

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(0, queue.DelayedCount);
    }
}
