using Sequora.Internal;

namespace Sequora.Tests;

public sealed class RetryDelayCalculatorTests
{
    [Fact]
    public void Constant_UsesTheSameDelayOnEveryRetry()
    {
        TimeSpan delay = TimeSpan.FromMilliseconds(100);

        Assert.Equal(delay, RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Constant, 1, TimeSpan.FromSeconds(10)));
        Assert.Equal(delay, RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Constant, 2, TimeSpan.FromSeconds(10)));
        Assert.Equal(delay, RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Constant, 3, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Linear_MultipliesByRetryNumber()
    {
        TimeSpan delay = TimeSpan.FromMilliseconds(100);

        Assert.Equal(TimeSpan.FromMilliseconds(100), RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Linear, 1, TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromMilliseconds(200), RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Linear, 2, TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromMilliseconds(300), RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Linear, 3, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Exponential_DoublesFromTheBaseDelay()
    {
        TimeSpan delay = TimeSpan.FromMilliseconds(100);

        Assert.Equal(TimeSpan.FromMilliseconds(100), RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Exponential, 1, TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromMilliseconds(200), RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Exponential, 2, TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromMilliseconds(400), RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Exponential, 3, TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromMilliseconds(800), RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Exponential, 4, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void MaxRetryDelay_CapsExponentialGrowth()
    {
        TimeSpan delay = TimeSpan.FromMilliseconds(100);
        TimeSpan max = TimeSpan.FromMilliseconds(250);

        Assert.Equal(TimeSpan.FromMilliseconds(100), RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Exponential, 1, max));
        Assert.Equal(TimeSpan.FromMilliseconds(200), RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Exponential, 2, max));
        Assert.Equal(max, RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Exponential, 3, max));
        Assert.Equal(max, RetryDelayCalculator.Compute(delay, RetryBackoffStrategy.Exponential, 4, max));
    }

    [Fact]
    public void ZeroRetryDelay_ProducesZeroWait()
    {
        Assert.Equal(
            TimeSpan.Zero,
            RetryDelayCalculator.Compute(TimeSpan.Zero, RetryBackoffStrategy.Exponential, 3, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ZeroMaxRetryDelay_ProducesZeroWait()
    {
        Assert.Equal(
            TimeSpan.Zero,
            RetryDelayCalculator.Compute(TimeSpan.FromSeconds(5), RetryBackoffStrategy.Exponential, 3, TimeSpan.Zero));
    }

    [Fact]
    public void Exponential_DoesNotOverflowToUnboundedDelay()
    {
        TimeSpan max = TimeSpan.FromMinutes(1);
        TimeSpan computed = RetryDelayCalculator.Compute(
            TimeSpan.FromHours(1),
            RetryBackoffStrategy.Exponential,
            retryNumber: 40,
            max);

        Assert.Equal(max, computed);
    }

    [Fact]
    public void RetryNumberLessThanOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetryDelayCalculator.Compute(
                TimeSpan.FromMilliseconds(10),
                RetryBackoffStrategy.Constant,
                retryNumber: 0,
                TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task TaskRetryDelay_HonorsAnAlreadyCanceledTokenWithoutWaiting()
    {
        TaskRetryDelay delay = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            delay.DelayAsync(TimeSpan.FromHours(1), cts.Token));
    }
}
