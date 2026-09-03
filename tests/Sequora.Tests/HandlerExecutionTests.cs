using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sequora.Internal;

namespace Sequora.Tests;

public sealed class HandlerExecutionTests
{
    [Fact]
    public void AddHandler_WithLifetime_RegistersDescriptor()
    {
        ServiceCollection services = new();
        services.AddSequora()
            .AddHandler<WorkJob, TrackingHandler>(ServiceLifetime.Singleton);

        ServiceDescriptor descriptor = Assert.Single(
            services,
            item => item.ServiceType == typeof(IJobHandler<WorkJob>));

        Assert.Equal(typeof(TrackingHandler), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddHandler_InvalidLifetime_Throws()
    {
        ServiceCollection services = new();
        ISequoraBuilder builder = services.AddSequora();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.AddHandler<WorkJob, TrackingHandler>((ServiceLifetime)42));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.AddHandler<TrackingHandler>((ServiceLifetime)42));
    }

    [Fact]
    public async Task TransientHandler_IsNewInstancePerJob()
    {
        using ExecutionProbe probe = new(remainingCount: 2, disposedCount: 1);
        probe.Disposed.Signal();

        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services => services.AddSingleton(probe),
            configureBuilder: builder => builder.AddHandler<WorkJob, TrackingHandler>(ServiceLifetime.Transient));

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(probe.Remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(2, probe.HandlerIds.Distinct().Count());
    }

