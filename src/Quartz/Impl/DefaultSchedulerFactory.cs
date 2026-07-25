using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Configuration;
using Quartz.Core;
using Quartz.Spi;

namespace Quartz.Impl;

/// <summary>
/// Builds a scheduler by resolving its parts from the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the reflective construction in <see cref="StdSchedulerFactory"/>: there is no type
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

    public ValueTask<IReadOnlyList<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default)
    {
        return new ValueTask<IReadOnlyList<IScheduler>>(schedulerRepository.LookupAll());
    }

    public ValueTask<IScheduler?> GetScheduler(string schedName, CancellationToken cancellationToken = default)
    {
        return new ValueTask<IScheduler?>(schedulerRepository.Lookup(schedName));
    }

    public async ValueTask<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
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

        if (options.GenerateInstanceId)
        {
            resources.InstanceId = await GenerateInstanceId(resources, cancellationToken).ConfigureAwait(false);
        }

        var threadPool = resources.ThreadPool;
        threadPool.InstanceName = resources.Name;
        threadPool.InstanceId = resources.InstanceId;
        threadPool.Initialize();

        QuartzScheduler? quartzScheduler = null;
        try
        {
            quartzScheduler = serviceProvider.GetScheduler<QuartzScheduler>(Key);
            quartzScheduler.JobFactory = serviceProvider.GetScheduler<IJobFactory>(Key);

            var scheduler = new StdScheduler(quartzScheduler);

            foreach (var pair in options.Context)
            {
                scheduler.Context[pair.Key] = pair.Value;
            }

            var jobStore = resources.JobStore;
            jobStore.InstanceName = resources.Name;
            jobStore.InstanceId = resources.InstanceId;
            jobStore.ThreadPoolSize = threadPool.PoolSize;
            jobStore.TimeProvider = resources.TimeProvider;
            await jobStore
                .Initialize(serviceProvider.GetRequiredService<ITypeLoadHelper>(), quartzScheduler.SchedulerSignaler, cancellationToken)
                .ConfigureAwait(false);

            resources.JobRunShellFactory.Initialize(scheduler);

            await InitializePlugins(resources, scheduler, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Quartz Scheduler {Version} - '{SchedulerName}' with instanceId '{SchedulerInstanceId}' initialized",
                quartzScheduler.Version, quartzScheduler.SchedulerName, quartzScheduler.SchedulerInstanceId);
            logger.LogInformation(
                "Using thread pool '{ThreadPoolType}', size: {ThreadPoolSize}",
                quartzScheduler.ThreadPoolClass.FullName, quartzScheduler.ThreadPoolSize);
            logger.LogInformation(
                "Using job store '{JobStoreType}', supports persistence: {SupportsPersistence}, clustered: {Clustered}",
                quartzScheduler.JobStoreClass.FullName, quartzScheduler.SupportsPersistence, quartzScheduler.Clustered);

            return scheduler;
        }
        catch
        {
            await ShutdownAfterFailure(quartzScheduler, threadPool).ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask InitializePlugins(
        QuartzSchedulerResources resources,
        IScheduler scheduler,
        CancellationToken cancellationToken)
    {
        foreach (var plugin in resources.SchedulerPlugins)
        {
            await plugin.Initialize(plugin.GetType().Name, scheduler, cancellationToken).ConfigureAwait(false);
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
                threadPool.Shutdown(waitForJobsToComplete: false);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Got another exception while shutting down after instantiation exception");
        }
    }
}
