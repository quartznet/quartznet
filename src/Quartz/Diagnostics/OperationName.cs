namespace Quartz.Diagnostics;

/// <summary>
/// The names Quartz's spans are begun under.
/// </summary>
/// <remarks>
/// These strings are the telemetry contract: dashboards, alerts and sampling rules match on them,
/// so a rename is a breaking change for everyone watching rather than a refactoring.
/// </remarks>
public static class OperationName
{
    /// <summary>
    /// The names of the spans covering one firing of a job.
    /// </summary>
    public static class Job
    {
        /// <summary>
        /// The span covering a job's execution, from the moment the scheduler has a job instance to the moment
        /// its <c>Execute</c> returns or throws.
        /// </summary>
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
        /// <summary>
        /// The span covering <c>IJobStore.AcquireNextTriggers</c>.
        /// </summary>
        public const string AcquireNextTriggers = "Quartz.JobStore.AcquireNextTriggers";

        /// <summary>
        /// The span covering <c>IJobStore.TriggersFired</c>.
        /// </summary>
        public const string TriggersFired = "Quartz.JobStore.TriggersFired";

        /// <summary>
        /// The span covering <c>IJobStore.TriggeredJobComplete</c>.
        /// </summary>
        public const string TriggeredJobComplete = "Quartz.JobStore.TriggeredJobComplete";

        /// <summary>
        /// The span covering <c>IJobStore.ReleaseAcquiredTrigger</c>.
        /// </summary>
        public const string ReleaseAcquiredTrigger = "Quartz.JobStore.ReleaseAcquiredTrigger";

        // Tier 2: user-initiated scheduling operations
        /// <summary>
        /// The span covering <c>IJobStore.ScheduleJob</c>.
        /// </summary>
        public const string ScheduleJob = "Quartz.JobStore.ScheduleJob";

        /// <summary>
        /// The span covering <c>IJobStore.ScheduleJobs</c>.
        /// </summary>
        public const string ScheduleJobs = "Quartz.JobStore.ScheduleJobs";

        /// <summary>
        /// The span covering <c>IJobStore.AddJob</c>.
        /// </summary>
        public const string AddJob = "Quartz.JobStore.AddJob";

        /// <summary>
        /// The span covering <c>IJobStore.AddTrigger</c>.
        /// </summary>
        public const string AddTrigger = "Quartz.JobStore.AddTrigger";

        /// <summary>
        /// The span covering <c>IJobStore.AddCalendar</c>.
        /// </summary>
        public const string AddCalendar = "Quartz.JobStore.AddCalendar";

        /// <summary>
        /// The span covering <c>IJobStore.DeleteJob</c>.
        /// </summary>
        public const string DeleteJob = "Quartz.JobStore.DeleteJob";

        /// <summary>
        /// The span covering <c>IJobStore.DeleteJobs</c>.
        /// </summary>
        public const string DeleteJobs = "Quartz.JobStore.DeleteJobs";

        /// <summary>
        /// The span covering <c>IJobStore.DeleteTrigger</c>.
        /// </summary>
        public const string DeleteTrigger = "Quartz.JobStore.DeleteTrigger";

        /// <summary>
        /// The span covering <c>IJobStore.DeleteTriggers</c>.
        /// </summary>
        public const string DeleteTriggers = "Quartz.JobStore.DeleteTriggers";

        /// <summary>
        /// The span covering <c>IJobStore.DeleteCalendar</c>.
        /// </summary>
        public const string DeleteCalendar = "Quartz.JobStore.DeleteCalendar";

        /// <summary>
        /// The span covering <c>IJobStore.ReplaceTrigger</c>.
        /// </summary>
        public const string ReplaceTrigger = "Quartz.JobStore.ReplaceTrigger";

        /// <summary>
        /// The span covering <c>IJobStore.UpdateTriggerDetails</c>.
        /// </summary>
        public const string UpdateTriggerDetails = "Quartz.JobStore.UpdateTriggerDetails";

        /// <summary>
        /// The span covering <c>IJobStore.PauseTrigger</c>.
        /// </summary>
        public const string PauseTrigger = "Quartz.JobStore.PauseTrigger";

        /// <summary>
        /// The span covering <c>IJobStore.PauseTriggers</c>.
        /// </summary>
        public const string PauseTriggers = "Quartz.JobStore.PauseTriggers";

        /// <summary>
        /// The span covering <c>IJobStore.PauseTriggerGroups</c>.
        /// </summary>
        public const string PauseTriggerGroups = "Quartz.JobStore.PauseTriggerGroups";

        /// <summary>
        /// The span covering <c>IJobStore.PauseJob</c>.
        /// </summary>
        public const string PauseJob = "Quartz.JobStore.PauseJob";

        /// <summary>
        /// The span covering <c>IJobStore.PauseJobs</c>.
        /// </summary>
        public const string PauseJobs = "Quartz.JobStore.PauseJobs";

        /// <summary>
        /// The span covering <c>IJobStore.PauseJobGroups</c>.
        /// </summary>
        public const string PauseJobGroups = "Quartz.JobStore.PauseJobGroups";

        /// <summary>
        /// The span covering <c>IJobStore.ResumeTrigger</c>.
        /// </summary>
        public const string ResumeTrigger = "Quartz.JobStore.ResumeTrigger";

        /// <summary>
        /// The span covering <c>IJobStore.ResumeTriggers</c>.
        /// </summary>
        public const string ResumeTriggers = "Quartz.JobStore.ResumeTriggers";

        /// <summary>
        /// The span covering <c>IJobStore.ResumeTriggerGroups</c>.
        /// </summary>
        public const string ResumeTriggerGroups = "Quartz.JobStore.ResumeTriggerGroups";

        /// <summary>
        /// The span covering <c>IJobStore.ResumeJob</c>.
        /// </summary>
        public const string ResumeJob = "Quartz.JobStore.ResumeJob";

        /// <summary>
        /// The span covering <c>IJobStore.ResumeJobs</c>.
        /// </summary>
        public const string ResumeJobs = "Quartz.JobStore.ResumeJobs";

        /// <summary>
        /// The span covering <c>IJobStore.ResumeJobGroups</c>.
        /// </summary>
        public const string ResumeJobGroups = "Quartz.JobStore.ResumeJobGroups";

        /// <summary>
        /// The span covering <c>IJobStore.PauseAll</c>.
        /// </summary>
        public const string PauseAll = "Quartz.JobStore.PauseAll";

        /// <summary>
        /// The span covering <c>IJobStore.ResumeAll</c>.
        /// </summary>
        public const string ResumeAll = "Quartz.JobStore.ResumeAll";

        /// <summary>
        /// The span covering <c>IJobStore.ResetTriggerFromErrorState</c>.
        /// </summary>
        public const string ResetTriggerFromErrorState = "Quartz.JobStore.ResetTriggerFromErrorState";

        /// <summary>
        /// The span covering <c>IJobStore.ResetTriggersFromErrorState</c>.
        /// </summary>
        public const string ResetTriggersFromErrorState = "Quartz.JobStore.ResetTriggersFromErrorState";

        /// <summary>
        /// The span covering <c>IJobStore.Clear</c>.
        /// </summary>
        public const string Clear = "Quartz.JobStore.Clear";
    }
}