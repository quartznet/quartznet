using System;
using System.Collections.Generic;
using System.Collections.Specialized;

using Quartz.Impl;

namespace Quartz
{
    public class QuartzOptions : NameValueCollection
    {
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

        internal List<IJobDetail> JobDetails { get; set; } = new List<IJobDetail>();
        internal List<ITrigger> Triggers { get; set; } = new List<ITrigger>();
        internal List<CalendarConfiguration> Calendars { get; set; } = new List<CalendarConfiguration>();

        public QuartzOptions AddJob(Type jobType, Action<JobBuilder> configure)
        {
            var builder = JobBuilder.Create(jobType);
            configure(builder);
            JobDetails.Add(builder.Build());
            return this;
        }

        public QuartzOptions AddJob<T>(Action<JobBuilder> configure) where T : IJob
        {
            var builder = JobBuilder.Create<T>();
            configure(builder);
            JobDetails.Add(builder.Build());
            return this;
        }

        public QuartzOptions AddTrigger(Action<TriggerBuilder> configure)
        {
            var builder = TriggerBuilder.Create();
            configure(builder);
            Triggers.Add(builder.Build());
            return this;
        }
    }
}