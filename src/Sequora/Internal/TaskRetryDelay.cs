namespace Sequora.Internal;

internal sealed class TaskRetryDelay : IRetryDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}
