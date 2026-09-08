using System.Collections.Concurrent;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Configuration;

/// <summary>
/// The schedulers a container was asked for after it was built, and the answer to
/// <see cref="ISchedulerRegistry" /> for a container that has any.
/// </summary>
/// <remarks>
/// <para>
/// One object answers both interfaces because they are one question asked twice: a listing that omitted
/// the tenants this holds would be wrong, and a second registry that had to be merged with the first at
/// every call site is how it would come to be wrong. <see cref="ContainerSchedulerRegistry" /> still
/// does the container's half; this appends what the container never registered.
/// </para>
/// <para>
/// The unit it owns is a <see cref="SchedulerGeneration" /> — one scheduler's container and the
/// instances in it — plus the blueprint that built it. Operations on one name are serialised behind a
/// gate of that name, so two callers adding "acme" at once produce one scheduler and one refusal rather
/// than a race against the repository's own duplicate check, which would report the collision from the
/// wrong place.
/// </para>
/// </remarks>
internal sealed class SchedulerRuntime : ISchedulerRuntime, IAsyncDisposable, IDisposable
{
    private readonly IServiceProvider application;
    private readonly ISchedulerRepository repository;
    private readonly SchedulerNameRegistry names;
    private readonly ContainerSchedulerRegistry registrations;
    private readonly IOptionsMonitor<QuartzSchedulerOptions> schedulerOptions;
    private readonly ILogger<SchedulerRuntime> logger;
    private readonly IHostApplicationLifetime? applicationLifetime;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RuntimeUnit> units = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Generations whose schedulers somebody else is shutting down, waiting to have their containers
    /// released.
    /// </summary>
    private readonly ConcurrentQueue<SchedulerGeneration> drained = new();

    /// <summary>
    /// Set once the schedulers are being taken away — by the host stopping, or by this being disposed —
    /// after which nothing new is added.
    /// </summary>
    private int draining;

    private int disposed;

    /// <param name="application">
    /// The application's container, which every tenant resolves what its own recipe did not register
    /// from. Nothing is resolved out of it here: a container that holds this type must be buildable
    /// whether or not anything ever adds a scheduler.
    /// </param>
    /// <param name="repository">Where a tenant is bound, and where a name already taken is noticed.</param>
    /// <param name="names">What the container was registered with, which a runtime name may not collide with.</param>
    /// <param name="registrations">The container's half of the listing.</param>
    /// <param name="schedulerOptions">Read for the default scheduler's name, which is not a registered one.</param>
    /// <param name="logger">Where an add, a removal and a host-driven shutdown are recorded.</param>
    /// <param name="applicationLifetime">
    /// The host's lifetime, when there is a host. Optional because a container built by
    /// <see cref="QuartzSchedulerBuilder" /> has none, and a scheduler added there is still this
    /// object's to own.
    /// </param>
    public SchedulerRuntime(
        IServiceProvider application,
        ISchedulerRepository repository,
        SchedulerNameRegistry names,
        ContainerSchedulerRegistry registrations,
        IOptionsMonitor<QuartzSchedulerOptions> schedulerOptions,
        ILogger<SchedulerRuntime> logger,
        IHostApplicationLifetime? applicationLifetime = null)
    {
        this.application = application;
        this.repository = repository;
        this.names = names;
        this.registrations = registrations;
        this.schedulerOptions = schedulerOptions;
        this.logger = logger;
        this.applicationLifetime = applicationLifetime;
    }

    /// <inheritdoc />
    public async ValueTask<IScheduler> Add(
        string schedulerName,
        Action<IQuartzBuilder>? configure = null,
        SchedulerAddOptions options = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);

        SemaphoreSlim gate = Gate(schedulerName);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfTheNameIsNotAvailable(schedulerName);

            SchedulerGeneration generation;
            try
            {
                generation = SchedulerGeneration.Build(application, schedulerName, options, configure, number: 1);
            }
            catch (OptionsValidationException e)
            {
                // The same translation DefaultSchedulerFactory makes: options validation is how
                // configuration is checked, and OptionsValidationException is an implementation detail
                // of that rather than something a caller of this should have to catch.
                throw new SchedulerConfigException(string.Join(" ", e.Failures), e);
            }

