namespace Quartz.Diagnostics;

public static class OperationName
{
    public static class Job
    {
        public const string Execute = "Quartz.Job.Execute";

        /// <summary>
        /// The span covering a vetoed fire: the trigger listeners said no, and the job listeners are being
        /// told so instead of the job being run.
        /// </summary>
        /// <remarks>
        /// Its value used to be <c>Quartz.Job.Vetoed</c> while the constant was named <c>Veto</c>, so the
        /// one name in this class that did not match its value was the one nobody could see. Every other
        /// operation here is named for the operation, in the present tense, as <see cref="Execute"/> is.
        /// </remarks>
        public const string Veto = "Quartz.Job.Veto";
    }

    /// <summary>
    /// The names of the spans a scheduler's calls into its store are traced under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A constant exists for every store operation that changes state or drives the fire cycle, and
    /// for nothing else.</b> Reads are not traced — the <c>Get*</c> and <c>Query*</c> members and the
    /// three <c>Exists</c> overloads — because a read is one database round trip whose cost the caller's
    /// own span already covers, and a span saying "the store was asked" adds a frame rather than a fact.
    /// Neither are the lifecycle members <c>Initialize</c>, <c>Shutdown</c>, <c>SchedulerStarted</c>,
    /// <c>SchedulerPaused</c> and <c>SchedulerResumed</c>: each happens once, outside any request, so a
    /// span for it is a root of its own with nothing to be a child of. Nor is <c>GetAcquireRetryDelay</c>,
    /// which is advice a store gives out of its own configuration rather than an operation it performs.
    /// </para>
    /// <para>
    /// The subset is therefore deliberate rather than half-finished, and it is exact in both directions
    /// — every constant here names a span <c>TracingJobStore</c> begins, and every span it begins is
    /// named here. <c>OperationNameTest</c> holds both halves, and separately holds every mutating
    /// member of <see cref="Quartz.Extensibility.IJobStore" /> to having a constant, so a member added
    /// to the interface without a span fails the build rather than going quietly untraced.
    /// </para>
    /// <para>
    /// These strings are the telemetry contract: dashboards, alerts and sampling rules match on them, so
    /// a rename is a breaking change for everyone watching and not a refactoring.
    /// </para>
    /// </remarks>
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
        public const string ResetTriggersFromErrorState = "Quartz.JobStore.ResetTriggersFromErrorState";
        public const string Clear = "Quartz.JobStore.Clear";
    }
}