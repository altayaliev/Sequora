using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sequora.Internal;

namespace Sequora.Tests;

public sealed class JobEngineTests
{
    [Fact]
    public async Task DequeueAsync_ReturnsEnqueuedJob()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        WorkJob job = new(42);

        await queue.EnqueueAsync(job);
        JobEnvelope? envelope = await queue.DequeueAsync().AsTask().WaitAsync(WorkerHarness.Timeout);

        Assert.NotNull(envelope);
        Assert.Equal(job, Assert.IsType<WorkJob>(envelope.Job));
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task DequeueAsync_AfterComplete_DrainsThenReturnsNull()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        await queue.EnqueueAsync(new WorkJob(1));
        await queue.EnqueueAsync(new WorkJob(2));
        queue.Complete();

        JobEnvelope? first = await queue.DequeueAsync().AsTask().WaitAsync(WorkerHarness.Timeout);
        JobEnvelope? second = await queue.DequeueAsync().AsTask().WaitAsync(WorkerHarness.Timeout);
        JobEnvelope? third = await queue.DequeueAsync().AsTask().WaitAsync(WorkerHarness.Timeout);

        Assert.Equal(1, Assert.IsType<WorkJob>(first!.Job).Id);
        Assert.Equal(2, Assert.IsType<WorkJob>(second!.Job).Id);
        Assert.Null(third);
        Assert.True(queue.IsCompleted);
    }

    [Fact]
    public async Task EnqueueAsync_AfterComplete_ThrowsStopped()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        queue.Complete();

        await Assert.ThrowsAsync<SequoraStoppedException>(() => queue.EnqueueAsync(new WorkJob(1)));
    }

    [Fact]
    public async Task Complete_UnblocksWaitingEnqueue()
    {
        using ServiceProvider provider = SequoraProvider.Create(options =>
        {
            options.Capacity = 1;
            options.QueueFullBehavior = QueueFullBehavior.Wait;
        });

        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        await queue.EnqueueAsync(new WorkJob(1));

        Task waiting = queue.EnqueueAsync(new WorkJob(2));
        Assert.False(waiting.IsCompleted);

        queue.Complete();

        await Assert.ThrowsAsync<SequoraStoppedException>(() => waiting.WaitAsync(WorkerHarness.Timeout));
    }

    [Fact]
    public async Task Complete_IsIdempotent()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());

        queue.Complete();
        queue.Complete();

        Assert.True(queue.IsCompleted);
        await Assert.ThrowsAsync<SequoraStoppedException>(() => queue.EnqueueAsync(new WorkJob(1)));
    }

    [Fact]
    public async Task ConcurrentEnqueue_AllJobsAreAccepted()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        const int count = 200;

        await Task.WhenAll(Enumerable.Range(0, count).Select(id => queue.EnqueueAsync(new WorkJob(id))))
            .WaitAsync(WorkerHarness.Timeout);

        Assert.Equal(count, queue.PendingCount);
    }

    [Fact]
    public async Task BoundedThrow_UnderContention_RejectsExactlyWhenFull()
    {
        using ServiceProvider provider = SequoraProvider.Create(options =>
        {
            options.Capacity = 25;
            options.QueueFullBehavior = QueueFullBehavior.Throw;
        });

        IJobQueue queue = provider.GetRequiredService<IJobQueue>();
        int accepted = 0;
        int rejected = 0;

        await Task.WhenAll(Enumerable.Range(0, 100).Select(async id =>
        {
            try
            {
                await queue.EnqueueAsync(new WorkJob(id)).WaitAsync(WorkerHarness.Timeout);
                Interlocked.Increment(ref accepted);
            }
            catch (SequoraQueueFullException)
            {
                Interlocked.Increment(ref rejected);
            }
        })).WaitAsync(WorkerHarness.Timeout);

        Assert.Equal(25, accepted);
        Assert.Equal(75, rejected);
        Assert.Equal(25, Assert.IsType<JobQueue>(queue).PendingCount);
    }

    [Fact]
    public async Task DequeueAsync_CanceledWhileWaiting_Throws()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        using CancellationTokenSource cts = new();

        Task<JobEnvelope?> dequeue = queue.DequeueAsync(cts.Token).AsTask();
        Assert.False(dequeue.IsCompleted);

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => dequeue.WaitAsync(WorkerHarness.Timeout));
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void AddSequora_RegistersHostedWorker()
    {
        using ServiceProvider provider = SequoraProvider.Create();

        IHostedService[] hosted = [.. provider.GetServices<IHostedService>()];
        Assert.Contains(hosted, service => service is JobWorker);
    }

    [Fact]
    public void AddSequora_IsIdempotentForWorkerRegistration()
    {
        ServiceCollection services = new();
        services.AddSequora();
        services.AddSequora();

        ServiceDescriptor[] workers = [.. services.Where(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(JobWorker))];

        Assert.Single(workers);
    }
}
