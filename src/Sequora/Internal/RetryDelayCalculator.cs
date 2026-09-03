namespace Sequora.Internal;

internal static class RetryDelayCalculator
{
    /// <summary>
    /// Computes the wait before a retry. <paramref name="retryNumber"/> is 1-based:
    /// the first retry after the initial attempt is 1.
    /// </summary>
    public static TimeSpan Compute(
        TimeSpan retryDelay,
        RetryBackoffStrategy strategy,
        int retryNumber,
        TimeSpan maxRetryDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retryNumber, 1);

        if (retryDelay <= TimeSpan.Zero || maxRetryDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        double factor = strategy switch
        {
            RetryBackoffStrategy.Linear => retryNumber,
            RetryBackoffStrategy.Exponential => Math.Pow(2, retryNumber - 1),
            _ => 1d
        };

        double ticks = retryDelay.Ticks * factor;
        if (double.IsNaN(ticks) || double.IsInfinity(ticks) || ticks >= maxRetryDelay.Ticks)
        {
            return maxRetryDelay;
        }

        if (ticks <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromTicks((long)ticks);
    }
}