            IScheduler scheduler;
            try
            {
                scheduler = await generation.Create(cancellationToken).ConfigureAwait(false);

                if (!options.CreateWithoutStarting)
                {
                    await scheduler.Start(cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                // Nothing is retained on any failure: a scheduler that was bound before the failure
                // unbinds itself when it is shut down, and the container goes with it. Half a tenant in
                // the listing is worse than none, because nothing would ever come back for it.
                await Discard(generation).ConfigureAwait(false);
                throw;
            }

            units[schedulerName] = new RuntimeUnit
            {
                Name = schedulerName,
                Options = options,
                Configure = configure,
                Generation = generation
            };

            logger.RuntimeSchedulerAdded(scheduler.SchedulerName, scheduler.SchedulerInstanceId);
            return scheduler;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> Remove(
        string schedulerName,
        bool waitForJobsToComplete = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);

        if (ContainerRegistration(schedulerName) is { } registered)
        {
            Throw.SchedulerConfigException(
                $"Scheduler '{registered}' is registered with the container, so it is not this runtime's to "
                + "remove: the container owns its parts and decides their lifetime. Shut it down with "
                + "IScheduler.Shutdown(), which unbinds it from the repository, or stop the host, which shuts "
                + "down every scheduler it started.");
        }

        SemaphoreSlim gate = Gate(schedulerName);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!units.TryRemove(schedulerName, out RuntimeUnit? unit))
            {
                return false;
            }

            try
            {
                if (unit.Generation.Scheduler is { } scheduler)
                {
                    await scheduler.Shutdown(waitForJobsToComplete, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                // Even when the shutdown threw: the container is this generation's, and leaking it would
                // leak the thread pool, the job store and its database connections along with it.
                await unit.Generation.DisposeAsync().ConfigureAwait(false);
            }

            logger.RuntimeSchedulerRemoved(unit.Name);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<List<SchedulerRegistration>> QuerySchedulers(CancellationToken cancellationToken = default)
    {
        List<SchedulerRegistration> listing = await registrations.QuerySchedulers(cancellationToken).ConfigureAwait(false);

        // A tenant that is running is already in the repository, so the container's half has reported it
        // as Runtime with its status. What is left to add is one that was shut down - by the host, or by
        // hand - and has not been removed: the repository drops it, and the registrations know nothing
        // about it, so without this it would vanish from the listing while Remove still had work to do.
        HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);
        foreach (SchedulerRegistration registration in listing)
        {
            reported.Add(registration.Name);
        }

        bool appended = false;
        foreach (RuntimeUnit unit in units.Values)
        {
            if (reported.Add(unit.Name))
            {
                listing.Add(new SchedulerRegistration(unit.Name, SchedulerOrigin.Runtime, Status: null));
                appended = true;
            }
        }

        if (appended)
        {
            listing.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        }

        return listing;
    }

    /// <summary>
    /// Hands the live schedulers to the host, which shuts them down inside its graceful shutdown window
    /// rather than leaving them for container disposal, which happens after it.
    /// </summary>
    /// <remarks>
    /// The units are taken rather than read, so a tenant is shut down once whoever gets to it first: the
    /// host through this, <see cref="Remove" /> through its gate, or <see cref="DisposeAsync" />. Their
    /// containers are kept back to be released when this is disposed, because the shutdown the caller is
    /// about to do has not happened yet.
    /// </remarks>
    internal List<IScheduler> TakeLiveSchedulers()
    {
        // Set first: an Add that has not yet taken its name's gate is refused from here on, rather than
        // creating a scheduler nothing will ever stop.
        Interlocked.Exchange(ref draining, 1);

        List<IScheduler> live = [];
        foreach (string name in units.Keys)
        {
            if (!units.TryRemove(name, out RuntimeUnit? unit))
            {
                continue;
            }

            drained.Enqueue(unit.Generation);

            if (unit.Generation.Scheduler is { } scheduler)
            {
                logger.RuntimeSchedulerShutDownByHost(unit.Name);
                live.Add(scheduler);
            }
        }

        return live;
    }

    /// <summary>
    /// Shuts down whatever is still running and releases every container this runtime built.
    /// </summary>
    /// <remarks>
    /// Reached when the container is disposed. A host that stopped first has already taken the
    /// schedulers through <see cref="TakeLiveSchedulers" /> and shut them down with the settings each
    /// was configured with, and what is left here is their containers. What is still running at this
    /// point is a scheduler in a container nobody stopped — a test, or an application that disposed its
    /// provider without stopping its host — and it is shut down without waiting, because there is
    /// nothing left to wait on behalf of.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        Interlocked.Exchange(ref draining, 1);

        List<Exception>? exceptions = null;

        try
        {
            foreach (string name in units.Keys)
            {
                if (!units.TryRemove(name, out RuntimeUnit? unit))
                {
                    continue;
                }

                drained.Enqueue(unit.Generation);

                if (unit.Generation.Scheduler is not { } scheduler)
                {
                    continue;
                }

                try
                {
                    await scheduler.Shutdown(waitForJobsToComplete: false).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    (exceptions ??= []).Add(e);
                }
            }
        }
        finally
        {
            while (drained.TryDequeue(out SchedulerGeneration? generation))
            {
                try
                {
                    await generation.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    (exceptions ??= []).Add(e);
                }
            }
        }

        if (exceptions is not null)
        {
            throw new AggregateException("One or more runtime scheduler shutdowns failed.", exceptions);
        }
    }

    /// <inheritdoc cref="DisposeAsync" />
    /// <remarks>
    /// <para>
    /// Present, and not as a formality: Microsoft's container refuses to dispose <em>at all</em>
    /// synchronously once it holds a service that is only <see cref="IAsyncDisposable" />, and
    /// <c>ServiceProvider.Dispose()</c> is still what a console application's <c>using var host</c>
    /// reaches. A registration nothing in the application ever asked for must not be the thing that
    /// turns its shutdown into an <see cref="InvalidOperationException" />.
    /// </para>
    /// <para>
    /// It waits for the asynchronous one, which is bounded: nothing there waits for a running job, and
    /// every await inside continues on the thread pool rather than on the caller's context, so there is
    /// no context for it to deadlock against.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Refuses every name this runtime must not take, saying which rule and what to do instead.
    /// </summary>
    /// <remarks>
    /// In this order because it is the order of how specifically the name is already spoken for: the
    /// container registered it, this runtime already holds it, something else is bound under it. A live
    /// tenant is both of the last two — it is in the repository because this runtime put it there — and
    /// "Remove it first" is the answer worth giving, so the runtime's own list is asked before the
    /// repository is. Letting the repository's duplicate check catch the collision instead would report
    /// it from the wrong place entirely: after a whole container had been built for a tenant that cannot
    /// exist.
    /// </remarks>
    private void ThrowIfTheNameIsNotAvailable(string schedulerName)
    {
        if (ContainerRegistration(schedulerName) is { } registered)
        {
            Throw.SchedulerConfigException(
                $"Scheduler '{registered}' is registered with the container, and a container registration "
                + "cannot be added again at runtime: its parts are registered under that name already, and a "
                + "second set of them would be two schedulers wearing one name. Build it with "
                + $"GetRequiredKeyedService<ISchedulerFactory>(\"{registered}\").GetScheduler(), or add this one "
                + "under a name of its own.");
        }

        if (units.ContainsKey(schedulerName))
        {
            Throw.SchedulerConfigException(
                $"A scheduler named '{schedulerName}' has already been added at runtime. Remove it first with "
                + $"ISchedulerRuntime.Remove(\"{schedulerName}\") and add it again: a scheduler's thread pool and "
                + "job store cannot be replaced underneath it, which is why there is no way to add over one.");
        }

        if (repository.Lookup(schedulerName) is not null)
        {
            Throw.SchedulerConfigException(
                $"A scheduler named '{schedulerName}' is already bound in this container's repository, so adding "
                + "one would be a second scheduler under one name. Shut down the one that is there first — a "
                + "scheduler bound by hand, or one AddQuartzHttpClient bound, is not this runtime's to remove.");
        }

        if (Volatile.Read(ref draining) == 1 || applicationLifetime?.ApplicationStopping.IsCancellationRequested == true)
        {
            Throw.SchedulerConfigException(
                $"The host is stopping, so scheduler '{schedulerName}' was not added: it would be created after "
                + "the shutdown that would have stopped it, and nothing would come back for it.");
        }
    }

    /// <summary>
    /// The container's own spelling of a name it registered, or <see langword="null" /> when it did not.
    /// </summary>
    /// <remarks>
    /// The default scheduler is the one registration whose name is not the name it was registered under —
    /// it has no service key at all — so its options are read to learn it, which is also what
    /// <see cref="ContainerSchedulerRegistry" /> does to list it.
    /// </remarks>
    private string? ContainerRegistration(string schedulerName)
    {
        if (names.Find(schedulerName) is { } registered)
        {
            return registered;
        }

        if (!names.HasDefaultScheduler)
        {
            return null;
        }

        string instanceName = schedulerOptions.Get(Options.DefaultName).InstanceName;
        return string.Equals(instanceName, schedulerName, StringComparison.OrdinalIgnoreCase) ? instanceName : null;
    }

    /// <summary>
    /// The gate serialising add and remove for one name.
    /// </summary>
    /// <remarks>
    /// Kept after a removal rather than dropped with the unit. A caller already waiting has the old gate
    /// in hand, so removing it from the map would let the next <see cref="Add" /> take a fresh one and
    /// run beside them — which is the exact race the gate exists to prevent, arriving only under the
    /// remove-then-add sequence this API is for. What is retained is one semaphore per name ever used,
    /// and a name space is bounded by the tenants an application has.
    /// </remarks>
    private SemaphoreSlim Gate(string schedulerName)
    {
        return gates.GetOrAdd(schedulerName, static _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Throws away a generation that failed on its way up, without letting its teardown hide what went
    /// wrong.
    /// </summary>
    private static async ValueTask Discard(SchedulerGeneration generation)
    {
        try
        {
            if (generation.Scheduler is { } scheduler)
            {
                await scheduler.Shutdown(waitForJobsToComplete: false).ConfigureAwait(false);
            }
        }
        catch
        {
            // The failure being unwound is what the caller needs to read. A scheduler that could not be
            // started is quite likely one that cannot be shut down cleanly either, and reporting the
            // second failure instead of the first would say nothing about the cause.
        }

        try
        {
            await generation.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // As above.
        }
    }

    /// <summary>
    /// One scheduler this runtime holds: what built it, and what it built.
    /// </summary>
    /// <remarks>
    /// The blueprint is kept rather than discarded after the generation exists, because a recipe that
    /// has been run and thrown away can never be run again — which is what every comparable system found
    /// out about restart, and what this leaves room for.
    /// </remarks>
    private sealed class RuntimeUnit
    {
        public required string Name { get; init; }

        public required SchedulerAddOptions Options { get; init; }

        public required Action<IQuartzBuilder>? Configure { get; init; }

        public required SchedulerGeneration Generation { get; init; }
    }
}
