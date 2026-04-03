namespace Quartz.Spi;

/// <summary>
/// Holds references to Scheduler instances - ensuring uniqueness, and preventing garbage collection, and allowing 'global' lookups.
/// </summary>
/// <remarks>
/// Schedulers are indexed by name. Multiple schedulers with the same name but different instance IDs
/// can coexist (e.g., remote proxies to different cluster nodes). Use <see cref="Lookup(string, string)"/>
/// to disambiguate by instance ID.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public interface ISchedulerRepository
{
    /// <summary>
    /// Binds scheduler to registry using its <see cref="IScheduler.SchedulerInstanceId"/> as the instance key.
    /// For remote schedulers where <see cref="IScheduler.SchedulerInstanceId"/> may require a network call,
    /// use <see cref="Bind(IScheduler, string)"/> with an explicit instance ID instead.
    /// </summary>
    void Bind(IScheduler scheduler);

    /// <summary>
    /// Binds scheduler to registry with an explicit instance ID, avoiding remote calls
    /// to resolve <see cref="IScheduler.SchedulerInstanceId"/>.
    /// </summary>
    void Bind(IScheduler scheduler, string instanceId);

    /// <summary>
    /// Removes the first scheduler with the given name.
    /// </summary>
    void Remove(string schedulerName);

    /// <summary>
    /// Removes a specific scheduler by name and instance ID.
    /// </summary>
    /// <returns><see langword="true"/> if the scheduler was found and removed.</returns>
    bool Remove(string schedulerName, string instanceId);

    /// <summary>
    /// Looks up the first scheduler with the given name.
    /// </summary>
    IScheduler? Lookup(string schedulerName);

    /// <summary>
    /// Looks up a scheduler by name and instance ID.
    /// </summary>
    IScheduler? Lookup(string schedulerName, string instanceId);

    /// <summary>
    /// Returns all schedulers with the given name.
    /// </summary>
    List<IScheduler> LookupByName(string schedulerName);

    /// <summary>
    /// Returns all registered schedulers.
    /// </summary>
    List<IScheduler> LookupAll();
}
