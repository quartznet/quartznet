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
public sealed class QuartzOptions
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
    /// How the jobs, triggers and calendars a scheduler is configured with are applied to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are directives about applying a schedule rather than settings of a component, so they have
    /// no options type of their own to bind onto: <c>ContainerConfigurationProcessor</c> reads them from
    /// here, and the <c>Quartz:Scheduling</c> configuration section binds onto them. They stay on this
    /// type for that reason, while the settings that duplicated a typed option have gone.
    /// </para>
    /// <para>
    /// Get-only, like <see cref="Properties"/>. Options callbacks compose by running in order over one
    /// instance, and assignment is the one operation that is not additive: a callback assigning a fresh
    /// <see cref="SchedulingOptions"/> would silently discard whatever <c>Quartz:Scheduling</c> — or an
    /// earlier callback — had put there. The configuration binder binds into a non-null complex
    /// property without needing a setter, so the section keeps working unchanged.
    /// </para>
    /// </remarks>
    public SchedulingOptions Scheduling { get; } = new();

    /// <summary>
    /// Returns a snapshot of the flat keys, in the shape <c>UseProperties</c> and <c>AddQuartz</c> take.
    /// </summary>
    /// <remarks>
    /// <see cref="Properties"/> is the live bag this options instance goes on being configured through,
    /// so a caller that wants to hand one scheduler's keys to another takes a copy here rather than
    /// passing that one along. Every key is copied, including ones whose value is <see langword="null"/>
    /// or whitespace: deciding that an empty value means "not configured" belongs to the reader, and a
    /// converter that dropped keys of its own accord would make a key set to an empty string
    /// indistinguishable from one that was never given.
    /// </remarks>
    public Dictionary<string, string?> ToProperties()
    {
        return new Dictionary<string, string?>(Properties, StringComparer.Ordinal);
    }
}
