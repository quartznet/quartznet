#if DIAGNOSTICS_SOURCE

using System.Diagnostics;

namespace Quartz.Logging
{
    internal sealed class JobDiagnosticsWriter
    {
        private readonly DiagnosticListener diagnosticListener = LogProvider.Cached.Default.Value;
        private const string name = OperationName.Job.Execute;

        internal Activity? WriteStarted(IJobExecutionContext context, DateTimeOffset startTimeUtc)
        {
            if (!diagnosticListener.IsEnabled(name))
            {
                return null;
            }

            var activity = new Activity(name);
            activity.SetStartTime(startTimeUtc.UtcDateTime);
            
            activity.AddTag(DiagnosticHeaders.SchedulerName, context.Scheduler.SchedulerName);
            activity.AddTag(DiagnosticHeaders.SchedulerId, context.Scheduler.SchedulerInstanceId);
            activity.AddTag(DiagnosticHeaders.FireInstanceId, context.FireInstanceId);
            activity.AddTag(DiagnosticHeaders.TriggerGroup, context.Trigger.Key.Group);
            activity.AddTag(DiagnosticHeaders.TriggerName, context.Trigger.Key.Name);
            activity.AddTag(DiagnosticHeaders.JobType, context.JobDetail.JobType.ToString());
            activity.AddTag(DiagnosticHeaders.JobGroup, context.JobDetail.Key.Group);
            activity.AddTag(DiagnosticHeaders.JobName, context.JobDetail.Key.Name);

            diagnosticListener.StartActivity(activity,context);
            return activity;
        }

        internal void WriteStopped(Activity? activity, DateTimeOffset endTimeUtc, IJobExecutionContext context)
        {
            if (activity != null && diagnosticListener.IsEnabled(name))
            {
                activity.SetEndTime(endTimeUtc.UtcDateTime);
                diagnosticListener.StopActivity(activity, context);
            }
        }

        public void WriteException(Activity? activity, JobExecutionException exception)
        {
            if (activity != null && diagnosticListener.IsEnabled(name))
            {
                diagnosticListener.Write(name + ".Exception", exception);
            }
        }
    }
}
#endif