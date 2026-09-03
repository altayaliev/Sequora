using Microsoft.Extensions.Options;

namespace Sequora.Internal;

internal sealed class SequoraOptionsValidator : IValidateOptions<SequoraOptions>
{
    public ValidateOptionsResult Validate(string? name, SequoraOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? errors = null;

        if (options.WorkerCount < 1)
        {
            Add(ref errors, $"{nameof(SequoraOptions.WorkerCount)} must be at least 1. Received {options.WorkerCount}.");
        }

        if (options.Capacity != SequoraOptions.Unbounded && options.Capacity < 1)
        {
            Add(ref errors, $"{nameof(SequoraOptions.Capacity)} must be {nameof(SequoraOptions.Unbounded)} ({SequoraOptions.Unbounded}) or at least 1. Received {options.Capacity}.");
        }

        if (options.RetryCount < 0)
        {
            Add(ref errors, $"{nameof(SequoraOptions.RetryCount)} must be greater than or equal to 0. Received {options.RetryCount}.");
        }

        if (options.RetryDelay < TimeSpan.Zero)
        {
            Add(ref errors, $"{nameof(SequoraOptions.RetryDelay)} must be greater than or equal to {nameof(TimeSpan.Zero)}. Received {options.RetryDelay}.");
        }

        if (options.MaxRetryDelay < TimeSpan.Zero)
        {
            Add(ref errors, $"{nameof(SequoraOptions.MaxRetryDelay)} must be greater than or equal to {nameof(TimeSpan.Zero)}. Received {options.MaxRetryDelay}.");
        }

        if (!Enum.IsDefined(options.RetryBackoff))
        {
            Add(ref errors, $"{nameof(SequoraOptions.RetryBackoff)} value '{options.RetryBackoff}' is not defined.");
        }

        if (!Enum.IsDefined(options.QueueFullBehavior))
        {
            Add(ref errors, $"{nameof(SequoraOptions.QueueFullBehavior)} value '{options.QueueFullBehavior}' is not defined.");
        }

        if (!Enum.IsDefined(options.ShutdownBehavior))
        {
            Add(ref errors, $"{nameof(SequoraOptions.ShutdownBehavior)} value '{options.ShutdownBehavior}' is not defined.");
        }

        if (options.PriorityFairnessLimit < 0)
        {
            Add(ref errors, $"{nameof(SequoraOptions.PriorityFairnessLimit)} must be greater than or equal to 0. Received {options.PriorityFairnessLimit}.");
        }

        return errors is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void Add(ref List<string>? errors, string message)
    {
        errors ??= [];
        errors.Add(message);
    }
}
