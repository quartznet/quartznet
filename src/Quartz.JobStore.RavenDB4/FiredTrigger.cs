using System;

using Quartz.Simpl;

namespace Quartz.Impl.RavenDB
{
    internal class FiredTrigger
    {
        private FiredTrigger()
        {
        }

        public FiredTrigger(
            string id,
            string scheduler) : this()
        {
            Id = id;
            Scheduler = scheduler;
        }

        public string Id { get; private set; }
        public string Scheduler { get; private set; }

        public string SchedulerInstanceId { get; set; }
        public string TriggerId { get; set; }
        public string JobId { get; set; }
        public DateTimeOffset FiredTime { get; set; }
        public DateTimeOffset ScheduledTime { get; set; }
        public int Priority { get; set; }
        public InternalTriggerState State { get; set; }
        public bool IsNonConcurrent { get; set; }
        public bool RequestsRecovery { get; set; }

        public FiredTriggerRecord Deserialize()
        {
            var rec = new FiredTriggerRecord();
            rec.FireInstanceId = Id;
            rec.SchedulerInstanceName = SchedulerInstanceId;
            rec.FireInstanceState = State;
            rec.FireTimestamp = FiredTime;
            rec.ScheduleTimestamp = ScheduledTime;
            rec.Priority = Priority;
            rec.TriggerKey = TriggerId.TriggerIdFromDocumentId();
            if (rec.FireInstanceState != InternalTriggerState.Acquired)
            {
                rec.JobDisallowsConcurrentExecution = IsNonConcurrent;
                rec.JobRequestsRecovery = RequestsRecovery;
                rec.JobKey = JobId.JobKeyFromDocumentId();
            }

            return rec;
        }
    }
}