    [Fact]
    public async Task ScopedHandler_IsNewInstancePerJobScope()
    {
        using ExecutionProbe probe = new(remainingCount: 2, disposedCount: 1);
        probe.Disposed.Signal();

        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services => services.AddSingleton(probe),
            configureBuilder: builder => builder.AddHandler<WorkJob, TrackingHandler>(ServiceLifetime.Scoped));

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(probe.Remaining.Wait(WorkerHarness.Timeout));
        Assert.Equal(2, probe.HandlerIds.Distinct().Count());
        Assert.Throws<InvalidOperationException>(
            () => harness.Provider.GetRequiredService<IJobHandler<WorkJob>>());
    }

    [Fact]
    public async Task SingletonHandler_IsSharedAcrossJobs()
    {
        using ExecutionProbe probe = new(remainingCount: 2, disposedCount: 1);
        probe.Disposed.Signal();

        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services => services.AddSingleton(probe),
            configureBuilder: builder => builder.AddHandler<WorkJob, TrackingHandler>(ServiceLifetime.Singleton));

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(probe.Remaining.Wait(WorkerHarness.Timeout));
        Assert.Single(probe.HandlerIds.Distinct());
        Assert.Same(
            harness.Provider.GetRequiredService<IJobHandler<WorkJob>>(),
            harness.Provider.GetRequiredService<IJobHandler<WorkJob>>());
    }

    [Fact]
    public async Task SingletonAndTransientDependencies_FollowTheirLifetimes()
    {
        using ExecutionProbe probe = new(remainingCount: 2, disposedCount: 1);
        probe.Disposed.Signal();

        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services =>
            {
                services.AddSingleton(probe);
                services.AddSingleton<SingletonStamp>();
                services.AddTransient<TransientStamp>();
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, LifetimeHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(probe.Remaining.Wait(WorkerHarness.Timeout));
        Assert.Single(probe.DependencyIds.Distinct());
        Assert.Equal(2, probe.HandlerIds.Distinct().Count());
    }

    [Fact]
    public async Task ScopedDbContext_IsResolvedFromJobScopeAndDisposed()
    {
        using ExecutionProbe probe = new(remainingCount: 2, disposedCount: 2);

        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services =>
            {
                services.AddSingleton(probe);
                services.AddScoped<DisposableContext>();
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, DbContextHandler>(ServiceLifetime.Scoped));

        Assert.Throws<InvalidOperationException>(
            () => harness.Provider.GetRequiredService<DisposableContext>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(probe.Remaining.Wait(WorkerHarness.Timeout));
        Assert.True(probe.Disposed.Wait(WorkerHarness.Timeout));
        Assert.Equal(2, probe.DependencyIds.Distinct().Count());
    }

    [Fact]
    public async Task AsyncDisposableScopedService_IsDisposedAfterJob()
    {
        using ExecutionProbe probe = new(remainingCount: 1, disposedCount: 1);

        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services =>
            {
                services.AddSingleton(probe);
                services.AddScoped<AsyncDisposableContext>();
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, AsyncDbContextHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.StartAsync();

        Assert.True(probe.Remaining.Wait(WorkerHarness.Timeout));
        Assert.True(probe.Disposed.Wait(WorkerHarness.Timeout));
    }

    [Fact]
    public async Task ManualServiceRegistration_IsHonoredByWorker()
    {
        using ExecutionProbe probe = new(remainingCount: 1, disposedCount: 1);
        probe.Disposed.Signal();

        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services =>
            {
                services.AddSingleton(probe);
                services.AddTransient<IJobHandler<WorkJob>, TrackingHandler>();
            });

        await harness.Queue.EnqueueAsync(new WorkJob(5));
        await harness.StartAsync();

        Assert.True(probe.Remaining.Wait(WorkerHarness.Timeout));
        Assert.Single(probe.HandlerIds);
    }

    [Fact]
    public async Task MultipleJobTypes_AreDispatchedIndependently()
    {
        using ExecutionProbe probe = new(remainingCount: 3, disposedCount: 1);
        probe.Disposed.Signal();

        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services => services.AddSingleton(probe),
            configureBuilder: builder => builder
                .AddHandler<SendEmailJob, EmailDispatchHandler>()
                .AddHandler<GenerateReportJob, GenerateReportHandler>()
                .AddHandler<SendNotificationJob, SendNotificationHandler>());

        await harness.Queue.EnqueueAsync(new GenerateReportJob("q1"));
        await harness.Queue.EnqueueAsync(new SendEmailJob("a@b.c", "S", "B"));
        await harness.Queue.EnqueueAsync(new SendNotificationJob("user-1"));
        await harness.StartAsync();

        Assert.True(probe.Remaining.Wait(WorkerHarness.Timeout));
        Assert.Contains("report:q1", probe.Processed);
        Assert.Contains("email:a@b.c", probe.Processed);
        Assert.Contains("notify:user-1", probe.Processed);
    }

    [Fact]
    public async Task MissingHandler_ThrowsDedicatedExceptionAndIsNotIgnored()
    {
        JobWorkerLogCapture log = new();
        using ExecutionProbe probe = new(remainingCount: 1, disposedCount: 1);
        probe.Disposed.Signal();

        await using WorkerHarness harness = WorkerHarness.Create(
            configureServices: services =>
            {
                services.AddSingleton<ILogger<JobWorker>>(log);
                services.AddSingleton(probe);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, TrackingHandler>());

        await harness.Queue.EnqueueAsync(new UnhandledJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        SequoraHandlerNotFoundException missing = await log.HandlerNotFound.Task.WaitAsync(WorkerHarness.Timeout);
        Assert.Equal(typeof(UnhandledJob), missing.JobType);
        Assert.Contains("UnhandledJob", missing.Message, StringComparison.Ordinal);
        Assert.True(probe.Remaining.Wait(WorkerHarness.Timeout));
    }

    [Fact]
    public async Task HandlerException_IsLoggedAndDoesNotSkipLaterJobs()
    {
        JobWorkerLogCapture log = new();
        WorkSink sink = new();
        using CountdownEvent remaining = new(2);

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.RetryCount = 0,
            configureServices: services =>
            {
                services.AddSingleton<ILogger<JobWorker>>(log);
                services.AddSingleton(sink);
                services.AddSingleton(remaining);
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, FailingThenCompletingHandler>());

        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await harness.Queue.EnqueueAsync(new WorkJob(2));
        await harness.StartAsync();

        Assert.True(remaining.Wait(WorkerHarness.Timeout));
        Assert.Contains(log.Exceptions, exception => exception is InvalidOperationException);
        Assert.Contains(1, sink.Completed);
        Assert.Contains(2, sink.Completed);
    }

    [Fact]
    public async Task Handler_ReceivesCancellationAndScopeIsDisposed()
    {
        HandlerStarted started = new();
        HandlerCancelled cancelled = new();
        using ExecutionProbe probe = new(remainingCount: 1, disposedCount: 1);

        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.ShutdownBehavior = ShutdownBehavior.Cancel,
            configureServices: services =>
            {
                services.AddSingleton(started);
                services.AddSingleton(cancelled);
                services.AddSingleton(probe);
                services.AddScoped<DisposableContext>();
            },
            configureBuilder: builder => builder.AddHandler<WorkJob, CancelAwareScopedHandler>());

        await harness.StartAsync();
        await harness.Queue.EnqueueAsync(new WorkJob(1));
        await started.Task.WaitAsync(WorkerHarness.Timeout);

        await harness.StopAsync();

        await cancelled.Task.WaitAsync(WorkerHarness.Timeout);
        Assert.True(probe.Disposed.Wait(WorkerHarness.Timeout));
    }

    [Fact]
    public async Task DifferentJobTypes_CanRunConcurrently()
    {
        using ConcurrencyGate gate = new(2);
        await using WorkerHarness harness = WorkerHarness.Create(
            configure: options => options.WorkerCount = 2,
            configureServices: services => services.AddSingleton(gate),
            configureBuilder: builder => builder
                .AddHandler<SendEmailJob, ConcurrentEmailHandler>()
                .AddHandler<SendNotificationJob, ConcurrentNotificationHandler>());

        await harness.Queue.EnqueueAsync(new SendEmailJob("a@b.c", "S", "B"));
        await harness.Queue.EnqueueAsync(new SendNotificationJob("user-1"));
        await harness.StartAsync();

        Assert.True(gate.Entered.Wait(WorkerHarness.Timeout));
        Assert.True(gate.Finished.Wait(WorkerHarness.Timeout));
    }
}
