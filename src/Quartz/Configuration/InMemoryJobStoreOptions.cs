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
}
