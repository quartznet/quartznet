namespace Quartz;

/// <summary>
/// Strongly typed configuration for the in-memory job store.
/// </summary>
/// <remarks>
/// Binds from the <c>JobStore</c> section of the Quartz configuration when an in-memory store is in use.
/// </remarks>
public sealed class InMemoryJobStoreOptions
{
    /// <summary>
    /// How far past its scheduled fire time a trigger may be before it is considered misfired and its
    /// misfire instruction is applied.
    /// </summary>
    public TimeSpan MisfireThreshold { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How many fires of one trigger in a row may fail before the trigger is set to <c>ERROR</c>, or
    /// <c>0</c> to never set it <c>ERROR</c> for this. Defaults to 5.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A fire fails when the trigger's <see cref="ICalendar" />, or a trigger type of your own, throws
    /// while the store moves the trigger on. That trigger is left as it was and released, and the rest of
    /// its batch fires. The same throw while the trigger's misfire is handled counts too, and logs event
    /// 2010. One that fails every time is acquired again at once, ahead of its
    /// <see cref="DisallowConcurrentExecutionAttribute" /> job's other triggers. After this many failures
    /// in a row it is set to <c>ERROR</c>, the scheduler listeners hear
    /// <see cref="ISchedulerListener.TriggerInError" />, and event 2009 is logged.
    /// <see cref="IScheduler.ResetTriggerFromErrorState" /> brings it back once the cause is fixed.
    /// </para>
    /// <para>
    /// The same setting as <see cref="AdoJobStoreOptions.MaxConsecutiveFireFailures" />, under the same
    /// flat key, <c>quartz.jobStore.maxConsecutiveFireFailures</c>.
    /// </para>
    /// </remarks>
    public int MaxConsecutiveFireFailures { get; set; } = 5;
}
