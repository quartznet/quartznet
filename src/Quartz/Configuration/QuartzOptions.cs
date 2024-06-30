using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

using Quartz.Impl;

namespace Quartz;

public class QuartzOptions : Dictionary<string, string?>
{
    internal readonly List<IJobDetail> _jobDetails = new();
    internal readonly List<ITrigger> _triggers = new();

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
        set => this["quartz.jobStore.misfireThreshold"] = value is not null ? ((int) value.Value.TotalMilliseconds).ToString() : "";
    }

    public SchedulingOptions Scheduling { get; set; } = new();

    public JobFactoryOptions JobFactory { get; set; } = new();

    public IReadOnlyList<IJobDetail> JobDetails => _jobDetails;

    public IReadOnlyList<ITrigger> Triggers => _triggers;

    public QuartzOptions AddJob(Type jobType, Action<JobBuilder> configure)
    {
        var builder = JobBuilder.Create(jobType);
        configure(builder);
        _jobDetails.Add(builder.Build());
        return this;
    }

    public QuartzOptions AddJob<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(Action<JobBuilder> configure) where T : IJob
    {
        var builder = JobBuilder.Create<T>();
        configure(builder);
        _jobDetails.Add(builder.Build());
        return this;
    }

    public QuartzOptions AddTrigger(Action<TriggerBuilder> configure)
    {
        var builder = TriggerBuilder.Create();
        configure(builder);
        _triggers.Add(builder.Build());
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