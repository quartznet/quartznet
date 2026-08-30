using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/job-data-map.md.
/// </summary>
/// <remarks>
/// The page shows two different jobs called <c>ReportJob</c> — one reading the merged map, one having
/// its properties injected — so each lives in a nested class of its own.
/// </remarks>
public static class JobDataMapSamples
{
    public sealed class ReportOptions;

    public static class ReadingTheMergedMap
    {
        #region sample_job_data_map_merged_map

        public sealed class ReportJob : IJob
        {
            public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                JobDataMap data = context.MergedJobDataMap;
                string region = data.GetString("region")!;
                int lookbackDays = data.GetInt("lookbackDays");
                // ...

                return default;
            }
        }

        #endregion
    }

    public static class PropertyInjection
    {
        #region sample_job_data_map_property_injection

        public sealed class ReportJob : IJob
        {
            public string Region { get; set; } = "";
            public int LookbackDays { get; set; }

            public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                // Region and LookbackDays are already set

                return default;
            }
        }

        #endregion

        public static void PuttingValuesIn(JobDataMap existingMap)
        {
            #region sample_job_data_map_using_job_data

            IJobDetail job = JobBuilder.Create<ReportJob>()
                .WithIdentity("nightly", "reports")
                .UsingJobData("region", "emea")                     // key and value
                .UsingJobData(j => j.LookbackDays, 30)              // name the property, not the key
                .UsingJobData(existingMap)                          // merge a whole map in
                .Build();

            #endregion
        }
    }

    public static async ValueTask DataForASingleFiring(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken)
    {
        #region sample_job_data_map_trigger_job_with_data

        await scheduler.TriggerJob(jobKey, new JobDataMap { ["reason"] = "manual re-run" }, cancellationToken);

        #endregion
    }

    public static void TheGenericReaders(JobDataMap data)
    {
        #region sample_job_data_map_generic_readers

        // False when the entry is missing and when it holds something else.
        if (data.TryGet<ReportOptions>("options", out ReportOptions? options))
        {
            // ...
        }

        // Throws KeyNotFoundException for a missing entry, InvalidCastException for a wrong one -
        // the two mistakes told apart, where TryGet answers false to both.
        ReportOptions required = data.Get<ReportOptions>("options");

        // Neither throws nor distinguishes: missing and wrong-typed both give the fallback.
        ReportOptions effective = data.GetValueOrDefault("options", new ReportOptions());

        #endregion
    }

    public static void PuttingValuesInAsStrings()
    {
        #region sample_job_data_map_put_as_string

        JobDataMap data = new();
        data.PutAsString("runAt", DateTimeOffset.UtcNow);   // "O": 2026-08-22T09:15:00.0000000+00:00
        data.PutAsString("window", TimeSpan.FromHours(6));  // invariant "06:00:00"
        data.PutAsString("batchId", Guid.NewGuid());
        data.PutAsString("lookbackDays", 30);               // any IFormattable

        #endregion
    }

    public static void StringMode(IServiceCollection services, string connectionString)
    {
        services.AddQuartz(q =>
        {
            #region sample_job_data_map_store_as_strings

            q.UsePersistentStore(s =>
            {
                s.UseSqlServer(connectionString);
                s.ConfigureStore(o => o.StoreJobDataAsStrings = true);
            });

            #endregion
        });
    }

    public static void ForcingAWrite(JobDataMap data)
    {
        #region sample_job_data_map_force_dirty

        data[SchedulerConstants.ForceJobDataMapDirty] = "true";

        #endregion
    }
}

#region sample_job_data_map_typed_input

public sealed record SendEmail(string To, string Subject);

public sealed class SendEmailJob : IJob<SendEmail>
{
    public ValueTask Execute(IJobExecutionContext context, SendEmail input, CancellationToken cancellationToken = default)
    {
        // input.To, input.Subject - no keys, no accessors, no casts
        return default;
    }
}

public static class TypedInputScheduling
{
    public static async ValueTask Schedule(IScheduler scheduler, CancellationToken cancellationToken)
    {
        await scheduler.ScheduleJob(
            JobBuilder.Create<SendEmailJob>()
                .WithIdentity("welcome", "email")
                .Build(),
            TriggerBuilder.Create<SendEmailJob>()
                .WithIdentity("welcome-3401", "email")
                .StartNow()
                .UsingInput(new SendEmail("someone@example.org", "Welcome"))
                .Build(),
            cancellationToken);
    }
}

#endregion

#region sample_job_data_map_persist_across_fires

[PersistJobDataAfterExecution]
[DisallowConcurrentExecution]
public sealed class IncrementalSyncJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobDataMap data = context.JobDetail.JobDataMap;
        data.PutAsString("lastSyncedAt", DateTimeOffset.UtcNow);
        return default;
    }
}

#endregion
