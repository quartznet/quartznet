using System;
using System.Collections.Generic;
using System.Collections.Specialized;

using Quartz.Impl;

namespace Quartz
{
    public class QuartzOptions : NameValueCollection, IDictionary<string, string>
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
            get
            {
                var value = this["quartz.jobStore.misfireThreshold"];
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }
                return TimeSpan.FromMilliseconds(int.Parse(value)); 
            }
            set => this["quartz.jobStore.misfireThreshold"] =  value != null ? ((int) value.Value.TotalMilliseconds).ToString() : "";
        }

        public SchedulingOptions Scheduling { get; set; } = new SchedulingOptions();

        public JobFactoryOptions JobFactory { get; set; } = new JobFactoryOptions();

        public IReadOnlyList<IJobDetail> JobDetails => jobDetails;

        public IReadOnlyList<ITrigger> Triggers => triggers;

        public QuartzOptions AddJob(Type jobType, Action<JobBuilder> configure)
        {
            var builder = JobBuilder.Create(jobType);
            configure(builder);
            jobDetails.Add(builder.Build());
            return this;
        }

        public QuartzOptions AddJob<T>(Action<JobBuilder> configure) where T : IJob, new()
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

        IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator()
        {
            throw new NotImplementedException();
        }

        void ICollection<KeyValuePair<string, string>>.Add(KeyValuePair<string, string> item)
        {
            throw new NotImplementedException();
        }

        bool ICollection<KeyValuePair<string, string>>.Contains(KeyValuePair<string, string> item)
        {
            throw new NotImplementedException();
        }

        void ICollection<KeyValuePair<string, string>>.CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        bool ICollection<KeyValuePair<string, string>>.Remove(KeyValuePair<string, string> item)
        {
            throw new NotImplementedException();
        }

        bool ICollection<KeyValuePair<string, string>>.IsReadOnly => false;

        bool IDictionary<string, string>.ContainsKey(string key)
        {
            throw new NotImplementedException();
        }

        bool IDictionary<string, string>.Remove(string key)
        {
            throw new NotImplementedException();
        }

        bool IDictionary<string, string>.TryGetValue(string key, out string value)
        {
            throw new NotImplementedException();
        }

        ICollection<string> IDictionary<string, string>.Keys => throw new NotImplementedException();

        ICollection<string> IDictionary<string, string>.Values => throw new NotImplementedException();
    }
}
