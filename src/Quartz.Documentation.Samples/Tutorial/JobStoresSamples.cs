using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/job-stores.md.
/// </summary>
public static class JobStoresSamples
{
    public static void InMemoryStore(IHostApplicationBuilder builder)
    {
        #region sample_job_stores_in_memory

        builder.Services.AddQuartz(q =>
        {
            // this is the default, so the call is only needed to change one of its settings
            q.UseInMemoryStore(options => options.MisfireThreshold = TimeSpan.FromSeconds(30));
        });

        #endregion
    }

    public static void PersistentStore(IHostApplicationBuilder builder)
    {
        #region sample_job_stores_persistent

        builder.Services.AddQuartz(q =>
        {
            q.UsePersistentStore(store =>
            {
                store.UseSqlServer("Server=localhost;Database=quartz;Trusted_Connection=True;Encrypt=False");
                store.UseSystemTextJsonSerializer();

                store.ConfigureStore(options =>
                {
                    options.TablePrefix = "QRTZ_";
                    options.StoreJobDataAsStrings = true;
                });
            });
        });

        #endregion
    }

    public static void ProvisionSchema(IHostApplicationBuilder builder, string connectionString)
    {
        #region sample_job_stores_provision_schema

        builder.Services.AddQuartz(q =>
        {
            q.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);
                store.UseSystemTextJsonSerializer();

                // outside production, where whatever applies the rest of the database's
                // schema applies this one too
                if (builder.Environment.IsDevelopment())
                {
                    store.ProvisionSchema();
                }
            });
        });

        #endregion
    }

    public static void StoreJobDataAsStrings(IPersistentStoreBuilder store)
    {
        #region sample_job_stores_store_job_data_as_strings

        store.ConfigureStore(options => options.StoreJobDataAsStrings = true);

        #endregion
    }

    public static void AcceptEnlistedTransactions(IHostApplicationBuilder builder, string connectionString)
    {
        #region sample_job_stores_accept_enlisted_transactions

        builder.Services.AddQuartz(q =>
        {
            q.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);
                store.ConfigureStore(options => options.AcceptEnlistedTransactions = true);
            });
        });

        #endregion
    }

    public static void JobStoreOfYourOwn(IHostApplicationBuilder builder)
    {
        #region sample_job_stores_registering_your_own

        builder.Services.AddQuartz(q =>
        {
            q.UsePersistentStore<LoggingJobStore>(options =>
            {
                // … store options
            });
        });

        #endregion
    }
}

#region sample_job_stores_delegating_store

public sealed class LoggingJobStore : DelegatingJobStore
{
    private readonly ILogger<LoggingJobStore> logger;

    public LoggingJobStore(
        ILoggerFactory loggerFactory,
        ISchedulerSignaler signaler,
        TimeProvider timeProvider,
        ILogger<LoggingJobStore> logger)
        : base(new RAMJobStore(loggerFactory, signaler, timeProvider))
    {
        this.logger = logger;
    }

    public override async ValueTask ScheduleJob(
        IJobDetail job,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        await base.ScheduleJob(job, trigger, cancellationToken);
        logger.LogInformation("Scheduled {JobKey} on {TriggerKey}", job.Key, trigger.Key);
    }
}

#endregion
