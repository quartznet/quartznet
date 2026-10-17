using System;
using System.Collections.Generic;
using System.Collections.Specialized;

using Quartz.Impl;

namespace Quartz;

public class QuartzOptions : Dictionary<string, string?>
{
    internal readonly List<IJobDetail> jobDetails = new();
    internal readonly List<ITrigger> triggers = new();

    // The keys registrations generated because they gave their job or trigger no identity, which a
    // persistent store keeps one more copy of on every start; ContainerConfigurationProcessor warns.
    internal readonly List<GeneratedIdentity> generatedIdentities = new();

    // Deferred configuration lists — populated by DeferredServiceCollection when
    // AddQuartz(Action<IServiceCollectionQuartzConfigurator, IServiceProvider>) is used.
    internal readonly List<SchedulerListenerConfiguration> deferredSchedulerListeners = new();
    internal readonly List<JobListenerConfiguration> deferredJobListeners = new();
    internal readonly List<TriggerListenerConfiguration> deferredTriggerListeners = new();
    internal readonly List<CalendarConfiguration> deferredCalendars = new();
    internal readonly DeferredSingletonRegistry deferredSingletons = new();

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
        AddJob(builder, $"AddJob(typeof({jobType.FullName}))");
        return this;
    }

    public QuartzOptions AddJob<
#if NET6_0_OR_GREATER
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(Action<JobBuilder> configure) where T : IJob
    {
        var builder = JobBuilder.Create<T>();
        configure(builder);
        AddJob(builder, $"AddJob<{typeof(T).FullName}>");
        return this;
    }

    public QuartzOptions AddTrigger(Action<TriggerBuilder> configure)
    {
        var builder = TriggerBuilder.Create();
        configure(builder);

        // Read before Build, which generates a key for a trigger given none.
        bool identityGenerated = builder.Key is null;
        var trigger = builder.Build();
        triggers.Add(trigger);

        if (identityGenerated)
        {
            generatedIdentities.Add(new GeneratedIdentity($"AddTrigger for job '{trigger.JobKey}'", "trigger", trigger.Key.ToString()));
        }

        return this;
    }

    private void AddJob(JobBuilder builder, string registration)
    {
        var jobDetail = builder.Build();
        jobDetails.Add(jobDetail);

        // The builder's key stays null when none was given; Build generated the one the job carries.
        if (builder.Key is null)
        {
            generatedIdentities.Add(new GeneratedIdentity(registration, "job", jobDetail.Key.ToString()));
        }
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