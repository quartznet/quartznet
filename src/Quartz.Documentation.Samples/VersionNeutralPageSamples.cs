using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Listeners;

namespace Quartz.Documentation.Samples;

/// <summary>
/// Samples for the version-neutral pages: docs/documentation/best-practices.md,
/// docs/documentation/troubleshooting.md and docs/documentation/faq.md.
/// </summary>
/// <remarks>
/// Only the 4.x blocks on those pages are compiled from here. A block written against 3.x stays a
/// hand-written fence, because this project is 4.x and could not compile it.
/// </remarks>
public static class VersionNeutralPageSamples
{
    public static async ValueTask ScheduleJobsInOneCall(IScheduler scheduler, IReadOnlyCollection<int> allData)
    {
        #region sample_best_practices_schedule_jobs

        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> jobsDictionary = new();
        foreach (var data in allData)
        {
            var triggerSet = new HashSet<ITrigger>();
            IJobDetail job = JobBuilder.Create<JobName>()
                .UsingJobData("jobData", data.ToString())
                .Build();
            ITrigger trigger = TriggerBuilder.Create()
                .ForJob(job)
                .Build();
            triggerSet.Add(trigger);
            jobsDictionary.Add(job, triggerSet);
        }
        await scheduler.ScheduleJobs(jobsDictionary, new ScheduleJobOptions { Replace = true });

        #endregion
    }

    public static void PoolSize(IServiceCollection services, string connectionString)
    {
        #region sample_troubleshooting_pool_size

        services.AddQuartz(q =>
        {
            q.UsePersistentStore(s =>
            {
                s.UseSystemTextJsonSerializer();
                s.UseSqlServer(connectionString);
                // Ensure your connection string has an adequate pool size
                // e.g., "...;Max Pool Size=25;"
            });
        });

        #endregion
    }

    public static void WaitForJobsToComplete(IServiceCollection services)
    {
        #region sample_troubleshooting_wait_for_jobs

        services.AddQuartz(q =>
        {
            // configure jobs and triggers
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        #endregion
    }

    public static void WaitForJobsToCompleteWithABlock(IServiceCollection services)
    {
        #region sample_troubleshooting_wait_for_jobs_block

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        #endregion
    }

    public static void MisfireSweep(IServiceCollection services, string connectionString)
    {
        services.AddQuartz(q =>
        {
            #region sample_troubleshooting_misfire_sweep

            q.UsePersistentStore(s =>
            {
                s.UseSystemTextJsonSerializer();
                s.UseSqlServer(connectionString);

                s.ConfigureStore(options =>
                {
                    // A pass handles at most this many triggers, then commits. Lower it when the
                    // sweep is timing out; the loop comes straight back for the rest.
                    options.MaxMisfiresToHandleAtATime = 20;

                    // How often the sweep runs. Defaults to MisfireThreshold.
                    options.MisfireHandlerFrequency = TimeSpan.FromMinutes(1);

                    // Applied to every statement the store issues, this one included.
                    options.CommandTimeout = TimeSpan.FromSeconds(30);
                });
            });

            #endregion
        });
    }

    public static void RenamedJobTypesDeclared(IServiceCollection services)
    {
        #region sample_troubleshooting_type_loader_map

        services.AddQuartz(q => q.UseTypeLoader(loader =>
        {
            // Old assembly-qualified name as stored, and the type it means now. Keep the entry until
            // every row that could carry the old name has been rewritten or has aged out.
            loader.Map("Acme.Jobs.NightlyReport, Acme.Jobs", typeof(NightlyRollupJob));
        }));

        #endregion
    }

    public static void RenamedJobTypes(IServiceCollection services)
    {
        #region sample_troubleshooting_type_loader

        services.AddQuartz(q => q.UseTypeLoader<RenameAwareTypeLoader>());

        #endregion
    }

    public static void ChainingJobs(IScheduler scheduler)
    {
        #region sample_faq_job_chaining

        JobChainingJobListener chain = new("chain");
        chain.AddJobChainLink(new JobKey("extract"), new JobKey("transform"));
        chain.AddJobChainLink(new JobKey("transform"), new JobKey("load"));

        scheduler.ListenerManager.AddJobListener(chain);

        #endregion
    }

    public static void ChainingJobsToSeveralFollowUps(IScheduler scheduler)
    {
        #region sample_faq_job_chaining_fan_out

        JobChainingJobListener chain = new("chain");
        chain.AddJobChainLinks(new JobKey("transform"), [new JobKey("load-warehouse"), new JobKey("load-cache")]);
        chain.AddJobChainLink(new JobKey("transform"), new JobKey("notify"));

        scheduler.ListenerManager.AddJobListener(chain);

        #endregion
    }

    public static void RequestRecovery(IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            #region sample_best_practices_request_recovery

            q.AddJob<ChargeInvoicesJob>(j => j
                .WithIdentity("charge-invoices")
                .RequestRecovery());

            #endregion
        });
    }

    public static void MisfireInstructionByConsequence(IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            #region sample_best_practices_misfire_do_nothing

            q.AddTrigger<NightlyRollupJob>(t => t
                .WithIdentity("nightly-rollup")
                .WithCronSchedule("0 0 2 * * ?", x => x
                    .InTimeZone(TimeZones.FindById("Europe/Helsinki"))
                    .WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)));

