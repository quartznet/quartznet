namespace Quartz.Configuration;

internal sealed class TriggerConfigurator : ITriggerConfigurator
{
    private readonly TriggerBuilder triggerBuilder = TriggerBuilder.Create();

    public ITriggerConfigurator WithIdentity(string name)
    {
        triggerBuilder.WithIdentity(name);
        return this;
    }

    public ITriggerConfigurator WithIdentity(string name, string @group)
    {
        triggerBuilder.WithIdentity(name, @group);
        return this;
    }

    public ITriggerConfigurator WithIdentity(TriggerKey key)
    {
        triggerBuilder.WithIdentity(key);
        return this;
    }

    public ITriggerConfigurator WithDescription(string? description)
    {
        triggerBuilder.WithDescription(description);
        return this;
    }

    public ITriggerConfigurator WithPriority(int priority)
    {
        triggerBuilder.WithPriority(priority);
        return this;
    }

    public ITriggerConfigurator ModifiedByCalendar(string? calendarName)
    {
        triggerBuilder.ModifiedByCalendar(calendarName);
        return this;
    }

    public ITriggerConfigurator StartAt(DateTimeOffset startTimeUtc)
    {
        triggerBuilder.StartAt(startTimeUtc);
        return this;
    }

    public ITriggerConfigurator StartNow()
    {
        triggerBuilder.StartNow();
        return this;
    }

    public ITriggerConfigurator EndAt(DateTimeOffset? endTimeUtc)
    {
        triggerBuilder.EndAt(endTimeUtc);
        return this;
    }

    public ITriggerConfigurator WithSchedule(IScheduleBuilder scheduleBuilder)
    {
        triggerBuilder.WithSchedule(scheduleBuilder);
        return this;
    }

    public ITriggerConfigurator ForJob(JobKey jobKey)
    {
        triggerBuilder.ForJob(jobKey);
        return this;
    }

    public ITriggerConfigurator ForJob(string jobName)
    {
        triggerBuilder.ForJob(jobName);
        return this;
    }

    public ITriggerConfigurator ForJob(string jobName, string jobGroup)
    {
        triggerBuilder.ForJob(jobName, jobGroup);
        return this;
    }

    public ITriggerConfigurator ForJob(IJobDetail jobDetail)
    {
        triggerBuilder.ForJob(jobDetail);
        return this;
    }

    public ITriggerConfigurator UsingJobData(JobDataMap newJobDataMap)
    {
        triggerBuilder.UsingJobData(newJobDataMap);
        return this;
    }

    public ITriggerConfigurator UsingJobData(string key, string value)
    {
        triggerBuilder.UsingJobData(key, value);
        return this;
    }

    public ITriggerConfigurator UsingJobData(string key, int value)
    {
        triggerBuilder.UsingJobData(key, value);
        return this;
    }

    public ITriggerConfigurator UsingJobData(string key, long value)
    {
        triggerBuilder.UsingJobData(key, value);
        return this;
    }

    public ITriggerConfigurator UsingJobData(string key, float value)
    {
        triggerBuilder.UsingJobData(key, value);
        return this;
    }

    public ITriggerConfigurator UsingJobData(string key, double value)
    {
        triggerBuilder.UsingJobData(key, value);
        return this;
    }

    public ITriggerConfigurator UsingJobData(string key, decimal value)
    {
        triggerBuilder.UsingJobData(key, value);
        return this;
    }

    public ITriggerConfigurator UsingJobData(string key, bool value)
    {
        triggerBuilder.UsingJobData(key, value);
        return this;
    }

    public ITriggerConfigurator UsingJobData(string key, Guid value)
    {
        triggerBuilder.UsingJobData(key, value);
        return this;
    }

    public ITriggerConfigurator UsingJobData(string key, char value)
    {
        triggerBuilder.UsingJobData(key, value);
        return this;
    }

    internal ITrigger Build() => triggerBuilder.Build();
}