using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Configuration;

/// <summary>
/// Jobs and triggers a scheduler should carry, as contributed by one registration.
/// </summary>
/// <remarks>
/// <para>
/// A scheduler's jobs and triggers are content, not configuration, so they are registered as services
/// under the scheduler's own key like every other per-scheduler part. Before 4.0 they lived in
/// <see cref="QuartzOptions"/>, which meant the only thing in that type that was not a legacy string key
/// was also the only thing that was not configuration.
/// </para>
/// <para>
/// One registration is added per <c>AddJob</c>, <c>AddTrigger</c> or <c>ScheduleJob</c> call, and they are
/// applied in registration order. A single registration can carry both a job and a trigger, which is what
/// <c>ScheduleJob</c> needs: the job's key is derived from the trigger's, so the two have to be built
/// together rather than resolved independently.
/// </para>
/// </remarks>
internal interface ISchedulerContent
{
    /// <summary>
    /// The jobs this registration contributes.
    /// </summary>
    IReadOnlyList<IJobDetail> Jobs { get; }

    /// <summary>
    /// The triggers this registration contributes.
    /// </summary>
    IReadOnlyList<ITrigger> Triggers { get; }

    /// <summary>
    /// The key this registration generated, because it gave its job or trigger no identity; otherwise
    /// <see langword="null" />.
    /// </summary>
    /// <remarks>
    /// Recorded while the builder still knows no key was given, because a generated key cannot be told
    /// from a chosen one afterwards: a job may be named with a GUID on purpose.
    /// </remarks>
    GeneratedIdentity? GeneratedIdentity { get; }
}

/// <summary>
/// A key generated for a job or trigger whose registration gave it no identity.
/// </summary>
/// <param name="Registration">The call that registered it, naming the job type, or the job's key when the call names no type.</param>
/// <param name="Kind"><c>job</c> or <c>trigger</c>.</param>
/// <param name="Key">The generated key.</param>
internal sealed record GeneratedIdentity(string Registration, string Kind, string Key);

/// <inheritdoc />
internal sealed class SchedulerContent : ISchedulerContent
{
    private readonly List<IJobDetail> jobs = [];
    private readonly List<ITrigger> triggers = [];

    public IReadOnlyList<IJobDetail> Jobs => jobs;

    public IReadOnlyList<ITrigger> Triggers => triggers;

    public GeneratedIdentity? GeneratedIdentity { get; private set; }

    public SchedulerContent Add(IJobDetail job)
    {
        jobs.Add(job);
        return this;
    }

    public SchedulerContent Add(ITrigger trigger)
    {
        triggers.Add(trigger);
        return this;
    }

    /// <summary>
    /// Records that this registration's job or trigger was given no identity, so its key was generated.
    /// </summary>
    public SchedulerContent WithGeneratedIdentity(string registration, string kind, string key)
    {
        GeneratedIdentity = new GeneratedIdentity(registration, kind, key);
        return this;
    }

    /// <summary>
    /// Registers one contribution of jobs and triggers under a scheduler's key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed for a named scheduler and unkeyed for the default one, which is the convention every other
    /// per-scheduler registration follows. Without the key a named scheduler's jobs would be indexed by
    /// nothing and end up on the default scheduler.
    /// </para>
    /// <para>
    /// The factory runs when the content is resolved rather than when it is registered, so a job or
    /// trigger configured from services — a cron expression read out of configuration, say — still has a
    /// container to read from. A named scheduler's factory is handed that scheduler's own view of the
    /// container.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="schedulerName">
    /// The scheduler these jobs and triggers belong to. <see langword="null"/> or empty means the default
    /// scheduler.
    /// </param>
    /// <param name="factory">Builds the jobs and triggers, given the container.</param>
    public static void Register(
        IServiceCollection services,
        string? schedulerName,
        Func<IServiceProvider, ISchedulerContent> factory)
    {
        if (string.IsNullOrEmpty(schedulerName))
        {
            services.AddSingleton(factory);
        }
        else
        {
            services.AddKeyedSingleton<ISchedulerContent>(
                schedulerName,
                (provider, key) => factory(SchedulerScopedServiceProvider.For(provider, key)));
        }
    }
}
