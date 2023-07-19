using System.Collections.Specialized;

using Quartz.Impl;

namespace Quartz;

public class QuartzOptions : Dictionary<string, string?>
{
    internal readonly List<IJobDetail> jobDetails = new();
    internal readonly List<ITrigger> triggers = new();

    public string? SchedulerId
    {
        get
        {
            TryGetValue(StdSchedulerFactory.PropertySchedulerInstanceId, out var schedulerId);
            return schedulerId;
        }
        set => this[StdSchedulerFactory.PropertySchedulerInstanceId] = value;
    }

    public string? SchedulerName
    {
        get
        {
            TryGetValue(StdSchedulerFactory.PropertySchedulerName, out var schedulerName);
            return schedulerName;
        }
        set => this[StdSchedulerFactory.PropertySchedulerName] = value;
    }

    public TimeSpan? MisfireThreshold
    {
        get
        {
            if (!TryGetValue("quartz.jobStore.misfireThreshold", out var value) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return TimeSpan.FromMilliseconds(int.Parse(value));
        }
        set => this["quartz.jobStore.misfireThreshold"] = value != null ? ((int) value.Value.TotalMilliseconds).ToString() : "";
    }

    public SchedulingOptions Scheduling { get; set; } = new();

    public JobFactoryOptions JobFactory { get; set; } = new();

    public IReadOnlyList<IJobDetail> JobDetails => jobDetails;

    public IReadOnlyList<ITrigger> Triggers => triggers;

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

    public NameValueCollection ToNameValueCollection()
    {
        var collection = new NameValueCollection(Count);
        foreach (var pair in this)
        {
            collection[pair.Key] = pair.Value;
        }

        return collection;
    }
}