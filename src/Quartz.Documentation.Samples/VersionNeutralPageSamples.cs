using Microsoft.Extensions.DependencyInjection;

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
}

/// <summary>Stands in for a job of your own in the <c>ScheduleJobs</c> sample.</summary>
public sealed class JobName : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class FaqJob : IJob
{
    #region sample_faq_value_task_execute

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // your job logic
    }

    #endregion
}
