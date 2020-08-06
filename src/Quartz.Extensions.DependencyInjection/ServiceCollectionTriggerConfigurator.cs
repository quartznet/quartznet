using System;

using Microsoft.Extensions.DependencyInjection;

namespace Quartz
{
    internal class ServiceCollectionTriggerConfigurator : IServiceCollectionTriggerConfigurator
    {
        private readonly IServiceCollection services;
        private readonly TriggerBuilder triggerBuilder = new TriggerBuilder();

        public ServiceCollectionTriggerConfigurator(IServiceCollection services)
        {
            this.services = services;
        }

        public IServiceCollectionTriggerConfigurator WithIdentity(string name)
        {
            triggerBuilder.WithIdentity(name);
            return this;
        }

        public IServiceCollectionTriggerConfigurator WithIdentity(string name, string @group)
        {
            triggerBuilder.WithIdentity(name, @group);
            return this;
        }

        public IServiceCollectionTriggerConfigurator WithIdentity(TriggerKey key)
        {
            triggerBuilder.WithIdentity(key);
            return this;
        }

        public IServiceCollectionTriggerConfigurator WithDescription(string? description)
        {
            triggerBuilder.WithDescription(description);
            return this;
        }

        public IServiceCollectionTriggerConfigurator WithPriority(int priority)
        {
            triggerBuilder.WithPriority(priority);
            return this;
        }

        public IServiceCollectionTriggerConfigurator ModifiedByCalendar(string? calendarName)
        {
            triggerBuilder.ModifiedByCalendar(calendarName);
            return this;
        }

        public IServiceCollectionTriggerConfigurator StartAt(DateTimeOffset startTimeUtc)
        {
            triggerBuilder.StartAt(startTimeUtc);
            return this;
        }

        public IServiceCollectionTriggerConfigurator StartNow()
        {
            triggerBuilder.StartNow();
            return this;
        }

        public IServiceCollectionTriggerConfigurator EndAt(DateTimeOffset? endTimeUtc)
        {
            triggerBuilder.EndAt(endTimeUtc);
            return this;
        }

        public IServiceCollectionTriggerConfigurator WithSchedule(IScheduleBuilder scheduleBuilder)
        {
            triggerBuilder.WithSchedule(scheduleBuilder);
            return this;
        }

        public IServiceCollectionTriggerConfigurator ForJob(JobKey jobKey)
        {
            triggerBuilder.ForJob(jobKey);
            return this;
        }

        public IServiceCollectionTriggerConfigurator ForJob(string jobName)
        {
            triggerBuilder.ForJob(jobName);
            return this;
        }

        public IServiceCollectionTriggerConfigurator ForJob(string jobName, string jobGroup)
        {
            triggerBuilder.ForJob(jobName, jobGroup);
            return this;
        }

        public IServiceCollectionTriggerConfigurator ForJob(IJobDetail jobDetail)
        {
            triggerBuilder.ForJob(jobDetail);
            return this;
        }

        public IServiceCollectionTriggerConfigurator UsingJobData(JobDataMap newJobDataMap)
        {
            triggerBuilder.UsingJobData(newJobDataMap);
            return this;
        }

        public IServiceCollectionTriggerConfigurator UsingJobData(string key, string value)
        {
            triggerBuilder.UsingJobData(key, value);
            return this;
        }

        public IServiceCollectionTriggerConfigurator UsingJobData(string key, int value)
        {
            triggerBuilder.UsingJobData(key, value);
            return this;
        }

        public IServiceCollectionTriggerConfigurator UsingJobData(string key, long value)
        {
            triggerBuilder.UsingJobData(key, value);
            return this;
        }

        public IServiceCollectionTriggerConfigurator UsingJobData(string key, float value)
        {
            triggerBuilder.UsingJobData(key, value);
            return this;
        }

        public IServiceCollectionTriggerConfigurator UsingJobData(string key, double value)
        {
            triggerBuilder.UsingJobData(key, value);
            return this;
        }

        public IServiceCollectionTriggerConfigurator UsingJobData(string key, decimal value)
        {
            triggerBuilder.UsingJobData(key, value);
            return this;
        }

        public IServiceCollectionTriggerConfigurator UsingJobData(string key, bool value)
        {
            triggerBuilder.UsingJobData(key, value);
            return this;
        }

        internal ITrigger Build() => triggerBuilder.Build();
    }
}