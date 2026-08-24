namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/jobs-and-triggers.md.
/// </summary>
public static class JobsAndTriggersSamples
{
    public static async ValueTask TheBuilderDsl(IScheduler scheduler)
    {
        #region sample_jobs_and_triggers_builders

        // define the job and tie it to our HelloJob class
        IJobDetail job = JobBuilder.Create<HelloJob>()
            .WithIdentity("myJob", "group1") // name "myJob", group "group1"
            .Build();

        // Trigger the job to run now, and then every 40 seconds
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("myTrigger", "group1")
            .StartNow()
            .WithSimpleSchedule(x => x
                .WithInterval(TimeSpan.FromSeconds(40))
                .RepeatForever())
            .Build();

        // Tell Quartz to schedule the job using our trigger
        await scheduler.ScheduleJob(job, trigger);

        #endregion
    }
}
