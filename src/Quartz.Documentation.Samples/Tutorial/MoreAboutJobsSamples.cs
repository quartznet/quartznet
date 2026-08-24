using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/more-about-jobs.md.
/// </summary>
/// <remarks>
/// The page shows four different jobs all called <c>DumbJob</c>, so each lives in a nested class of
/// its own — the region never includes the enclosing declaration, so the page shows only the job.
/// </remarks>
public static class MoreAboutJobsSamples
{
    public static class Basics
    {
        public static async ValueTask SchedulingAJob(IScheduler scheduler)
        {
            #region sample_more_about_jobs_scheduling

            // define the job and tie it to our HelloJob class
            IJobDetail job = JobBuilder.Create<HelloJob>()
                .WithIdentity("myJob", "group1")
                .Build();

            // Trigger the job to run now, and then every 40 seconds
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("myTrigger", "group1")
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithInterval(TimeSpan.FromSeconds(40))
                    .RepeatForever())
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            #endregion
        }

        #region sample_more_about_jobs_hello_job

        public class HelloJob : IJob
        {
            public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                await Console.Out.WriteLineAsync("HelloJob is executing.");
            }
        }

        #endregion
    }

    public static class JobDataByKey
    {
        public static void SettingValues()
        {
            #region sample_more_about_jobs_setting_job_data

            // define the job and tie it to our DumbJob class
            IJobDetail job = JobBuilder.Create<DumbJob>()
                .WithIdentity("myJob", "group1") // name "myJob", group "group1"
                .UsingJobData("jobSays", "Hello World!")
                .UsingJobData("myFloatValue", 3.141f)
                .Build();

            #endregion
        }

        #region sample_more_about_jobs_getting_job_data

        public class DumbJob : IJob
        {
            public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                JobKey key = context.JobDetail.Key;

                JobDataMap dataMap = context.JobDetail.JobDataMap;

                string? jobSays = dataMap.GetString("jobSays");
                float myFloatValue = dataMap.GetFloat("myFloatValue");

                await Console.Error.WriteLineAsync("Instance " + key + " of DumbJob says: " + jobSays + ", and val is: " + myFloatValue);
            }
        }

        #endregion
    }

    public static class JobDataByProperty
    {
        #region sample_more_about_jobs_job_with_properties

        public class DumbJob : IJob
        {
            public string JobSays { get; set; } = "";
            public float FloatValue { get; set; }

            public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
        }

        #endregion

        public static IJobDetail NamingTheProperty()
        {
            #region sample_more_about_jobs_naming_the_property

            IJobDetail job = JobBuilder.Create<DumbJob>()
                .WithIdentity("myJob", "group1")
                .UsingJobData(j => j.JobSays, "Hello World!")
                .UsingJobData(j => j.FloatValue, 3.141f)
                .Build();

            #endregion

            return job;
        }

        public static void NamingThePropertyOnATrigger()
        {
            IJobDetail job = NamingTheProperty();

            #region sample_more_about_jobs_naming_the_property_on_a_trigger

            ITrigger trigger = TriggerBuilder.Create<DumbJob>()
                .WithIdentity("myTrigger", "group1")
                .ForJob(job)
                .UsingJobData(j => j.JobSays, "Good evening!")
                .Build();

            #endregion
        }

        public static void NamingThePropertyUnderDependencyInjection(IServiceCollection services)
        {
            JobKey jobKey = new("myJob");

            services.AddQuartz(q =>
            {
                #region sample_more_about_jobs_naming_the_property_under_di

                q.AddJob<DumbJob>(j => j.UsingJobData(x => x.JobSays, "Hello World!"));

                q.ScheduleJob<DumbJob>(
                    t => t.StartNow().UsingJobData(x => x.JobSays, "Good evening!"),
                    j => j.WithIdentity("myJob"));

                // a trigger added on its own names the job type it fires
                q.AddTrigger<DumbJob>(t => t.ForJob(jobKey).UsingJobData(x => x.JobSays, "Good evening!"));

                #endregion
            });
        }
    }

    public static class MergedJobData
    {
        #region sample_more_about_jobs_merged_job_data

        public class DumbJob : IJob
        {
            public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                JobKey key = context.JobDetail.Key;

                JobDataMap dataMap = context.MergedJobDataMap;  // Note the difference from the previous example

                string? jobSays = dataMap.GetString("jobSays");
                float myFloatValue = dataMap.GetFloat("myFloatValue");
                IList<DateTimeOffset> state = (IList<DateTimeOffset>) dataMap["myStateData"]!;
                state.Add(DateTimeOffset.UtcNow);

                await Console.Error.WriteLineAsync("Instance " + key + " of DumbJob says: " + jobSays + ", and val is: " + myFloatValue);
            }
        }

        #endregion
    }

    public static class InjectedJobData
    {
        #region sample_more_about_jobs_injected_job_data

        public class DumbJob : IJob
        {
            public string JobSays { private get; set; } = "";
            public float FloatValue { private get; set; }

            public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                JobKey key = context.JobDetail.Key;

                JobDataMap dataMap = context.MergedJobDataMap;  // Note the difference from the previous example

                IList<DateTimeOffset> state = (IList<DateTimeOffset>) dataMap["myStateData"]!;
                state.Add(DateTimeOffset.UtcNow);

                await Console.Error.WriteLineAsync("Instance " + key + " of DumbJob says: " + JobSays + ", and val is: " + FloatValue);
            }
        }

        #endregion
    }

    public static async ValueTask RefiringFromAJobExecutionException()
    {
        try
        {
            await Task.Yield();
        }
        #region sample_more_about_jobs_job_execution_exception
        catch (Exception ex)
        {
            // ask the scheduler to run this fire again with the same context
            throw new JobExecutionException(ex) { RefireImmediately = true };
        }
        #endregion
    }
}

#region sample_more_about_jobs_custom_job_detail

public sealed class TenantJobDetail : IJobDetail
{
    public TenantJobDetail(JobKey key, JobType jobType, string tenant, JobDataMap? jobDataMap = null)
    {
        Key = key;
        JobType = jobType;
        Tenant = tenant;
        JobDataMap = jobDataMap ?? new JobDataMap();
    }

    public string Tenant { get; }

    public JobKey Key { get; }
    public string Description => $"jobs for {Tenant}";
    public JobType JobType { get; }
    public JobDataMap JobDataMap { get; }
    public bool Durable => true;
    public bool PersistJobDataAfterExecution => true;
    public bool ConcurrentExecutionDisallowed => true;
    public bool RequestsRecovery => false;

    // How a job store re-stores the data a [PersistJobDataAfterExecution] job left behind: it asks the
    // detail for a copy of itself rather than building one, which it could only do as Quartz's own type.
    public IJobDetail WithJobData(JobDataMap jobDataMap)
        => new TenantJobDetail(Key, JobType, Tenant, jobDataMap);

    public IJobDetail Clone()
        => new TenantJobDetail(Key, JobType, Tenant, new JobDataMap(JobDataMap));
}

#endregion
