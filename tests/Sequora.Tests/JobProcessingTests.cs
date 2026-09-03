using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sequora.Tests;

public sealed class JobProcessingTests
{
    [Fact]
    public async Task Worker_ExecutesRegisteredHandler()
    {
        WorkSink sink = new();
        using CountdownEvent remaining = new(1);
        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        await harness.StartAsync();
        await harness.Queue.EnqueueAsync(new WorkJob(7));

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(7, Assert.Single(sink.Completed));
    }

    [Fact]
    public async Task Worker_ProcessesMultipleJobsSequentiallyByDefault()
    {
        WorkSink sink = new();
        using CountdownEvent remaining = new(5);
        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        await harness.StartAsync();
        for (int id = 0; id < 5; id++)
        {
            await harness.Queue.EnqueueAsync(new WorkJob(id));
        }

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(5, sink.Completed.Count);
        Assert.Equal([0, 1, 2, 3, 4], sink.Completed.OrderBy(id => id));
    }

    [Fact]
    public async Task FailedJob_DoesNotStopSubsequentJobs()
    {
        WorkSink sink = new();
        using CountdownEvent remaining = new(2);
        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.RetryCount = 0,
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, FailingThenCompletingHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Contains(1, sink.Completed);
        Assert.Contains(2, sink.Completed);
    }

    [Fact]
    public async Task MissingHandler_DoesNotStopSubsequentJobs()
    {
        WorkSink sink = new();
        using CountdownEvent remaining = new(1);
        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, SignalingHandler>());

        await harness.Queue.EnqueueAsync(new UnhandledJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(2, Assert.Single(sink.Completed));
    }

    [Fact]
    public async Task Handler_IsResolvedFromANewScopePerJob()
    {
        WorkSink sink = new();
        using CountdownEvent remaining = new(2);
        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
                services.AddScoped<ScopeMarker>();
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, ScopedHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(2, sink.ScopeIds.Distinct().Count());
    }

    [Fact]
    public async Task Host_StartsWorkerAutomatically()
    {
        WorkSink sink = new();
        using CountdownEvent remaining = new(1);
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(sink);
        builder.Services.AddSingleton(remaining);
        builder.Services.AddSequora()
            .AddHandler<WorkJob, SignalingHandler>();

        using IHost host = builder.Build();
        await host.StartAsync().WaitAsync(WorkerHarness.Timeout);

        IJobQueue queue = host.Services.GetRequiredService<IJobQueue>();
        await queue.EnqueueAsync(new WorkJob(99));

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(99, Assert.Single(sink.Completed));

        await host.StopAsync().WaitAsync(WorkerHarness.Timeout);
    }
}
