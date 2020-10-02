using System;
using System.Collections.Generic;
using System.Collections.Specialized;

using Quartz.Impl;

namespace Quartz
{
    public class QuartzOptions : NameValueCollection
    {
        internal readonly List<IJobDetail> jobDetails = new List<IJobDetail>();
        internal readonly List<ITrigger> triggers = new List<ITrigger>();

        public string? SchedulerId
        {
            get => this[StdSchedulerFactory.PropertySchedulerInstanceId];
            set => this[StdSchedulerFactory.PropertySchedulerInstanceId] = value;
        }

        public string? SchedulerName
        {
            get => this[StdSchedulerFactory.PropertySchedulerName];
            set => this[StdSchedulerFactory.PropertySchedulerName] = value;
        }

        public TimeSpan? MisfireThreshold
        {
            get => TimeSpan.FromMilliseconds(int.Parse(this["quartz.jobStore.misfireThreshold"]));
            set => this["quartz.jobStore.misfireThreshold"] =  value != null ? ((int) value.Value.TotalMilliseconds).ToString() : "";
        }

        public SchedulingOptions Scheduling { get; set; } = new SchedulingOptions();

        public JobFactoryOptions JobFactory { get; set; } = new JobFactoryOptions();

        internal IReadOnlyList<IJobDetail> JobDetails => jobDetails;

        internal IReadOnlyList<ITrigger> Triggers => triggers;

        public QuartzOptions AddJob(Type jobType, Action<JobBuilder> configure)
        {
            var builder = JobBuilder.Create(jobType);
            configure(builder);
            jobDetails.Add(builder.Build());
            return this;
        }

        public QuartzOptions AddJob<T>(Action<JobBuilder> configure) where T : IJob
        {
            var builder = JobBuilder.Create<T>();
            configure(builder);
            jobDetails.Add(builder.Build());
            return this;
        }

        public QuartzOptions AddTrigger(Action<TriggerBuilder> configure)
        {
            var builder = TriggerBuilder.Create();
            configure(builder);
            triggers.Add(builder.Build());
            return this;
        }
    }
}