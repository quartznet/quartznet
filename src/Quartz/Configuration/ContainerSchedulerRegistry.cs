using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Impl;

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

    /// <summary>
    /// How long the whole listing may spend asking schedulers what state they are in.
    /// </summary>
    /// <remarks>
    /// One budget for the query rather than one per scheduler: a listing is a page render, and a
    /// process fronting five unreachable targets must not take five times as long to say so. A local
    /// scheduler spends none of it — its default interface members answer the properties, which are
    /// fields.
    /// </remarks>
    internal static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(2);

    public async ValueTask<List<SchedulerRegistration>> QuerySchedulers(CancellationToken cancellationToken = default)
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

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(StatusTimeout);

        List<SchedulerRegistration> registrations = [];
        HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in RegisteredNames())
        {
            if (!reported.Add(name))
            {
                continue;
            }

            LiveState state = live.TryGetValue(name, out IScheduler? registered)
                ? await Ask(registered, deadline.Token, cancellationToken).ConfigureAwait(false)
                : default;

            registrations.Add(new SchedulerRegistration(name, SchedulerOrigin.Container, state.Status)
            {
                SchedulerInstanceId = state.SchedulerInstanceId
            });
        }

        foreach (IScheduler scheduler in live.Values)
        {
            if (!reported.Add(scheduler.SchedulerName))
            {
                continue;
            }

            LiveState state = await Ask(scheduler, deadline.Token, cancellationToken).ConfigureAwait(false);

            registrations.Add(new SchedulerRegistration(
                scheduler.SchedulerName,
                scheduler is IProxyScheduler ? SchedulerOrigin.Remote : SchedulerOrigin.Runtime,
                state.Status)
            {
                SchedulerInstanceId = state.SchedulerInstanceId
            });
        }

        // Deterministic order, ordinal, as the paged queries over a job store are.
        registrations.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return registrations;
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

    /// <summary>
    /// Asks a scheduler what state it is in and which node it is, treating an unanswerable question as
    /// <see cref="SchedulerStatus.Unknown" /> and no id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked through <see cref="IScheduler.GetStatus" /> and
    /// <see cref="IScheduler.GetSchedulerInstanceId" /> rather than through the properties those default
    /// interface members answer: a local scheduler pays nothing either way, and for a scheduler in
    /// another process the properties can only do it by blocking the calling thread until the client's
    /// timeout — 100 seconds by default — expires. This is a listing, and a listing runs on a request
    /// thread.
    /// </para>
    /// <para>
    /// A listing of tenants is exactly the call that must not fail because one of them is unreachable.
    /// <see cref="SchedulerStatus.Unknown" /> already means "state could not be determined", so it is
    /// reported rather than the registration being dropped or the exception escaping. The caller's own
    /// cancellation is not swallowed: only the deadline's is.
    /// </para>
    /// </remarks>
    private static async ValueTask<LiveState> Ask(
        IScheduler scheduler,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        try
        {
            SchedulerStatus status = await scheduler.GetStatus(deadline).ConfigureAwait(false);
            string instanceId = await scheduler.GetSchedulerInstanceId(deadline).ConfigureAwait(false);
            return new LiveState(status, instanceId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LiveState(SchedulerStatus.Unknown, SchedulerInstanceId: null);
        }
        catch (SchedulerException)
        {
            return new LiveState(SchedulerStatus.Unknown, SchedulerInstanceId: null);
        }
        catch (HttpRequestException)
        {
            return new LiveState(SchedulerStatus.Unknown, SchedulerInstanceId: null);
        }
    }

    /// <summary>
    /// What one live scheduler answered, or nothing at all when there is no scheduler to ask.
    /// </summary>
    private readonly record struct LiveState(SchedulerStatus? Status, string? SchedulerInstanceId);
}
