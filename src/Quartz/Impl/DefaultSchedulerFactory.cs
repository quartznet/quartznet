using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Core;
using Quartz.Impl.AdoJobStore;
using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// Builds a scheduler by resolving its parts from the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the reflective construction the properties-based factory used to do: there is no type
/// loading from configuration strings, no property setting by reflection, and no
/// <c>InstantiateType&lt;T&gt;</c> seam for a container to patch — the name 3.x's
/// <c>StdSchedulerFactory</c> gave it. Whatever the container holds is what the scheduler is built from.
/// </para>
/// <para>
/// The graph itself is constructed by the container. What remains here is the ordering that
/// construction alone cannot express: work that must be asynchronous, and wiring that cannot happen
/// until a scheduler reference exists.
/// </para>
/// </remarks>
internal sealed class DefaultSchedulerFactory : ISchedulerFactory
{
    private readonly IServiceProvider serviceProvider;
    private readonly ISchedulerRepository schedulerRepository;
    private readonly ILogger<DefaultSchedulerFactory> logger;
    private readonly SchedulerKey schedulerKey;
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public DefaultSchedulerFactory(
        IServiceProvider serviceProvider,
        ISchedulerRepository schedulerRepository,
        ILogger<DefaultSchedulerFactory> logger,
        SchedulerKey schedulerKey)
    {
        this.serviceProvider = serviceProvider;
        this.schedulerRepository = schedulerRepository;
        this.logger = logger;
        this.schedulerKey = schedulerKey;
    }

    private object? Key => schedulerKey.Key;

