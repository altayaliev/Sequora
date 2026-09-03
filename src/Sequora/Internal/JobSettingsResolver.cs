namespace Sequora.Internal;

internal sealed class EffectiveJobSettings
{
    public required int RetryCount { get; init; }

    public required TimeSpan RetryDelay { get; init; }

    public required TimeSpan MaxRetryDelay { get; init; }

    public required RetryBackoffStrategy RetryBackoff { get; init; }

    public required int Priority { get; init; }

    public required TimeSpan Delay { get; init; }

    public string? JobId { get; init; }
}

/// <summary>
/// Applies configuration precedence: queue options, then job-level overrides.
/// Unset (null) job properties inherit the queue value.
/// </summary>
internal static class JobSettingsResolver
{
    public static EffectiveJobSettings Resolve(SequoraOptions queue, EnqueueOptions? job)
    {
        ArgumentNullException.ThrowIfNull(queue);

        if (job is not null)
        {
            EnqueueOptionsValidator.Validate(job);
        }

        return new EffectiveJobSettings
        {
            RetryCount = job?.RetryCount ?? queue.RetryCount,
            RetryDelay = job?.RetryDelay ?? queue.RetryDelay,
            MaxRetryDelay = job?.MaxRetryDelay ?? queue.MaxRetryDelay,
            RetryBackoff = job?.RetryBackoff ?? queue.RetryBackoff,
            Priority = job?.Priority ?? queue.Priority,
            Delay = job?.Delay ?? TimeSpan.Zero,
            JobId = job?.JobId
        };
    }
}
