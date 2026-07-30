namespace Quartz.Diagnostics;

public static class OperationName
{
    public static class Job
    {
        public const string Execute = "Quartz.Job.Execute";
        public const string Veto = "Quartz.Job.Vetoed";
    }

    public static class JobStore
    {
        // Tier 1: scheduler loop hot path
        public const string AcquireNextTriggers = "Quartz.JobStore.AcquireNextTriggers";
        public const string TriggersFired = "Quartz.JobStore.TriggersFired";
        public const string TriggeredJobComplete = "Quartz.JobStore.TriggeredJobComplete";
        public const string ReleaseAcquiredTrigger = "Quartz.JobStore.ReleaseAcquiredTrigger";

        // Tier 2: user-initiated scheduling operations
        public const string ScheduleJob = "Quartz.JobStore.ScheduleJob";
        public const string ScheduleJobs = "Quartz.JobStore.ScheduleJobs";
        public const string AddJob = "Quartz.JobStore.AddJob";
        public const string AddTrigger = "Quartz.JobStore.AddTrigger";
        public const string AddCalendar = "Quartz.JobStore.AddCalendar";
        public const string DeleteJob = "Quartz.JobStore.DeleteJob";
        public const string DeleteJobs = "Quartz.JobStore.DeleteJobs";
        public const string DeleteTrigger = "Quartz.JobStore.DeleteTrigger";
        public const string DeleteTriggers = "Quartz.JobStore.DeleteTriggers";
        public const string DeleteCalendar = "Quartz.JobStore.DeleteCalendar";
        public const string ReplaceTrigger = "Quartz.JobStore.ReplaceTrigger";
        public const string UpdateTriggerDetails = "Quartz.JobStore.UpdateTriggerDetails";
        public const string PauseTrigger = "Quartz.JobStore.PauseTrigger";
        public const string PauseTriggers = "Quartz.JobStore.PauseTriggers";
        public const string PauseJob = "Quartz.JobStore.PauseJob";
        public const string PauseJobs = "Quartz.JobStore.PauseJobs";
        public const string ResumeTrigger = "Quartz.JobStore.ResumeTrigger";
        public const string ResumeTriggers = "Quartz.JobStore.ResumeTriggers";
        public const string ResumeJob = "Quartz.JobStore.ResumeJob";
        public const string ResumeJobs = "Quartz.JobStore.ResumeJobs";
        public const string PauseAll = "Quartz.JobStore.PauseAll";
        public const string ResumeAll = "Quartz.JobStore.ResumeAll";
        public const string ResetTriggerFromErrorState = "Quartz.JobStore.ResetTriggerFromErrorState";
        public const string Clear = "Quartz.JobStore.Clear";
    }
}