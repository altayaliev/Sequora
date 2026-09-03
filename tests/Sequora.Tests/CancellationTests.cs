using Microsoft.Extensions.DependencyInjection;
using Sequora.Internal;

namespace Sequora.Tests;

public sealed class CancellationTests
{
    [Fact]
    public async Task EnqueueAsync_CanceledToken_ThrowsBeforeWriting()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            queue.EnqueueAsync(new SendEmailJob("a@b.c", "S", "B"), cts.Token));

        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task EnqueueAsync_CanceledToken_WithConfigure_ThrowsBeforeWriting()
    {
        using ServiceProvider provider = SequoraProvider.Create();
        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            queue.EnqueueAsync(
                new SendEmailJob("a@b.c", "S", "B"),
                options => options.RetryCount = 1,
                cts.Token));

        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task EnqueueAsync_Wait_PropagatesCancellationWhileFull()
    {
        using ServiceProvider provider = SequoraProvider.Create(options =>
        {
            options.Capacity = 1;
            options.QueueFullBehavior = QueueFullBehavior.Wait;
        });

        JobQueue queue = Assert.IsType<JobQueue>(provider.GetRequiredService<IJobQueue>());
        await queue.EnqueueAsync(new SendEmailJob("a@b.c", "1", "1"));

        using CancellationTokenSource cts = new();
        Task enqueue = queue.EnqueueAsync(new SendEmailJob("a@b.c", "2", "2"), cts.Token);
        Assert.False(enqueue.IsCompleted);

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => enqueue);
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public async Task HandlerContract_ReceivesTheProvidedCancellationToken()
    {
        SendEmailHandler handler = new();
        SendEmailJob job = new("a@b.c", "S", "B");
        using CancellationTokenSource cts = new();

        await handler.HandleAsync(job, cts.Token);

        Assert.Single(handler.Calls);
        Assert.Equal(cts.Token, handler.Calls[0].Token);
        Assert.Equal(job, handler.Calls[0].Job);
    }

    [Fact]
    public async Task HandlerContract_CanceledToken_Throws()
    {
        SendEmailHandler handler = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.HandleAsync(new SendEmailJob("a@b.c", "S", "B"), cts.Token));

        Assert.Empty(handler.Calls);
    }
}
