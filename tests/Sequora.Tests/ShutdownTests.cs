using Microsoft.Extensions.DependencyInjection;

namespace Sequora.Tests;

public sealed class ShutdownTests
{
    [Fact]
    public async Task CancelShutdown_CancelsInFlightHandler()
    {
        HandlerStarted started = new();
        HandlerCancelled cancelled = new();

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.ShutdownBehavior = ShutdownBehavior.Cancel,
            configureServices: services =>
            {
                services.AddSingleton(started);
                services.AddSingleton(cancelled);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, CancelAwareHandler>());

        await harness.StartAsync();
        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await started.Task.WaitAsync(WorkerHarness.Timeout);

        await harness.StopAsync();

        await cancelled.Task.WaitAsync(WorkerHarness.Timeout);
        Assert.True(harness.ConcreteQueue.IsCompleted);
    }

    [Fact]
    public async Task DrainShutdown_DoesNotCancelInFlightHandler()
    {
        HandlerStarted started = new();
        HandlerAllowComplete allowComplete = new();
        ObservedExecutionToken observedToken = new();

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.ShutdownBehavior = ShutdownBehavior.Drain,
            configureServices: services =>
            {
                services.AddSingleton(started);
                services.AddSingleton(allowComplete);
                services.AddSingleton(observedToken);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, BlockingHandler>());

        await harness.StartAsync();
        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await started.Task.WaitAsync(WorkerHarness.Timeout);

        Task stopping = harness.Worker.StopAsync(CancellationToken.None);
        CancellationToken token = await observedToken.Task.WaitAsync(WorkerHarness.Timeout);

        Assert.False(token.CanBeCanceled);
        Assert.False(stopping.IsCompleted);

        allowComplete.TrySetResult();
        await stopping.WaitAsync(WorkerHarness.Timeout);
        Assert.True(harness.ConcreteQueue.IsCompleted);
    }

    [Fact]
    public async Task DrainShutdown_ProcessesQueuedJobs()
    {
        WorkSink sink = new();
        using CountdownEvent remaining = new(3);
        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.ShutdownBehavior = ShutdownBehavior.Drain,
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.Queue.EnqueueAsync(new WorkJob(3));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        await harness.StopAsync();

        Assert.Equal(3, sink.Completed.Count);
        await Assert.ThrowsAsync<SequoraStoppedException>(() => harness.Queue.EnqueueAsync(new WorkJob(4)));
    }

    [Fact]
    public async Task Stop_EmptyQueue_CompletesWithoutDeadlock()
    {
        await using WorkerHarness harness = WorkerHarness.Create(
            configureBuilder: builder => builder.AddHandler<WorkJob, CompletingHandler>(),
            configureServices: services => services.AddSingleton(new WorkSink()));

        await harness.StartAsync();
        await harness.StopAsync();

        Assert.True(harness.ConcreteQueue.IsCompleted);
    }

    [Fact]
    public async Task ShutdownWhileEnqueueing_DoesNotThrowUnexpectedExceptions()
    {
        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options =>
            {
                options.Capacity = 8;
                options.QueueFullBehavior = QueueFullBehavior.Wait;
                options.WorkerCount = 1;
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, CompletingHandler>(),
            configureServices: services => services.AddSingleton(new WorkSink()));

        await harness.StartAsync();

        Task[] producers = [.. Enumerable.Range(0, 50).Select(async id =>
        {
            try
            {
                await harness.Queue.EnqueueAsync(new WorkJob(id)).WaitAsync(WorkerHarness.Timeout);
            }
            catch (SequoraStoppedException)
            {
            }
        })];

        await harness.StopAsync();
        await Task.WhenAll(producers).WaitAsync(WorkerHarness.Timeout);
    }

    [Fact]
    public async Task Cancel_WhileWaitingForWork_StopsWorker()
    {
        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.ShutdownBehavior = ShutdownBehavior.Cancel,
            configureBuilder: builder => builder.AddHandler<WorkJob, CompletingHandler>(),
            configureServices: services => services.AddSingleton(new WorkSink()));

        await harness.StartAsync();
        await harness.StopAsync();

        Assert.True(harness.ConcreteQueue.IsCompleted);
        await Assert.ThrowsAsync<SequoraStoppedException>(() => harness.Queue.EnqueueAsync(new WorkJob(1)));
    }
}
