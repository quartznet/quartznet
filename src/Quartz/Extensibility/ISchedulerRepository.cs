namespace Quartz.Extensibility;

/// <summary>
/// Holds references to Scheduler instances - ensuring uniqueness, and preventing garbage collection, and allowing 'global' lookups.
/// </summary>
/// <remarks>
/// Schedulers are indexed by name. Multiple schedulers with the same name but different instance IDs
/// can coexist (e.g., remote proxies to different cluster nodes). Pass an instance ID to
/// <see cref="Lookup"/> to disambiguate between them.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public interface ISchedulerRepository
{
    /// <summary>
    /// Binds a scheduler to the registry.
    /// </summary>
    /// <param name="scheduler">The scheduler to bind.</param>
    /// <param name="instanceId">
    /// The instance ID to index the scheduler under. When null, <see cref="IScheduler.SchedulerInstanceId"/>
    /// supplies it; pass it explicitly for a remote scheduler, where reading that property may cost a
    /// network call.
    /// </param>
    void Bind(IScheduler scheduler, string? instanceId = null);

    /// <summary>
    /// Removes a scheduler from the registry.
    /// </summary>
    /// <param name="schedulerName">The name of the scheduler to remove.</param>
    /// <param name="instanceId">
    /// The instance ID of the scheduler to remove. When null, the first scheduler registered under
    /// the name is removed.
    /// </param>
    /// <returns><see langword="true"/> if a scheduler was found and removed.</returns>
    bool Remove(string schedulerName, string? instanceId = null);

    /// <summary>
    /// Looks up a scheduler by name, and by instance ID when one is given.
    /// </summary>
    /// <param name="schedulerName">The name of the scheduler to look up.</param>
    /// <param name="instanceId">
    /// The instance ID to disambiguate by. When null, the first scheduler registered under the name
    /// is returned.
    /// </param>
    IScheduler? Lookup(string schedulerName, string? instanceId = null);

    /// <summary>
    /// Returns all schedulers with the given name.
    /// </summary>
    List<IScheduler> LookupByName(string schedulerName);

    /// <summary>
    /// Returns all registered schedulers.
    /// </summary>
    List<IScheduler> LookupAll();
}