            #endregion
        });
    }

    public static void FixedStartTime(IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            #region sample_best_practices_fixed_start_time

            q.AddTrigger<HourlySyncJob>(t => t
                .WithIdentity("hourly-sync")
                .StartAt(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
                .WithSimpleSchedule(s => s
                    .WithInterval(TimeSpan.FromHours(1))
                    .RepeatForever()));

            #endregion
        });
    }

    public static void CapTheHeavyGroup(IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            #region sample_best_practices_execution_limits

            q.AddTrigger<ReindexTenantJob>(t => t
                .WithIdentity("reindex-acme")
                .WithExecutionGroup("reindex")
                .WithCronSchedule("0 0 3 * * ?"));

            q.UseExecutionLimits(limits =>
            {
                limits.ForGroup("reindex", maxConcurrent: 2);
                limits.ForOtherGroups(maxConcurrent: 8);
            });

            #endregion
        });
    }

    public static void SizingThePools(IServiceCollection services, string connectionString)
    {
        #region sample_best_practices_pool_sizing

        services.AddQuartz(q =>
        {
            q.UseDefaultThreadPool(maxConcurrency: 20);

            q.UsePersistentStore(s =>
            {
                s.UseSystemTextJsonSerializer();
                s.UseClustering();

                // 20 workers, the scheduler thread, the misfire handler and the cluster manager
                s.UseSqlServer($"{connectionString};Max Pool Size=25");
            });
        });

        #endregion
    }

    public static void ShutdownBudget(IServiceCollection services)
    {
        #region sample_best_practices_shutdown

        // The scheduler's wait for running jobs is bounded by the host's shutdown budget,
        // which is 30 seconds unless you say otherwise.
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromMinutes(2));

        services.AddQuartz(q => q.ConfigureScheduler(options =>
            options.ShutdownJobInterruption = ShutdownJobInterruption.Always));

        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        #endregion
    }
}

/// <summary>Stands in for the application's own storage in the idempotence sample.</summary>
public interface IInvoiceLedger
{
    /// <summary>Charges the period's invoices unless <paramref name="idempotencyKey" /> has been seen before.</summary>
    ValueTask ChargeOnce(string idempotencyKey, string period, CancellationToken cancellationToken);
}

public sealed class ChargeInvoicesJob : IJob
{
    private readonly IInvoiceLedger ledger;

    public ChargeInvoicesJob(IInvoiceLedger ledger) => this.ledger = ledger;

    #region sample_best_practices_idempotent_job

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // The key names the occurrence, not the firing. A recovered execution arrives on a new trigger
        // with a new fire instance id, so a key derived from either of those would never match the
        // execution it is repeating.
        string period = context.MergedJobDataMap.GetString("period")!;
        string idempotencyKey = $"{context.JobDetail.Key}:{period}";

        // Recording the key and doing the work commit together, and a unique index on the key is what
        // settles a race between two executions rather than a read followed by a write.
        await ledger.ChargeOnce(idempotencyKey, period, cancellationToken);
    }

    #endregion
}

public sealed class RecoveryAwareJob : IJob
{
    private readonly ILogger<RecoveryAwareJob> logger;

    public RecoveryAwareJob(ILogger<RecoveryAwareJob> logger) => this.logger = logger;

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        #region sample_best_practices_recovering

        if (context.Recovering)
        {
            TriggerKey original = context.RecoveringTriggerKey!;
            string firstFiredAt = context.MergedJobDataMap.GetString(
                SchedulerConstants.FailedJobOriginalTriggerFireTime)!;

            logger.LogWarning(
                "Recovering work that {Trigger} started at {FirstFiredAt} on a node that did not finish it.",
                original, firstFiredAt);
        }

        #endregion

        return default;
    }
}

/// <summary>Stands in for whatever the job calls in the refire sample.</summary>
public interface IReportGateway
{
    ValueTask Publish(JobKey job, CancellationToken cancellationToken);
}

public sealed class PublishReportJob : IJob
{
    private readonly IReportGateway gateway;

    public PublishReportJob(IReportGateway gateway) => this.gateway = gateway;

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        #region sample_best_practices_bounded_refire

        try
        {
            await gateway.Publish(context.JobDetail.Key, cancellationToken);
        }
        catch (HttpRequestException ex) when (context.RefireCount < 3)
        {
            // A refire runs this firing again immediately, on the same worker, with no delay of its own.
            throw new JobExecutionException(ex) { RefireImmediately = true };
        }

        #endregion
    }
}

#region sample_best_practices_disallow_concurrent

[DisallowConcurrentExecution]
public sealed class RebuildSearchIndexJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // One execution of this job key at a time — across the whole cluster, with a persistent store.
        return default;
    }
}

#endregion

/// <summary>Stands in for a job of your own in the misfire sample.</summary>
public sealed class NightlyRollupJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

/// <summary>Stands in for one of many reindexing jobs in the execution-limits sample.</summary>
public sealed class ReindexTenantJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

/// <summary>Stands in for a job of your own in the start-time sample.</summary>
public sealed class HourlySyncJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

/// <summary>Stands in for a job of your own in the <c>ScheduleJobs</c> sample.</summary>
public sealed class JobName : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

#region sample_troubleshooting_type_loader_implementation

/// <summary>
/// Resolves the type names stored in JOB_CLASS_NAME, translating the ones that have since moved.
/// </summary>
public sealed class RenameAwareTypeLoader : ITypeLoader
{
    // Old assembly-qualified name as stored, new type. Keep an entry until every row that could
    // carry the old name has been rewritten or has aged out.
    private static readonly Dictionary<string, Type> renamed = new(StringComparer.Ordinal)
    {
        ["Acme.Jobs.NightlyReport, Acme.Jobs"] = typeof(NightlyRollupJob)
    };

    public Type? LoadType(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (renamed.TryGetValue(name, out Type? moved))
        {
            return moved;
        }

        // A name that cannot be resolved must throw rather than return null: Quartz only asks when
        // it already knows a type is required.
        return Type.GetType(name, throwOnError: true);
    }
}

#endregion

public sealed class FaqJob : IJob
{
    #region sample_faq_value_task_execute

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // your job logic
    }

    #endregion
}
