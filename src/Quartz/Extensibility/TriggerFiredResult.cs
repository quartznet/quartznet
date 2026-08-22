namespace Quartz.Extensibility;

/// <summary>
/// What became of one trigger in a <see cref="IJobStore.TriggersFired" /> batch.
/// </summary>
/// <remarks>
/// Exactly one of three things happened to each trigger the scheduler asked the store to fire, and
/// the three factories are the only way to say so: it fired and there is a bundle to run
/// (<see cref="Fired" />), it turned out not to be firable after all — paused, blocked, removed, or
/// the scheduler halted underneath it — and there is nothing to run and nothing wrong
/// (<see cref="NotFired" />), or firing it failed (<see cref="Failed" />). A result carrying both a
/// bundle and an exception, or claiming a failure with no exception, cannot be constructed.
/// </remarks>
public sealed class TriggerFiredResult
{
    private TriggerFiredResult(TriggerFiredBundle? triggerFiredBundle, Exception? exception)
    {
        TriggerFiredBundle = triggerFiredBundle;
        Exception = exception;
    }

    /// <summary>
    /// The trigger fired: <paramref name="triggerFiredBundle" /> is what the scheduler is to run.
    /// </summary>
    public static TriggerFiredResult Fired(TriggerFiredBundle triggerFiredBundle)
    {
        if (triggerFiredBundle is null)
        {
            Throw.ArgumentNullException(nameof(triggerFiredBundle));
        }

        return new TriggerFiredResult(triggerFiredBundle, exception: null);
    }

    /// <summary>
    /// The trigger did not fire, and nothing went wrong. The scheduler releases it and carries on.
    /// </summary>
    /// <remarks>
    /// This is the answer for a trigger that was paused, blocked by a
    /// <see cref="DisallowConcurrentExecutionAttribute" /> sibling, or removed between acquisition and
    /// firing, and for every trigger in a batch a halted scheduler never got to.
    /// </remarks>
    public static TriggerFiredResult NotFired { get; } = new(triggerFiredBundle: null, exception: null);

    /// <summary>
    /// Firing the trigger failed. The scheduler releases the trigger, reporting the exception.
    /// </summary>
    public static TriggerFiredResult Failed(Exception exception)
    {
        if (exception is null)
        {
            Throw.ArgumentNullException(nameof(exception));
        }

        return new TriggerFiredResult(triggerFiredBundle: null, exception);
    }

    /// <summary>
    /// What to run, when the trigger fired; <see langword="null" /> otherwise.
    /// </summary>
    public TriggerFiredBundle? TriggerFiredBundle { get; }

    /// <summary>
    /// Why firing failed; <see langword="null" /> when it did not.
    /// </summary>
    public Exception? Exception { get; }
}
