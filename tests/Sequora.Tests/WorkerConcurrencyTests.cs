using Microsoft.Extensions.DependencyInjection;

namespace Sequora.Tests;

public sealed class WorkerConcurrencyTests
{
    [Fact]
    public async Task MultipleWorkers_ProcessJobsConcurrently()
    {
        using ConcurrencyGate gate = new(3);
        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.WorkerCount = 3,
            configureServices: services => services.AddSingleton(gate),
            configureBuilder: builder => builder.AddHandler<WorkJob, BarrierHandler>());

        Assert.Equal(3, harness.Worker.WorkerCount);

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.Queue.EnqueueAsync(new WorkJob(3));
        await harness.StartAsync();

        Assert.True(gate.Entered.Wait(WorkerHarness.Timeout));
        Assert.True(gate.Finished.Wait(WorkerHarness.Timeout));
    }

    [Fact]
    public async Task BoundedWait_UnderContention_DrainsAllJobs()
    {
        WorkSink sink = new();
        const int count = 40;
        using CountdownEvent remaining = new(count);
        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options =>
            {
                options.WorkerCount = 2;
                options.Capacity = 5;
                options.QueueFullBehavior = QueueFullBehavior.Wait;
            },
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        await harness.StartAsync();
        await Task.WhenAll(Enumerable.Range(0, count).Select(id => harness.Queue.EnqueueAsync(new WorkJob(id))))
            .WaitAsync(WorkerHarness.Timeout);

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(count, sink.Completed.Count);
    }

    [Fact]
    public async Task ConcurrentProducersAndConsumers_ProcessEveryJob()
    {
        WorkSink sink = new();
        const int count = 100;
        using CountdownEvent remaining = new(count);
        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.WorkerCount = 4,
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        await harness.StartAsync();
        await Task.WhenAll(Enumerable.Range(0, count).Select(id => harness.Queue.EnqueueAsync(new WorkJob(id))))
            .WaitAsync(WorkerHarness.Timeout);

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(count, sink.Completed.Distinct().Count());
    }
}
