namespace Sequora.Internal;

internal static class EnqueueOptionsValidator
{
    public static void Validate(EnqueueOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.RetryCount is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EnqueueOptions.RetryCount),
                options.RetryCount,
                $"{nameof(EnqueueOptions.RetryCount)} must be greater than or equal to 0.");
        }

        if (options.RetryDelay is { } delay && delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EnqueueOptions.RetryDelay),
                options.RetryDelay,
                $"{nameof(EnqueueOptions.RetryDelay)} must be greater than or equal to {nameof(TimeSpan.Zero)}.");
        }

        if (options.MaxRetryDelay is { } maxDelay && maxDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EnqueueOptions.MaxRetryDelay),
                options.MaxRetryDelay,
                $"{nameof(EnqueueOptions.MaxRetryDelay)} must be greater than or equal to {nameof(TimeSpan.Zero)}.");
        }

        if (options.RetryBackoff is { } backoff && !Enum.IsDefined(backoff))
        {
            throw new ArgumentOutOfRangeException(
                nameof(EnqueueOptions.RetryBackoff),
                options.RetryBackoff,
                $"{nameof(EnqueueOptions.RetryBackoff)} value '{backoff}' is not defined.");
        }

        if (options.JobId is not null)
        {
            if (string.IsNullOrWhiteSpace(options.JobId))
            {
                throw new ArgumentException(
                    $"{nameof(EnqueueOptions.JobId)} must be null or a non-empty, non-whitespace value.",
                    nameof(EnqueueOptions.JobId));
            }

            if (options.JobId.Length > EnqueueOptions.MaxJobIdLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(EnqueueOptions.JobId),
                    options.JobId.Length,
                    $"{nameof(EnqueueOptions.JobId)} length must be at most {EnqueueOptions.MaxJobIdLength}.");
            }
        }

        if (options.Delay is { } enqueueDelay && enqueueDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EnqueueOptions.Delay),
                options.Delay,
                $"{nameof(EnqueueOptions.Delay)} must be greater than or equal to {nameof(TimeSpan.Zero)}.");
        }
    }
}
