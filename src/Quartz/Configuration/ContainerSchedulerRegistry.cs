using Microsoft.Extensions.Options;

using Quartz.Extensibility;

namespace Quartz.Configuration;

/// <summary>
/// Answers <see cref="ISchedulerRegistry" /> from what one container was told to register, joined with
/// what its repository currently holds.
/// </summary>
/// <remarks>
/// <para>
/// The two halves answer different questions and neither is enough on its own.
/// <see cref="SchedulerNameRegistry" /> is written while <c>AddQuartz</c> runs, so it knows every
/// registration whether or not anything ever resolved it — but it holds names, not schedulers.
/// <see cref="ISchedulerRepository" /> holds schedulers, but only the ones something already built, plus
/// any a caller bound by hand. Joining them by name is what turns "these exist" and "these are alive"
/// into one answer.
/// </para>
/// <para>
/// Nothing here creates a scheduler. That is the point: enumerating tenants must not start them.
/// </para>
/// </remarks>
internal sealed class ContainerSchedulerRegistry : ISchedulerRegistry
{
    private readonly SchedulerNameRegistry names;
    private readonly ISchedulerRepository repository;
    private readonly IOptionsMonitor<QuartzSchedulerOptions> schedulerOptions;

    public ContainerSchedulerRegistry(
        SchedulerNameRegistry names,
        ISchedulerRepository repository,
        IOptionsMonitor<QuartzSchedulerOptions> schedulerOptions)
    {
        this.names = names;
        this.repository = repository;
        this.schedulerOptions = schedulerOptions;
    }

    public ValueTask<List<SchedulerRegistration>> QuerySchedulers(CancellationToken cancellationToken = default)
    {
        // Names are matched the way the repository indexes them, so a registration and the scheduler
        // built from it are never reported as two schedulers because their spelling differs in case.
        Dictionary<string, IScheduler> live = new(StringComparer.OrdinalIgnoreCase);
        foreach (IScheduler scheduler in repository.LookupAll())
        {
            // A name can hold several entries - proxies to different nodes of one cluster - and they
            // are one scheduler as far as a registration is concerned. The first one answers for it.
            live.TryAdd(scheduler.SchedulerName, scheduler);
        }

        List<SchedulerRegistration> registrations = [];
        HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in RegisteredNames())
        {
            if (!reported.Add(name))
            {
                continue;
            }

            registrations.Add(new SchedulerRegistration(name, SchedulerOrigin.Container, StatusOf(live, name)));
        }

        foreach (IScheduler scheduler in live.Values)
        {
            if (!reported.Add(scheduler.SchedulerName))
            {
                continue;
            }

            registrations.Add(new SchedulerRegistration(
                scheduler.SchedulerName,
                SchedulerOrigin.Runtime,
                Status(scheduler)));
        }

        // Deterministic order, ordinal, as the paged queries over a job store are.
        registrations.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return new ValueTask<List<SchedulerRegistration>>(registrations);
    }

    /// <summary>
    /// The names <c>AddQuartz</c> registered, the default scheduler included.
    /// </summary>
    /// <remarks>
    /// The default scheduler is the one registration whose name is not the name it was registered
    /// under — it has no service key at all, and its name is whatever its options say. Reading them is
    /// therefore the only way to learn it, and it is also what validates them.
    /// </remarks>
    private IEnumerable<string> RegisteredNames()
    {
        if (names.HasDefaultScheduler)
        {
            yield return schedulerOptions.Get(Options.DefaultName).InstanceName;
        }

        foreach (string name in names.Names)
        {
            yield return name;
        }
    }

    private static SchedulerStatus? StatusOf(Dictionary<string, IScheduler> live, string name)
    {
        return live.TryGetValue(name, out IScheduler? scheduler) ? Status(scheduler) : null;
    }

    /// <summary>
    /// Asks a scheduler what state it is in, treating an unanswerable question as
    /// <see cref="SchedulerStatus.Unknown" />.
    /// </summary>
    /// <remarks>
    /// A local scheduler reads three fields. A remote one answers over the network and may simply be
    /// unreachable — and a listing of tenants is exactly the call that must not fail because one of them
    /// is. <see cref="SchedulerStatus.Unknown" /> already means "state could not be determined", so it is
    /// reported rather than the registration being dropped or the exception escaping.
    /// </remarks>
    private static SchedulerStatus Status(IScheduler scheduler)
    {
        try
        {
            return scheduler.GetStatus();
        }
        catch
        {
            return SchedulerStatus.Unknown;
        }
    }
}
