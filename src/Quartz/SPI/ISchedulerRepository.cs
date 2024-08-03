namespace Quartz.Spi;

/// <summary>
/// Holds references to Scheduler instances - ensuring uniqueness, and preventing garbage collection, and allowing 'global' lookups.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public interface ISchedulerRepository
{
    /// <summary>
    /// Binds scheduler to registry.
    /// </summary>
    void Bind(IScheduler schedulerName);

    /// <summary>
    /// Removes the specified scheduler from registry.
    /// </summary>
    void Remove(string schedulerName);

    /// <summary>
    /// Lookup a scheduler by name.
    /// </summary>
    IScheduler? Lookup(string schedulerName);

    /// <summary>
    /// Get all schedulers.
    /// </summary>
    /// <returns></returns>
    List<IScheduler> LookupAll();
}