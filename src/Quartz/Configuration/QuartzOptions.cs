using System.Collections.Specialized;

using Quartz.Configuration;
using Quartz.Impl;

namespace Quartz;

/// <summary>
/// The flat <c>quartz.*</c> property keys a scheduler was configured with.
/// </summary>
/// <remarks>
/// <para>
/// This type used to <em>be</em> a <see cref="Dictionary{TKey,TValue}"/> and to double as the store for a
/// scheduler's jobs and triggers, because the flat string bag was the pivot the whole configuration model
/// turned on. It is not any more: settings bind onto typed options such as
/// <see cref="QuartzSchedulerOptions"/> and <see cref="ThreadPoolOptions"/>, and jobs and triggers are
/// registered per scheduler in the container, since they are a scheduler's content rather than its
/// configuration.
/// </para>
/// <para>
/// What is left is the legacy string format, held in <see cref="Properties"/>: keys that select an
/// implementation by type name, and keys that configure a component which has no options type of its own.
/// <c>QuartzPropertyBridge</c> is the only thing that reads them.
/// </para>
/// </remarks>
public class QuartzOptions
{
    /// <summary>
    /// The flat <c>quartz.*</c> keys, exactly as they were given.
    /// </summary>
    /// <remarks>
    /// Setting a key here is still the way to configure something the typed options do not cover — a
    /// third-party job store's own settings, for instance.
    /// </remarks>
    public Dictionary<string, string?> Properties { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>
    /// The scheduler's instance id, as <c>quartz.scheduler.instanceId</c>.
    /// </summary>
    public string? SchedulerId
    {
        get => Property(StdSchedulerFactory.PropertySchedulerInstanceId);
        set => Properties[StdSchedulerFactory.PropertySchedulerInstanceId] = value;
    }

    /// <summary>
    /// The scheduler's name, as <c>quartz.scheduler.instanceName</c>.
    /// </summary>
    /// <remarks>
    /// Up to 4.0 this read and wrote <c>schedulerName</c>, which is an ADO.NET column key that nothing
    /// reads — so a name set here was silently discarded.
    /// </remarks>
    public string? SchedulerName
    {
        get => Property(StdSchedulerFactory.PropertySchedulerInstanceName);
        set => Properties[StdSchedulerFactory.PropertySchedulerInstanceName] = value;
    }

    public TimeSpan? MisfireThreshold
    {
        get
        {
            var value = Property("quartz.jobStore.misfireThreshold");
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return TimeSpan.FromMilliseconds(int.Parse(value));
        }
        set => Properties["quartz.jobStore.misfireThreshold"] = value is not null ? ((int) value.Value.TotalMilliseconds).ToString() : "";
    }

    public SchedulingOptions Scheduling { get; set; } = new();

    /// <summary>
    /// Returns the flat keys in the form the property readers take.
    /// </summary>
    /// <remarks>
    /// Every key is copied, including ones whose value is <see langword="null"/> or whitespace. Deciding
    /// that an empty value means "not configured" belongs to the reader, which is where it happens; a
    /// converter that dropped keys of its own accord would make a key set to an empty string
    /// indistinguishable from one that was never given.
    /// </remarks>
    public NameValueCollection ToNameValueCollection()
    {
        var collection = new NameValueCollection(Properties.Count);
        foreach (var pair in Properties)
        {
            collection[pair.Key] = pair.Value;
        }

        return collection;
    }

    private string? Property(string key)
    {
        Properties.TryGetValue(key, out var value);
        return value;
    }
}
