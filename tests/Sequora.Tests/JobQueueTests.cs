using Microsoft.Extensions.DependencyInjection;
using Sequora.Internal;

namespace Sequora.Tests;

public sealed class JobQueueTests
{
    [Fact]
    public async Task EnqueueAsync_AcceptsStronglyTypedJob()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        SendEmailJob job = new("user@example.com", "Welcome", "Hello");

        await queue.EnqueueAsync(job);

        Assert.Equal(1, queue.PendingCount);
        Assert.True(queue.TryPeekPending(out JobEnvelope? envelope));
        SendEmailJob enqueued = Assert.IsType<SendEmailJob>(envelope.Job);
        Assert.Equal(job, enqueued);
    }

    [Fact]
    public async Task EnqueueAsync_AcceptsValueTypeJobs()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new PingJob(7));

        Assert.True(queue.TryReadPending(out JobEnvelope? envelope));
        Assert.Equal(typeof(PingJob), envelope.JobType);
        Assert.Equal(new PingJob(7), Assert.IsType<PingJob>(envelope.Job));
    }

    [Fact]
    public async Task EnqueueAsync_AcceptsMultipleJobTypes()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new SendEmailJob("a@b.c", "S", "B"));
        await queue.EnqueueAsync(new SmsJob("+1000", "Hi"));

        Assert.Equal(2, queue.PendingCount);
        Assert.True(queue.TryReadPending(out JobEnvelope? first));
        Assert.True(queue.TryReadPending(out JobEnvelope? second));
        Assert.IsType<SendEmailJob>(first.Job);
        Assert.IsType<SmsJob>(second.Job);
    }

    [Fact]
    public async Task EnqueueAsync_NullJob_Throws()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        IJobQueue queue = provider.GetRequiredService<IJobQueue>();
        SendEmailJob job = null!;

        await Assert.ThrowsAsync<ArgumentNullException>(() => queue.EnqueueAsync(job));
    }

    [Fact]
    public async Task EnqueueAsync_NullConfigure_Throws()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        IJobQueue queue = provider.GetRequiredService<IJobQueue>();
        Action<EnqueueOptions> configure = null!;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            queue.EnqueueAsync(new SendEmailJob("a@b.c", "S", "B"), configure));
    }

    [Fact]
    public async Task BoundedQueue_Throw_RejectsWhenFull()
    {
        using ServiceProvider provider = SequoraProvider.Create(options =>
        {
            options.Capacity = 1;
            options.QueueFullBehavior = QueueFullBehavior.Throw;
        });

        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        await queue.EnqueueAsync(new SendEmailJob("a@b.c", "1", "1"));

        SequoraQueueFullException exception = await Assert.ThrowsAsync<SequoraQueueFullException>(() =>
            queue.EnqueueAsync(new SendEmailJob("a@b.c", "2", "2")));

        Assert.Equal(1, exception.Capacity);
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public async Task BoundedQueue_Drop_DiscardsIncomingJobWhenFull()
    {
        using ServiceProvider provider = SequoraProvider.Create(options =>
        {
            options.Capacity = 1;
            options.QueueFullBehavior = QueueFullBehavior.Drop;
        });

        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        SendEmailJob first = new("a@b.c", "keep", "keep");
        await queue.EnqueueAsync(first);
        await queue.EnqueueAsync(new SendEmailJob("a@b.c", "drop", "drop"));

        Assert.Equal(1, queue.PendingCount);
        Assert.True(queue.TryPeekPending(out JobEnvelope? envelope));
        Assert.Equal("keep", Assert.IsType<SendEmailJob>(envelope.Job).Subject);
    }

    [Fact]
    public async Task BoundedQueue_Wait_EnqueuesAfterSpaceIsAvailable()
    {
        using ServiceProvider provider = SequoraProvider.Create(options =>
        {
            options.Capacity = 1;
            options.QueueFullBehavior = QueueFullBehavior.Wait;
        });

        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        await queue.EnqueueAsync(new SendEmailJob("a@b.c", "1", "1"));

        Task enqueue = queue.EnqueueAsync(new SendEmailJob("a@b.c", "2", "2"));
        Assert.False(enqueue.IsCompleted);

        Assert.True(queue.TryReadPending(out _));
        await enqueue;

        Assert.Equal(1, queue.PendingCount);
        Assert.True(queue.TryPeekPending(out JobEnvelope? envelope));
        Assert.Equal("2", Assert.IsType<SendEmailJob>(envelope.Job).Subject);
    }

    [Fact]
    public async Task QueueIsRegisteredAsSingleton()
    {
        using ServiceProvider provider = SequoraProvider.Create();

        IJobQueue first = provider.GetRequiredService<IJobQueue>();
        IJobQueue second = provider.GetRequiredService<IJobQueue>();

        Assert.Same(first, second);
        await first.EnqueueAsync(new PingJob(1));
        Assert.Equal(1, Assert.IsType<JobQueue>(second).PendingCount);
    }
}
