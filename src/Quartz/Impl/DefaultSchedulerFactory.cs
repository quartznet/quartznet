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
/// <c>InstantiateType&lt;T&gt;</c> seam for a container to patch. Whatever the container holds is what
/// the scheduler is built from.
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
                if (!existing.IsShutdown)
                {
                    return existing;
                }

                schedulerRepository.Remove(options.InstanceName);
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

        var plugins = SchedulerPluginFactory.Create(
            serviceProvider,
            serviceProvider.GetSchedulerServices<ISchedulerPlugin>(Key),
            properties,
            schedulerKey.OptionsName);

        foreach (var (_, plugin) in plugins)
        {
            resources.AddSchedulerPlugin(plugin);
        }

        if (options.GenerateInstanceId)
        {
            resources.InstanceId = await GenerateInstanceId(resources, cancellationToken).ConfigureAwait(false);

            // The job store was constructed before the id existed, and its rows are keyed by it, so it
            // has to be told the generated value rather than keeping the placeholder.
            if (resources.JobStore is JobStoreSupport persistentStore)
            {
                persistentStore.InstanceId = resources.InstanceId;
                persistentStore.InstanceName = resources.Name;
            }
        }

        // Unlike JobStoreSupport, RAMJobStore has no constructor-time access to QuartzSchedulerOptions, so
        // it never learns the configured instance id on its own — tell it now, whether that id came from
        // configuration or was just generated above.
        if (resources.JobStore is RAMJobStore ramJobStore)
        {
            ramJobStore.SchedulerInstanceId = resources.InstanceId;
        }

        var threadPool = resources.ThreadPool;
        await threadPool.Initialize(cancellationToken).ConfigureAwait(false);

        QuartzScheduler? quartzScheduler = null;
        try
        {
            quartzScheduler = serviceProvider.GetScheduler<QuartzScheduler>(Key);
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

            var scheduler = new StdScheduler(quartzScheduler);

            foreach (var pair in options.Context)
            {
                scheduler.Context[pair.Key] = pair.Value;
            }

            await resources.JobStore.Initialize(cancellationToken).ConfigureAwait(false);

            resources.JobRunShellFactory.Initialize(scheduler);

            foreach (var (name, plugin) in plugins)
            {
                await plugin.Initialize(name, scheduler, cancellationToken).ConfigureAwait(false);
            }

            // Listeners, calendars, jobs and triggers can only be applied once a scheduler exists.
            await serviceProvider.GetScheduler<SchedulerContentInitializer>(Key)
                .Initialize(scheduler, cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Quartz Scheduler {Version} - '{SchedulerName}' with instanceId '{SchedulerInstanceId}' initialized",
                quartzScheduler.Version, quartzScheduler.SchedulerName, quartzScheduler.SchedulerInstanceId);
            logger.LogInformation(
                "Using thread pool '{ThreadPoolType}', size: {ThreadPoolSize}",
                quartzScheduler.ThreadPoolType.FullName, quartzScheduler.ThreadPoolSize);
            logger.LogInformation(
                "Using job store '{JobStoreType}', supports persistence: {SupportsPersistence}, clustered: {Clustered}",
                quartzScheduler.JobStoreType.FullName, quartzScheduler.SupportsPersistence, quartzScheduler.Clustered);

            return scheduler;
        }
        catch
        {
            await ShutdownAfterFailure(quartzScheduler, threadPool).ConfigureAwait(false);
            throw;
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
            logger.LogError(e, "Couldn't generate instance id");
            Throw.SchedulerException("Cannot run without an instance id.", e);
            return default!;
        }
    }

    private async ValueTask ShutdownAfterFailure(QuartzScheduler? quartzScheduler, IThreadPool threadPool)
    {
        try
        {
            if (quartzScheduler is not null)
            {
                await quartzScheduler.Shutdown(waitForJobsToComplete: false).ConfigureAwait(false);
            }
            else
            {
                await threadPool.Shutdown(waitForJobsToComplete: false, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Got another exception while shutting down after instantiation exception");
        }
    }
}
