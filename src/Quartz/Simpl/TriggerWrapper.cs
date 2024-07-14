using Quartz.Spi;

namespace Quartz.Simpl;

/// <summary>
/// Helper wrapper class
/// </summary>
internal sealed class TriggerWrapper : IEquatable<TriggerWrapper>
{
    public TriggerKey TriggerKey => Trigger.Key;

    public JobKey JobKey => Trigger.JobKey;

    /// <summary>
    /// The trigger
    /// </summary>
    public IOperableTrigger Trigger { get; }

    /// <summary>
    /// Current state
    /// </summary>
    public InternalTriggerState state = InternalTriggerState.Waiting;

    internal TriggerWrapper(IOperableTrigger trigger)
    {
        Trigger = trigger;
    }

    public bool Equals(TriggerWrapper? other)
    {
        return other is not null && other.TriggerKey.Equals(TriggerKey);
    }

    /// <summary>
    /// Determines whether the specified <see cref="System.Object"></see> is equal to the current <see cref="System.Object"></see>.
    /// </summary>
    /// <param name="obj">The <see cref="System.Object"></see> to compare with the current <see cref="System.Object"></see>.</param>
    /// <returns>
    /// true if the specified <see cref="System.Object"></see> is equal to the current <see cref="System.Object"></see>; otherwise, false.
    /// </returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as TriggerWrapper);
    }

    /// <summary>
    /// Serves as a hash function for a particular type. <see cref="System.Object.GetHashCode"></see> is suitable for use in hashing algorithms and data structures like a hash table.
    /// </summary>
    /// <returns>
    /// A hash code for the current <see cref="System.Object"></see>.
    /// </returns>
    public override int GetHashCode()
    {
        return TriggerKey.GetHashCode();
    }
}