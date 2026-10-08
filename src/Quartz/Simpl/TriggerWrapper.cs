using System;

using Quartz.Spi;

namespace Quartz.Simpl;

/// <summary>
/// Helper wrapper class
/// </summary>
public class TriggerWrapper : IEquatable<TriggerWrapper>
{
    /// <summary>
    /// The key used
    /// </summary>
    public TriggerKey TriggerKey { get; }

    /// <summary>
    /// Job's key
    /// </summary>
    public JobKey JobKey { get; }

    /// <summary>
    /// The trigger
    /// </summary>
    /// <remarks>
    /// Replaced only by a fire that failed part-way, which puts back the copy it took before the
    /// trigger was advanced, and by a calendar's replacement, which puts in the copy it re-computed;
    /// the key, and so every index the wrapper is in, stays the same.
    /// </remarks>
    public IOperableTrigger Trigger { get; internal set; }

    /// <summary>
    /// Current state
    /// </summary>
    public InternalTriggerState state = InternalTriggerState.Waiting;

    internal TriggerWrapper(IOperableTrigger trigger)
    {
        Trigger = trigger;
        TriggerKey = trigger.Key;
        JobKey = trigger.JobKey;
    }

    public bool Equals(TriggerWrapper? other)
    {
        return other != null && other.TriggerKey.Equals(TriggerKey);
    }

    /// <summary>
    /// Determines whether the specified <see cref="T:System.Object"></see> is equal to the current <see cref="T:System.Object"></see>.
    /// </summary>
    /// <param name="obj">The <see cref="T:System.Object"></see> to compare with the current <see cref="T:System.Object"></see>.</param>
    /// <returns>
    /// true if the specified <see cref="T:System.Object"></see> is equal to the current <see cref="T:System.Object"></see>; otherwise, false.
    /// </returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as TriggerWrapper);
    }

    /// <summary>
    /// Serves as a hash function for a particular type. <see cref="M:System.Object.GetHashCode"></see> is suitable for use in hashing algorithms and data structures like a hash table.
    /// </summary>
    /// <returns>
    /// A hash code for the current <see cref="T:System.Object"></see>.
    /// </returns>
    public override int GetHashCode()
    {
        return TriggerKey.GetHashCode();
    }
}