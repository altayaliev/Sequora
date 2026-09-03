namespace Sequora.Tests;

public sealed record SendEmailJob(string To, string Subject, string Body);

public sealed record SmsJob(string PhoneNumber, string Text);

public readonly record struct PingJob(int Sequence);

public sealed class SendEmailHandler : IJobHandler<SendEmailJob>
{
    public List<(SendEmailJob Job, CancellationToken Token)> Calls { get; } = [];

    public Task HandleAsync(SendEmailJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((job, cancellationToken));
        return Task.CompletedTask;
    }
}

public sealed class SecondSendEmailHandler : IJobHandler<SendEmailJob>
{
    public Task HandleAsync(SendEmailJob job, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class SmsHandler : IJobHandler<SmsJob>
{
    public Task HandleAsync(SmsJob job, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class DualHandler : IJobHandler<SendEmailJob>, IJobHandler<SmsJob>
{
    public Task HandleAsync(SendEmailJob job, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(SmsJob job, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class NotAHandler
{
}
