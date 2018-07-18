using System;
using System.Collections.Generic;

namespace Quartz.Impl.RavenDB
{
    internal class Job : IHasGroup
    {
        private Job()
        {
        }

        public Job(
            IJobDetail newJob,
            string schedulerInstanceName) : this()
        {
            newJob.Key.Validate();

            Id = newJob.Key.DocumentId(schedulerInstanceName);
            Name = newJob.Key.Name;
            Group = newJob.Key.Group;

            Scheduler = schedulerInstanceName;

            UpdateWith(newJob);
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string Group { get; set; }
        public string Scheduler { get; set; }

        public string Description { get; set; }
        public Type JobType { get; set; }
        public bool Durable { get; set; }
        public bool ConcurrentExecutionDisallowed { get; set; }
        public bool PersistJobDataAfterExecution { get; set; }
        public bool RequestsRecovery { get; set; }
        public IDictionary<string, object> JobDataMap { get; set; }

        internal void UpdateWith(IJobDetail job)
        {
            Description = job.Description;
            JobType = job.JobType;
            Durable = job.Durable;
            ConcurrentExecutionDisallowed = job.ConcurrentExecutionDisallowed;
            PersistJobDataAfterExecution = job.PersistJobDataAfterExecution;
            RequestsRecovery = job.RequestsRecovery;
            JobDataMap = new Dictionary<string, object>(job.JobDataMap.WrappedMap);
        }

        public IJobDetail Deserialize()
        {
            return JobBuilder.Create()
                    .WithIdentity(Name, Group)
                    .WithDescription(Description)
                    .OfType(JobType)
                    .RequestRecovery(RequestsRecovery)
                    .SetJobData(new JobDataMap(JobDataMap))
                    .StoreDurably(Durable)
                    .Build();

            // A JobDetail doesn't have builder methods for two properties:   IsNonConcurrent,IsUpdateData
            // they are determined according to attributes on the job class
        }

    }
}