using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/scheduler-listeners.md and
/// docs/documentation/quartz-4.x/tutorial/trigger-and-job-listeners.md.
/// </summary>
public static class ListenersSamples
{
    public static void RegisteringASchedulerListener(IScheduler scheduler, ISchedulerListener mySchedListener)
    {
        #region sample_scheduler_listeners_add

        scheduler.ListenerManager.AddSchedulerListener(mySchedListener);

        #endregion
    }

    public static void RemovingASchedulerListener(IScheduler scheduler, ISchedulerListener mySchedListener)
    {
        #region sample_scheduler_listeners_remove

        scheduler.ListenerManager.RemoveSchedulerListener(mySchedListener.Name);

        #endregion
    }

    public static void RegisteringASchedulerListenerUnderDependencyInjection(IHostApplicationBuilder builder)
    {
        #region sample_scheduler_listeners_under_di

        builder.AddQuartz(q =>
        {
            q.AddSchedulerListener<AuditSchedulerListener>();
        });

        #endregion
    }

    public static void MatchingOneJob(IScheduler scheduler, IJobListener myJobListener)
    {
        #region sample_job_listeners_match_one_job

        scheduler.ListenerManager.AddJobListener(myJobListener, Matchers.Key(new JobKey("myJobName", "myJobGroup")));

        #endregion
    }

    public static void MatchingAGroup(IScheduler scheduler, IJobListener myJobListener)
    {
        #region sample_job_listeners_match_a_group

        scheduler.ListenerManager.AddJobListener(myJobListener, GroupMatcher<JobKey>.GroupEquals("myJobGroup"));

        #endregion
    }

    public static void MatchingTwoGroups(IScheduler scheduler, IJobListener myJobListener)
    {
        #region sample_job_listeners_match_two_groups

        scheduler.ListenerManager.AddJobListener(myJobListener,
            GroupMatcher<JobKey>.GroupEquals("myJobGroup").Or(GroupMatcher<JobKey>.GroupEquals("yourGroup")));

        #endregion
    }

    public static void MatchingEveryJob(IScheduler scheduler, IJobListener myJobListener)
    {
        #region sample_job_listeners_match_every_job

        scheduler.ListenerManager.AddJobListener(myJobListener, Matchers.AllJobs());

        #endregion
    }

    public static void RegisteringListenersUnderDependencyInjection(IHostApplicationBuilder builder)
    {
        #region sample_job_listeners_under_di

        builder.AddQuartz(q =>
        {
            // every job
            q.AddJobListener<AuditListener>();

            // only the reporting group, and only triggers whose name starts with "nightly"
            q.AddJobListener<ReportAuditListener>(GroupMatcher<JobKey>.GroupEquals("reports"));
            q.AddTriggerListener<NightlyListener>(NameMatcher<TriggerKey>.NameStartsWith("nightly"));

            // an instance you built yourself, or a factory over the provider
            q.AddTriggerListener(new VetoWeekends(), Matchers.AllTriggers());
            q.AddJobListener(provider => new MeteredListener(provider.GetRequiredService<IMeterFactory>()));
        });

        #endregion
    }
}
