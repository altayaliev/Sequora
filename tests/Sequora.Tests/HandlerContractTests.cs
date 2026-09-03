using Microsoft.Extensions.DependencyInjection;

namespace Sequora.Tests;

public sealed class HandlerContractTests
{
    [Fact]
    public void SampleHandler_ImplementsIJobHandler()
    {
        Assert.True(typeof(IJobHandler<SendEmailJob>).IsAssignableFrom(typeof(SendEmailHandler)));
        Assert.NotNull(typeof(IJobHandler<SendEmailJob>).GetMethod(nameof(IJobHandler<SendEmailJob>.HandleAsync)));
    }

    [Fact]
    public void AddHandler_RegistersTypedHandler()
    {
        using ServiceProvider provider = SequoraProvider.Create(
            configure: null,
            builder => builder.AddHandler<SendEmailJob, SendEmailHandler>());

        IJobHandler<SendEmailJob> handler = provider.GetRequiredService<IJobHandler<SendEmailJob>>();
        Assert.IsType<SendEmailHandler>(handler);
    }

    [Fact]
    public void AddHandler_ByImplementation_DiscoversJobType()
    {
        using ServiceProvider provider = SequoraProvider.Create(
            configure: null,
            builder => builder.AddHandler<SmsHandler>());

        IJobHandler<SmsJob> handler = provider.GetRequiredService<IJobHandler<SmsJob>>();
        Assert.IsType<SmsHandler>(handler);
    }

    [Fact]
    public void AddHandler_ByImplementation_RegistersAllImplementedJobTypes()
    {
        using ServiceProvider provider = SequoraProvider.Create(
            configure: null,
            builder => builder.AddHandler<DualHandler>());

        Assert.IsType<DualHandler>(provider.GetRequiredService<IJobHandler<SendEmailJob>>());
        Assert.IsType<DualHandler>(provider.GetRequiredService<IJobHandler<SmsJob>>());
    }

    [Fact]
    public void AddHandler_TypeWithoutContract_Throws()
    {
        ServiceCollection services = new();
        ISequoraBuilder builder = services.AddSequora();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            builder.AddHandler<NotAHandler>);

        Assert.Contains("IJobHandler", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddHandler_NullBuilder_Throws()
    {
        ISequoraBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() => builder.AddHandler<SendEmailHandler>());
    }

    [Fact]
    public void AddHandler_DuplicateJobType_Throws()
    {
        ServiceCollection services = new();
        ISequoraBuilder builder = services.AddSequora();
        builder.AddHandler<SendEmailJob, SendEmailHandler>();

        SequoraHandlerAlreadyRegisteredException exception = Assert.Throws<SequoraHandlerAlreadyRegisteredException>(
            () => builder.AddHandler<SendEmailJob, SecondSendEmailHandler>());

        Assert.Equal(typeof(SendEmailJob), exception.JobType);
        Assert.Equal(typeof(SendEmailHandler), exception.ExistingHandlerType);
        Assert.Equal(typeof(SecondSendEmailHandler), exception.NewHandlerType);

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsType<SendEmailHandler>(provider.GetRequiredService<IJobHandler<SendEmailJob>>());
    }

    [Fact]
    public void AddHandler_WithLifetime_DuplicateJobType_Throws()
    {
        ServiceCollection services = new();
        ISequoraBuilder builder = services.AddSequora();
        builder.AddHandler<SendEmailJob, SendEmailHandler>();

        Assert.Throws<SequoraHandlerAlreadyRegisteredException>(
            () => builder.AddHandler<SendEmailJob, SecondSendEmailHandler>(ServiceLifetime.Scoped));
    }

    [Fact]
    public void AddHandler_ByImplementation_DuplicateJobType_Throws()
    {
        ServiceCollection services = new();
        ISequoraBuilder builder = services.AddSequora();
        builder.AddHandler<SendEmailJob, SendEmailHandler>();

        SequoraHandlerAlreadyRegisteredException exception = Assert.Throws<SequoraHandlerAlreadyRegisteredException>(
            builder.AddHandler<DualHandler>);

        Assert.Equal(typeof(SendEmailJob), exception.JobType);
        Assert.Equal(typeof(SendEmailHandler), exception.ExistingHandlerType);
        Assert.Equal(typeof(DualHandler), exception.NewHandlerType);
    }

    [Fact]
    public void AddHandler_ByImplementation_PartialConflict_DoesNotRegisterAnyInterface()
    {
        ServiceCollection services = new();
        ISequoraBuilder builder = services.AddSequora();
        builder.AddHandler<SendEmailJob, SendEmailHandler>();

        Assert.Throws<SequoraHandlerAlreadyRegisteredException>(builder.AddHandler<DualHandler>);

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IJobHandler<SmsJob>>());
    }

    [Fact]
    public void Handlers_AreTransient()
    {
        using ServiceProvider provider = SequoraProvider.Create(
            configure: null,
            builder => builder.AddHandler<SendEmailJob, SendEmailHandler>());

        IJobHandler<SendEmailJob> first = provider.GetRequiredService<IJobHandler<SendEmailJob>>();
        IJobHandler<SendEmailJob> second = provider.GetRequiredService<IJobHandler<SendEmailJob>>();

        Assert.NotSame(first, second);
    }
}
