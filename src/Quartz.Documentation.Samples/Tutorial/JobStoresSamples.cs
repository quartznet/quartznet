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

    public static void SqliteFile(IHostApplicationBuilder builder)
    {
        #region sample_job_stores_sqlite_file

        builder.AddQuartz(q =>
        {
            q.UsePersistentStore(store =>
            {
                // a file beside the application; "Data Source=:memory:" would not survive a restart,
                // which is the whole point of a persistent store
                store.UseSqlite("Data Source=quartz.db");

                // let the store create the twelve tables on first start
                store.ProvisionSchema();

                store.ConfigureStore(options => options.StoreJobDataAsStrings = true);
            });

            q.ScheduleJob<HelloJob>(trigger => trigger
                .WithIdentity("helloTrigger")
                .StartNow()
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever()));
        });

        // ScheduleJob declares HelloJob and its trigger on every start, and by default a declaration
        // replaces what the store holds, StartNow() included. This keeps the stored trigger instead, so a
        // restart carries on from the file rather than scheduling afresh.
        builder.Services.Configure<QuartzOptions>(options => options.Scheduling.IgnoreDuplicates = true);

        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

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
        await base.ScheduleJob(job, trigger, cancellationToken: cancellationToken);
        logger.LogInformation("Scheduled {JobKey} on {TriggerKey}", job.Key, trigger.Key);
    }
}

#endregion
