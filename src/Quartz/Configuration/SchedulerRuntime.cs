using System.Collections.Concurrent;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;
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

    /// <summary>
    /// What a restart waits for the outgoing scheduler's jobs when the caller says nothing.
    /// </summary>
    private static readonly TimeSpan defaultDrainTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RuntimeUnit> units = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The container-registered schedulers this runtime has built a later generation of.
    /// </summary>
    /// <remarks>
    /// A dictionary of their own, so that <see cref="QuerySchedulers" /> keeps calling them
    /// <see cref="SchedulerOrigin.Container" />: they are the container's registrations, and a restart
    /// changes which instances answer for a name rather than where the name came from. Everything else
    /// treats the two kinds alike — one gate per name covers both, the host drains both, and disposal
    /// releases both.
    /// </remarks>
    private readonly ConcurrentDictionary<string, RuntimeUnit> containerUnits = new(StringComparer.OrdinalIgnoreCase);

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

                // Asked again, because building and starting a scheduler is not instantaneous and the
                // unit is not registered until the line below this block. A host that began stopping in
                // that window took the live schedulers from a map this one was not in yet, so the
                // shutdown that should have drained it has already been and gone — and a started, bound
                // scheduler would run past the graceful shutdown window with only container disposal
                // left to catch it. Refusing here undoes it while there is still something holding it.
                ThrowIfTheHostIsStopping(schedulerName);
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
                FromContainer = false,
                Live = generation,
                Number = generation.Number
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
                if (unit.Live?.Scheduler is { } scheduler)
                {
                    await scheduler.Shutdown(waitForJobsToComplete, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                // Even when the shutdown threw: the container is this generation's, and leaking it would
                // leak the thread pool, the job store and its database connections along with it. The
                // abandoned one is a generation a restart shut down but could not prove had finished, and
                // it is released here for the same reason - its jobs are somebody else's problem now, and
                // there is nothing left that would ever come back for its container.
                await Release(unit.Live).ConfigureAwait(false);
                await Release(unit.Abandoned?.Owned).ConfigureAwait(false);
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
    public async ValueTask<IScheduler> Restart(
        string schedulerName,
        SchedulerRestartOptions options = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);

        SemaphoreSlim gate = Gate(schedulerName);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RuntimeUnit unit = UnitToRestart(schedulerName);

            // Before anything is built or shut down: a restart that ran now would create its scheduler
            // after the shutdown that would have stopped it, and would have shut the running one down on
            // the way. Refusing here leaves the old scheduler for the host to drain.
            ThrowIfTheHostIsStopping(unit.Name, "restarted");

            TimeSpan drainTimeout = options.DrainTimeout ?? defaultDrainTimeout;
            using CancellationTokenSource drain = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (drainTimeout != Timeout.InfiniteTimeSpan)
            {
                drain.CancelAfter(drainTimeout);
            }

            // Read before the abandoned generation is let go of. An attempt whose drain gave up shut a
            // running scheduler down, and the attempt that finishes the job should leave the name the way
            // it found it rather than in standby because the first one already did the shutting down.
            bool abandonedWasRunning = unit.Abandoned?.WasRunning == true;

            await ThrowIfAnEarlierRestartIsStillFinishing(unit, drainTimeout, drain.Token, cancellationToken).ConfigureAwait(false);

            // May be null, and that is not a failure: the scheduler was shut down by hand, by the host,
            // or by a restart whose drain gave up, and there is simply nothing to stop before the next
            // generation is built. Restarting something that is already down is starting it again.
            Incumbent? live = Incumbent.Of(unit, application, repository);

            SchedulerGeneration next = BuildNext(unit);

            try
            {
                if (live is not null)
                {
                    ThrowIfTheRecipeSuppliesAnInstance(unit.Name, live, next);
                }
            }
            catch
            {
                await Discard(next).ConfigureAwait(false);
                throw;
            }

            bool wasRunning = live?.WasRunning ?? abandonedWasRunning;
            string? previousInstanceId = live?.Facade.SchedulerInstanceId;

            if (live is not null)
            {
                logger.SchedulerRestarting(unit.Name, previousInstanceId!, live.JobsExecuting);

                // Waiting is not optional. The next generation's first act is a recovery sweep over the
                // whole scheduler name - triggers out of acquired and blocked, every fired-trigger row
                // deleted - and it is unfiltered by instance id, so starting it beside a job the old
                // generation is still running would tear that job's bookkeeping out from under it.
                await live.Facade.Shutdown(waitForJobsToComplete: true, drain.Token).ConfigureAwait(false);

                if (live.Drained != true)
                {
                    // Kept rather than forgotten: its jobs are still running, so the next attempt has to
                    // wait for them before it may build anything, and the listing has to keep saying the
                    // name is there with nothing running under it.
                    unit.Live = null;
                    unit.Abandoned = live;
                    await Discard(next).ConfigureAwait(false);

                    throw Abandoned(unit.Name, live.JobsExecuting, drainTimeout);
                }

                await Release(live.Owned).ConfigureAwait(false);
                unit.Live = null;
            }

            IScheduler scheduler;
            try
            {
                // Here, after the drain, and nowhere earlier: this is what initializes the store, takes
                // the lock handler, runs the recovery sweep and applies the declared jobs and triggers.
                scheduler = await next.Create(cancellationToken).ConfigureAwait(false);

                if (options.Start ?? wasRunning)
                {
                    await scheduler.Start(cancellationToken).ConfigureAwait(false);
                }

                // Asked again for the reason Add asks again: building and starting is not instantaneous,
                // and a host that began stopping in that window has already taken the schedulers it was
                // going to drain.
                ThrowIfTheHostIsStopping(unit.Name, "restarted");
            }
            catch
            {
                await Discard(next).ConfigureAwait(false);
                throw;
            }

            unit.Live = next;
            unit.Number = next.Number;
            unit.Abandoned = null;

            logger.SchedulerRestarted(unit.Name, previousInstanceId ?? "none", scheduler.SchedulerInstanceId);
            return scheduler;
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
    /// <para>
    /// The units are taken rather than read, so a tenant is shut down once whoever gets to it first: the
    /// host through this, <see cref="Remove" /> through its gate, or <see cref="DisposeAsync" />. Their
    /// containers are kept back to be released when this is disposed, because the shutdown the caller is
    /// about to do has not happened yet.
    /// </para>
    /// <para>
    /// A restarted container-registered scheduler is handed over too, and it has to be: the hosted
    /// service resolved its schedulers when the host started, so what it holds under that name is the
    /// generation the restart shut down. Nothing else would stop the one that replaced it.
    /// </para>
    /// </remarks>
    internal List<IScheduler> TakeLiveSchedulers()
    {
        // Set first: an Add that has not yet taken its name's gate is refused from here on, rather than
        // creating a scheduler nothing will ever stop.
        Interlocked.Exchange(ref draining, 1);

        List<IScheduler> live = [];

        foreach (string name in units.Keys)
        {
            if (units.TryRemove(name, out RuntimeUnit? unit) && Take(unit) is { } scheduler)
            {
                logger.RuntimeSchedulerShutDownByHost(unit.Name);
                live.Add(scheduler);
            }
        }

        foreach (string name in containerUnits.Keys)
        {
            if (containerUnits.TryRemove(name, out RuntimeUnit? unit) && Take(unit) is { } scheduler)
            {
                live.Add(scheduler);
            }
        }

        return live;

        IScheduler? Take(RuntimeUnit unit)
        {
            Keep(unit.Live);
            Keep(unit.Abandoned?.Owned);
            return unit.Live?.Scheduler;
        }

        void Keep(SchedulerGeneration? generation)
        {
            if (generation is not null)
            {
                drained.Enqueue(generation);
            }
        }
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
            foreach (RuntimeUnit unit in Taken(units).Concat(Taken(containerUnits)))
            {
                Retain(unit.Live);
                Retain(unit.Abandoned?.Owned);

                if (unit.Live?.Scheduler is not { } scheduler)
                {
                    continue;
                }

                try
                {
                    // No token, said rather than defaulted. The host's own is the only one in reach and
                    // it is already cancelled by the time a container is being disposed, so passing it
                    // would abort the shutdown this exists to perform.
                    await scheduler.Shutdown(waitForJobsToComplete: false, CancellationToken.None).ConfigureAwait(false);
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

        List<RuntimeUnit> Taken(ConcurrentDictionary<string, RuntimeUnit> from)
        {
            List<RuntimeUnit> taken = [];
            foreach (string name in from.Keys)
            {
                if (from.TryRemove(name, out RuntimeUnit? unit))
                {
                    taken.Add(unit);
                }
            }

            return taken;
        }

        void Retain(SchedulerGeneration? generation)
        {
            if (generation is not null)
            {
                drained.Enqueue(generation);
            }
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

        ThrowIfTheHostIsStopping(schedulerName);
    }

    /// <summary>
    /// The unit a restart is about, creating one from the container's recipe the first time a registered
    /// scheduler is restarted, and refusing every name that has no recipe to replay.
    /// </summary>
    /// <remarks>
    /// The two dictionaries are asked before the registry is, because after one restart a
    /// container-registered scheduler <em>is</em> a generation this runtime holds, and its next restart
    /// has to be against that generation rather than against the recipe on its own.
    /// </remarks>
    private RuntimeUnit UnitToRestart(string schedulerName)
    {
        if (units.TryGetValue(schedulerName, out RuntimeUnit? added))
        {
            return added;
        }

        if (containerUnits.TryGetValue(schedulerName, out RuntimeUnit? restarted))
        {
            return restarted;
        }

        if (names.Blueprint(schedulerName) is { } blueprint)
        {
            RuntimeUnit unit = new()
            {
                Name = blueprint.Name,
                Options = Recipe(blueprint),
                Configure = blueprint.Configure,
                FromContainer = true,

                // Generation one is the container's own keyed graph rather than anything this runtime
                // built, so there is nothing to record but its number: what the restart shuts down is
                // read from the repository, and its parts stay the container's to dispose.
                Number = 1
            };

            return containerUnits.GetOrAdd(blueprint.Name, unit);
        }

        if (ContainerRegistration(schedulerName) is { } registered)
        {
            Throw.SchedulerConfigException(
                $"Scheduler '{registered}' is registered without a name; its parts are the container's unkeyed "
                + "registrations, so its recipe cannot be replayed — nothing can tell them apart from the "
                + $"application's own. Register it with AddQuartz(\"{registered}\", …) to make it restartable; "
                + "Standby()/Start() pause and resume it.");
        }

        throw new SchedulerNotFoundException(
            schedulerName,
            $"No scheduler named '{schedulerName}' is registered with this container or has been added at "
            + "runtime, so there is no recipe to build one from. ISchedulerRuntime.Add(\"" + schedulerName
            + "\", …) creates one.");
    }

    /// <summary>
    /// The recipe a container registration was made with, in the form a generation is built from.
    /// </summary>
    /// <remarks>
    /// One of the two, never both: a section says everything a property bag does, and
    /// <see cref="SchedulerGeneration.Build" /> refuses a recipe that sets both rather than choosing
    /// between them.
    /// </remarks>
    private static SchedulerAddOptions Recipe(SchedulerBlueprint blueprint)
    {
        if (blueprint.Configuration is not null)
        {
            return new SchedulerAddOptions { Configuration = blueprint.Configuration };
        }

        List<KeyValuePair<string, string?>> properties = [];
        foreach (string? key in blueprint.Properties.AllKeys)
        {
            if (key is not null)
            {
                properties.Add(new KeyValuePair<string, string?>(key, blueprint.Properties[key]));
            }
        }

        return new SchedulerAddOptions { Properties = properties };
    }

    /// <summary>
    /// Builds the next generation's container, translating a configuration failure the way
    /// <see cref="Add" /> does.
    /// </summary>
    /// <remarks>
    /// Before the old scheduler is touched, so a recipe that no longer builds — a connection string
    /// removed from configuration, a validator that has since been added — leaves the scheduler that is
    /// running exactly where it was. Construction is not initialization: nothing here opens a connection
    /// or starts a thread, which is what makes it safe to do beside a live generation.
    /// </remarks>
    private SchedulerGeneration BuildNext(RuntimeUnit unit)
    {
        try
        {
            return SchedulerGeneration.Build(application, unit.Name, unit.Options, unit.Configure, unit.Number + 1);
        }
        catch (OptionsValidationException e)
        {
            throw new SchedulerConfigException(string.Join(" ", e.Failures), e);
        }
    }

    /// <summary>
    /// Refuses a recipe that hands the next generation a part the last one is holding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A recipe made of <c>Use…&lt;T&gt;()</c> and factory registrations produces a new set of instances
    /// every time it is run. One that closes over an object — <c>UseJobStore(myStore)</c> — produces the
    /// same object, and that object is about to be shut down. Nothing in a service descriptor says which
    /// of the two a recipe is, so it is answered by building the next generation and comparing what came
    /// out by reference.
    /// </para>
    /// <para>
    /// Four parts, because these are the ones a recipe can be handed as an instance and that a scheduler
    /// cannot work without. Plugins and listeners are supplied as instances too, and are deliberately not
    /// checked: they are the application's to reason about, a plugin that tolerates a second
    /// <c>Initialize</c> is perfectly legal, and refusing every recipe with a listener object in it would
    /// leave almost nothing restartable.
    /// </para>
    /// </remarks>
    private static void ThrowIfTheRecipeSuppliesAnInstance(string schedulerName, Incumbent live, SchedulerGeneration next)
    {
        string? shared = Shared(live.Store, next.StoreInstance, "a job store", "UseJobStore(IJobStore)")
            ?? Shared(live.Pool, next.PoolInstance, "a thread pool", "UseThreadPool(IThreadPool)")
            ?? Shared(live.JobFactory, next.Part<IJobFactory>(), "a job factory", "UseJobFactory(instance)")
            ?? Shared(live.InstanceIdGenerator, next.Part<IInstanceIdGenerator>(), "an instance id generator", "UseInstanceIdGenerator(instance)");

        if (shared is null)
        {
            return;
        }

        Throw.SchedulerConfigException(
            $"The recipe for scheduler '{schedulerName}' supplies {shared}, so replaying it hands the new "
            + "scheduler the object the old one is about to shut down, and a shut-down instance cannot be "
            + "re-initialised. Register a type or a factory instead — the same recipe written as "
            + "UseJobStore<T>(), UseThreadPool<T>() or UseJobStore(provider => …) builds a new instance each "
            + $"time it runs. Scheduler '{schedulerName}' is still running and was not touched.");

        static string? Shared<T>(T? current, T? candidate, string what, string how) where T : class
        {
            return current is not null && ReferenceEquals(current, candidate) ? $"{what} as an instance ({how})" : null;
        }
    }

    /// <summary>
    /// Waits out the generation an earlier restart abandoned, and refuses this one if its work is still
    /// not finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pool is asked rather than the count of executing jobs, because a job leaves that count before
    /// its job store update is issued and it is exactly that write the next generation must not start
    /// beside. The built-in pool answers truthfully after a drain it gave up on — the completion the
    /// first wait watched is the one this watches — which is what makes a retry a plain second call
    /// rather than something that has to keep state of its own.
    /// </para>
    /// <para>
    /// Bounded twice over: by the deadline on the token, which a pool of ours honours, and by
    /// <see cref="TaskAsyncEnumerableExtensions" />' wait on the same token, because
    /// <see cref="IThreadPool.Drain" />'s default implementation ends in a wait that cannot be given up
    /// on. A pool that never overrode it would otherwise hang the retry rather than refuse it, and the
    /// caller would have no way to learn that it had.
    /// </para>
    /// </remarks>
    private async ValueTask ThrowIfAnEarlierRestartIsStillFinishing(
        RuntimeUnit unit,
        TimeSpan drainTimeout,
        CancellationToken drainToken,
        CancellationToken cancellationToken)
    {
        if (unit.Abandoned is not { } abandoned)
        {
            return;
        }

        bool finished;
        try
        {
            finished = await abandoned.Pool.Drain(drainToken).AsTask().WaitAsync(drainToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The deadline, not the caller. Reported as an answer for the same reason Drain reports it
            // as one: giving up on the wait is a fact about the old generation, not a failure of this
            // call, and the caller is the one that knows what to do about it.
            finished = false;
        }

        if (!finished)
        {
            throw Abandoned(unit.Name, abandoned.JobsExecuting, drainTimeout);
        }

        await Release(abandoned.Owned).ConfigureAwait(false);
        unit.Abandoned = null;
    }

    /// <summary>
    /// The report that a generation's work outlived the drain, logged and thrown as one.
    /// </summary>
    private SchedulerRestartException Abandoned(string schedulerName, int jobsStillExecuting, TimeSpan drainTimeout)
    {
        logger.SchedulerRestartAbandoned(schedulerName, jobsStillExecuting, drainTimeout);

        return new SchedulerRestartException(
            schedulerName,
            jobsStillExecuting,
            drainTimeout,
            $"Scheduler '{schedulerName}' was shut down, but its work had not finished when the {drainTimeout} "
            + $"drain gave up on it ({jobsStillExecuting} job(s) still executing), so no new scheduler was built. "
            + "The next generation's first act is a recovery sweep over the whole scheduler name — every "
            + "acquired and blocked trigger back to waiting, every fired-trigger row deleted — and it does not "
            + "filter by instance id, so running it now would tear that work's bookkeeping out from under it. "
            + $"Call Restart(\"{schedulerName}\") again once the work has finished; nothing else is needed, and "
            + "until then the name is listed with no status. ShutdownJobInterruption and [JobTimeout] are how a "
            + "job is made to stop.");
    }

    /// <summary>
    /// Releases a generation's container, if there is one to release.
    /// </summary>
    /// <remarks>
    /// There is not, for the generation a container registration built: its parts are keyed singletons in
    /// the application's container, disposed with it. That is the one asymmetry between restarting a
    /// registered scheduler and restarting one this runtime added, and it costs one dead object graph
    /// per registered scheduler ever restarted.
    /// </remarks>
    private static ValueTask Release(SchedulerGeneration? generation)
    {
        return generation?.DisposeAsync() ?? default;
    }

    /// <summary>
    /// Refuses a name while the schedulers are being taken away.
    /// </summary>
    /// <remarks>
    /// Asked twice: before the work starts, and again once the scheduler is running and about to be
    /// registered. The two readings answer different questions — "is it worth building this" and "is
    /// there still something that will shut it down" — and only the second one is about the scheduler
    /// that now exists.
    /// </remarks>
    private void ThrowIfTheHostIsStopping(string schedulerName, string verb = "added")
    {
        if (Volatile.Read(ref draining) == 1 || applicationLifetime?.ApplicationStopping.IsCancellationRequested == true)
        {
            Throw.SchedulerConfigException(
                $"The host is stopping, so scheduler '{schedulerName}' was not {verb}: it would be created after "
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
                // No token, said rather than defaulted, and deliberately not the caller's: a cancelled
                // add is one of the ways to arrive here, and unwinding it must not be cancelled by the
                // very token that caused it - that would leave the scheduler this is undoing running.
                await scheduler.Shutdown(waitForJobsToComplete: false, CancellationToken.None).ConfigureAwait(false);
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
    /// The recipe is kept rather than discarded after the generation exists, because a recipe that has
    /// been run and thrown away can never be run again — which is what every comparable system found out
    /// about restart, and what makes <see cref="Restart" /> a replay rather than a resurrection.
    /// </remarks>
    private sealed class RuntimeUnit
    {
        public required string Name { get; init; }

        public required SchedulerAddOptions Options { get; init; }

        public required Action<IQuartzBuilder>? Configure { get; init; }

        /// <summary>
        /// Whether the recipe came from a container registration rather than from a call to
        /// <see cref="Add" />.
        /// </summary>
        /// <remarks>
        /// It decides one thing: whether the scheduler bound in the repository under this name is this
        /// unit's first generation. For a registration it is, and shutting it down is what a restart is;
        /// for a name this runtime added, anything in the repository that is not a generation this
        /// runtime holds is somebody else's scheduler and not this one's to stop.
        /// </remarks>
        public required bool FromContainer { get; init; }

        /// <summary>
        /// Which generation of this name the runtime last built, counting the container's own as one.
        /// </summary>
        public required int Number { get; set; }

        /// <summary>
        /// The generation this runtime built and has not released, or <see langword="null" /> while the
        /// live scheduler is the container's own — or while there is none at all.
        /// </summary>
        /// <remarks>
        /// Null in three quite different situations, which is why nothing infers anything from it beyond
        /// "there is no container of ours to release": a container-registered scheduler before its first
        /// restart, one whose restart was abandoned, and one that has been shut down by hand.
        /// </remarks>
        public SchedulerGeneration? Live { get; set; }

        /// <summary>
        /// The generation a restart shut down but could not prove had finished its work.
        /// </summary>
        /// <remarks>
        /// Kept so the next attempt can wait for it rather than build over it, and released as soon as it
        /// answers that its work is done.
        /// </remarks>
        public Incumbent? Abandoned { get; set; }
    }

    /// <summary>
    /// The generation a restart is replacing, as the restart needs to see it.
    /// </summary>
    /// <remarks>
    /// One shape for two quite different things — a generation this runtime built, and the keyed graph a
    /// container registration produced — because a restart does the same five things to either: read its
    /// status, name its parts, shut it down, ask whether its work finished, and release what it owns.
    /// The last of those is the only place the two differ, and <see cref="Owned" /> is where the
    /// difference is written down.
    /// <para>
    /// <c>WasRunning</c> is captured rather than read on demand, because it is the question a shutdown
    /// destroys the answer to: after it, every scheduler reports <see cref="SchedulerStatus.Shutdown" />,
    /// and the next generation's starting policy defaults to what this one was doing.
    /// </para>
    /// </remarks>
    private sealed record Incumbent(
        IScheduler Facade,
        QuartzScheduler? Core,
        bool WasRunning,
        IJobStore Store,
        IThreadPool Pool,
        IJobFactory? JobFactory,
        IInstanceIdGenerator? InstanceIdGenerator,
        SchedulerGeneration? Owned)
    {
        /// <summary>
        /// How many jobs this generation is still running, or was still running when it was last asked.
        /// </summary>
        public int JobsExecuting => Core?.NumberOfJobsExecutingHere ?? 0;

        /// <summary>
        /// Whether the shutdown's wait for this generation's work succeeded, or <see langword="null" />
        /// when the answer cannot be read.
        /// </summary>
        /// <remarks>
        /// Unreadable only for a facade that is not this library's — a scheduler bound into the
        /// repository by hand, or a proxy to another process — and an unreadable answer is treated as a
        /// refusal, because the whole point of asking is to know before writing to a shared store.
        /// </remarks>
        public bool? Drained => Core?.RunningWorkDrained;

        /// <summary>
        /// The generation a restart of <paramref name="unit" /> would replace, or <see langword="null" />
        /// when nothing is running under that name.
        /// </summary>
        /// <remarks>
        /// The parts are resolved here, while the scheduler is still alive: they are keyed singletons and
        /// are therefore already constructed, and after the shutdown a runtime generation's container may
        /// well be gone.
        /// </remarks>
        public static Incumbent? Of(RuntimeUnit unit, IServiceProvider application, ISchedulerRepository repository)
        {
            if (unit.Live is { Scheduler: { } tenant } generation)
            {
                return new Incumbent(
                    tenant,
                    generation.QuartzScheduler,
                    tenant.Status == SchedulerStatus.Running,
                    generation.StoreInstance,
                    generation.PoolInstance,
                    generation.Part<IJobFactory>(),
                    generation.Part<IInstanceIdGenerator>(),
                    generation);
            }

            // The repository is asked only for a name the container registered, and only while this
            // runtime holds no generation of its own under it. Any other reading of it would be somebody
            // else's scheduler wearing the same name — one bound by hand, one AddQuartzHttpClient bound —
            // and this is not the place to notice that, nor is it something to shut down on the way past.
            if (unit.Live is not null || !unit.FromContainer || repository.Lookup(unit.Name) is not { } registered)
            {
                // A generation of ours that was never created, a registration nothing has built, or one
                // something has already shut down. All of them mean the same thing here: nothing to stop.
                return null;
            }

            // The container's own graph. Resolving it constructs nothing that is not already there, since
            // the scheduler the repository is holding was built out of it.
            QuartzSchedulerResources resources = application.GetScheduler<QuartzSchedulerResources>(unit.Name);

            return new Incumbent(
                registered,
                (registered as StdScheduler)?.scheduler,
                registered.Status == SchedulerStatus.Running,
                JobStores.Unwrap(resources.JobStore),
                resources.ThreadPool,
                application.GetSchedulerService<IJobFactory>(unit.Name),
                application.GetSchedulerService<IInstanceIdGenerator>(unit.Name),
                Owned: null);
        }
    }
}