    public ValueTask<List<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default)
    {
        return new ValueTask<List<IScheduler>>(schedulerRepository.LookupAll());
    }

    public async ValueTask<IScheduler?> LookupScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        // Asking for this factory's scheduler by name has to be able to create it. Looking straight in
        // the repository would only ever find a scheduler somebody else had already asked for. The
        // comparison ignores case because that is how the repository indexes names, so the create path
        // and the lookup path agree on what counts as the same scheduler.
        var options = serviceProvider.GetSchedulerOptions<QuartzSchedulerOptions>(Key);
        if (string.Equals(schedulerName, options.InstanceName, StringComparison.OrdinalIgnoreCase))
        {
            return await GetScheduler(cancellationToken).ConfigureAwait(false);
        }

        return schedulerRepository.Lookup(schedulerName);
    }

    public async ValueTask<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
    {
        // Options validation is how configuration is checked, but OptionsValidationException is an
        // implementation detail. Callers have always been told about bad configuration through
        // SchedulerException, so keep that contract. This wraps the options lookup as well as
        // construction, because resolving options is what triggers validation.
        try
        {
            return await GetOrCreate(cancellationToken).ConfigureAwait(false);
        }
        catch (OptionsValidationException e)
        {
            throw new SchedulerConfigException(string.Join(" ", e.Failures), e);
        }
    }

    private async ValueTask<IScheduler> GetOrCreate(CancellationToken cancellationToken)
    {
        var options = serviceProvider.GetSchedulerOptions<QuartzSchedulerOptions>(Key);

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = schedulerRepository.Lookup(options.InstanceName);
            if (existing is not null)
            {
                ThrowIfShutdown(existing.Status is SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown, options.InstanceName);
                return existing;
            }

            var scheduler = await Create(cancellationToken).ConfigureAwait(false);
            schedulerRepository.Bind(scheduler);
            return scheduler;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async ValueTask<IScheduler> Create(CancellationToken cancellationToken)
    {
        var options = serviceProvider.GetSchedulerOptions<QuartzSchedulerOptions>(Key);
        var resources = serviceProvider.GetScheduler<QuartzSchedulerResources>(Key);
        var properties = serviceProvider.GetSchedulerProperties(schedulerKey.OptionsName);

        // The scheduler is a keyed singleton, so this is the same instance a previous GetScheduler()
        // handed out — and a shut-down scheduler stays shut down. Asked for before anything is
        // initialized, because the initialization below would otherwise resurrect the thread pool and the
        // job store underneath a scheduler that can never run again.
        var quartzScheduler = serviceProvider.GetScheduler<QuartzScheduler>(Key);
        ThrowIfShutdown(quartzScheduler.Status is SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown, quartzScheduler.SchedulerName);

        var plugins = SchedulerPluginFactory.Create(
            serviceProvider,
            serviceProvider.GetSchedulerServices<ISchedulerPlugin>(Key),
            properties,
            schedulerKey);

        foreach (var (_, plugin) in plugins)
        {
            resources.AddSchedulerPlugin(plugin);
        }

        if (options.GenerateInstanceId)
        {
            resources.InstanceId = await GenerateInstanceId(resources, cancellationToken).ConfigureAwait(false);
        }

        await resources.ThreadPool.Initialize(cancellationToken).ConfigureAwait(false);

        try
        {
            quartzScheduler.JobFactory = serviceProvider.GetScheduler<IJobFactory>(Key);

            // Both code and quartz.executionLimit.* keys produce this registration, and the registration
            // is first-wins, so the precedence between them is settled before we get here. Falling back to
            // the resolved properties covers limits that only exist once the container is built — a key set
            // from a Configure<QuartzOptions> callback was not in the collection AddQuartz was handed, and
            // would otherwise be dropped without a word.
            var executionLimits = serviceProvider.GetSchedulerService<SchedulerExecutionLimits>(Key)?.Limits
                ?? ExecutionLimitsParser.Parse(properties);

            if (executionLimits is not null)
            {
                quartzScheduler.SetExecutionLimits(executionLimits);
            }

            // The scheduler's own facade, not a fresh one: it is what every listener callback and every
            // execution context is handed, so anything that compares the scheduler it was told about
            // with the one it holds must be comparing the same object.
            IScheduler scheduler = quartzScheduler.Scheduler;

            foreach (var pair in options.Context)
            {
                scheduler.Context[pair.Key] = pair.Value;
            }

            // The identity is handed over here rather than at construction because a generated instance
            // id does not exist until the generator above has run — and every store needs it, not only
            // the persistent one, since a firing is reported with the id of the node that owns it.
            SchedulerIdentity identity = new()
            {
                SchedulerName = resources.Name,
                InstanceId = resources.InstanceId
            };

            await resources.JobStore.Initialize(identity, cancellationToken).ConfigureAwait(false);

            // After the store has validated its own schema, so a missing table is reported as the error
            // it is before anything is said about how this scheduler's tables relate to a sibling's.
            serviceProvider.GetRequiredService<SharedDatabaseValidator>().Validate(resources.Name, resources.JobStore);

            resources.JobRunShellFactory.Initialize(scheduler);

            foreach (var (name, plugin) in plugins)
            {
                await plugin.Initialize(name, scheduler, cancellationToken).ConfigureAwait(false);
            }

            // Listeners, calendars, jobs and triggers can only be applied once a scheduler exists.
            await serviceProvider.GetScheduler<SchedulerContentInitializer>(Key)
                .Initialize(scheduler, cancellationToken)
                .ConfigureAwait(false);

            logger.SchedulerInitialized(
                quartzScheduler.Version, quartzScheduler.SchedulerName, quartzScheduler.SchedulerInstanceId);
            logger.UsingThreadPool(
                quartzScheduler.ThreadPoolType.FullName, quartzScheduler.ThreadPoolSize);
            logger.UsingJobStore(
                quartzScheduler.JobStoreType.FullName, quartzScheduler.SupportsPersistence, quartzScheduler.Clustered);

            return scheduler;
        }
        catch
        {
            await ShutdownAfterFailure(quartzScheduler).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Refuses to hand out a scheduler that has been shut down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 3.x built a fresh scheduler here, which it could: it constructed every part itself. The container
    /// owns those lifetimes now, and a scheduler's parts are keyed singletons, so "create it again" would
    /// re-initialize the very thread pool and job store the shutdown just tore down and hand back the same
    /// closed instance wearing a working scheduler's face. Saying so is the only honest answer.
    /// </para>
    /// <para>
    /// A scheduler that is only <see cref="SchedulerStatus.ShuttingDown" /> counts as shut down here: the
    /// shutdown is claimed and cannot be abandoned, so it refuses every call already, and handing it back
    /// hands back the same dead scheduler a moment earlier.
    /// </para>
    /// </remarks>
    private static void ThrowIfShutdown(bool isShutdown, string schedulerName)
    {
        if (isShutdown)
        {
            Throw.SchedulerException(
                $"Scheduler '{schedulerName}' has been shut down. A scheduler cannot be restarted within "
                + "the same service provider, because the container owns its components' lifetimes. Use "
                + "Standby()/Start() to pause and resume, or build a new host/container for a fresh scheduler.");
        }
    }

    private async ValueTask<string> GenerateInstanceId(QuartzSchedulerResources resources, CancellationToken cancellationToken)
    {
        try
        {
            // A non-clustered scheduler shares its database with nobody, so a generated id buys nothing.
            if (!resources.JobStore.Clustered)
            {
                return QuartzSchedulerOptions.DefaultInstanceId;
            }

            var generator = serviceProvider.GetScheduler<IInstanceIdGenerator>(Key);
            var instanceId = await generator.GenerateInstanceId(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                Throw.SchedulerException($"Instance id generator '{generator.GetType()}' produced an empty instance id.");
            }

            return instanceId!;
        }
        catch (Exception e) when (e is not SchedulerException)
        {
            logger.InstanceIdGenerationFailed(e);
            Throw.SchedulerException("Cannot run without an instance id.", e);
            return default!;
        }
    }

    private async ValueTask ShutdownAfterFailure(QuartzScheduler quartzScheduler)
    {
        try
        {
            // Shutting the scheduler down takes its thread pool and job store with it, and it is resolved
            // before anything is initialized, so there is no longer a window where only the pool is up.
            await quartzScheduler.Shutdown(waitForJobsToComplete: false).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.ShutdownAfterInstantiationFailureFailed(e);
        }
    }
}
