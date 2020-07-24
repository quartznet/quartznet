using System;
using System.Diagnostics;

namespace Quartz.Logging
{
    /// <summary>
    /// Forwards job events to diagnostics source. 
    /// </summary>
    internal sealed class JobDiagnosticsListener
    {
        private readonly DiagnosticListener diagnosticListener;

        public JobDiagnosticsListener()
        {
            diagnosticListener = new DiagnosticListener(DiagnosticHeaders.DefaultListenerName);
        }

        internal void JobExecutionVetoed(IJobExecutionContext context)
        {
            // the job actually never started
            if (diagnosticListener.IsEnabled(OperationName.Job.Veto, context))
            {
                diagnosticListener.Write(OperationName.Job.Veto, context);
            }
        }

        internal Activity? JobStarting(IJobExecutionContext context, DateTimeOffset startTimeUtc)
        {
            if (!diagnosticListener.IsEnabled(OperationName.Job.Execute))
            {
                return null;
            }

            var activity = new Activity(OperationName.Job.Execute);
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

        public void JobExecutionException(Activity? activity, JobExecutionException exception)
        {
            if (activity != null && diagnosticListener.IsEnabled(OperationName.Job.Execute))
            {
                diagnosticListener.Write(OperationName.Job.Execute + ".Exception", exception);
            }
        }

        internal void JobEnded(Activity? activity, DateTimeOffset endTimeUtc, IJobExecutionContext context)
        {
            if (activity != null && diagnosticListener.IsEnabled(OperationName.Job.Execute))
            {
                activity.SetEndTime(endTimeUtc.UtcDateTime);
                diagnosticListener.StopActivity(activity, context);
            }
        }
    }
}