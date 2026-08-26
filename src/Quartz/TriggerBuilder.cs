#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Util;

namespace Quartz;

/// <summary>
/// TriggerBuilder is used to instantiate <see cref="ITrigger" />s.
/// </summary>
/// <remarks>
/// <para>
/// The builder will always try to keep itself in a valid state, with
/// reasonable defaults set for calling build() at any point.  For instance
/// if you do not invoke <i>WithSchedule(..)</i> method, a default schedule
/// of firing once immediately will be used.  As another example, if you
/// do not invoked <i>WithIdentity(..)</i> a trigger name will be generated
/// for you.
/// </para>
/// <para>
/// Quartz provides a builder-style API for constructing scheduling-related
/// entities via a Domain-Specific Language (DSL).  The DSL can best be
/// utilized through the usage of static imports of the methods on the classes
/// <see cref="TriggerBuilder" />, <see cref="JobBuilder" />,
/// <see cref="DateBuilder" />, <see cref="JobKey" />, <see cref="TriggerKey" />
/// and the various <see cref="IScheduleBuilder" /> implementations.
/// </para>
/// <para>
/// Client code can then use the DSL to write code such as this:
/// </para>
/// <code>
/// IJobDetail job = JobBuilder.Create&lt;MyJob>()
///     .WithIdentity("myJob")
///     .Build();
/// ITrigger trigger = TriggerBuilder.Create()
///     .WithIdentity("myTrigger", "myTriggerGroup")
///     .WithSimpleSchedule(x => x
///         .WithInterval(TimeSpan.FromHours(1))
///         .RepeatForever())
///     .StartAt(DateTimeOffset.UtcNow.AddMinutes(10))
///     .Build();
/// scheduler.scheduleJob(job, trigger);
/// </code>
/// </remarks>
/// <seealso cref="JobBuilder" />
/// <seealso cref="IScheduleBuilder" />
/// <seealso cref="DateBuilder" />
/// <seealso cref="ITrigger" />
public static class TriggerBuilder
{
    /// <summary>
    /// Create a new TriggerBuilder with which to define a
    /// specification for a Trigger.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>the new TriggerBuilder</returns>
    public static TriggerBuilder<IJob> Create(TimeProvider? timeProvider = null)
    {
        return new TriggerBuilder<IJob>(timeProvider ?? TimeProvider.System);
    }

    /// <summary>
    /// Create a new TriggerBuilder for a trigger that fires a known job type.
    /// </summary>
    /// <remarks>
    /// The job type stays with the builder, so the trigger's job data can name the job's properties rather
    /// than spell their keys.
    /// </remarks>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>the new TriggerBuilder</returns>
    public static TriggerBuilder<TJob> Create<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(TimeProvider? timeProvider = null) where TJob : IJob
    {
        return new TriggerBuilder<TJob>(timeProvider ?? TimeProvider.System);
    }
}

/// <summary>
/// TriggerBuilder is used to instantiate <see cref="ITrigger" />s for a trigger that fires a known job type.
/// </summary>
/// <remarks>
/// Knowing the job type is what lets <see cref="UsingJobData{TValue}" /> take the job's property instead of
/// its key. <c>TriggerBuilder.Create()</c> gives a builder for <see cref="IJob" />, which has no properties
/// to name; <c>TriggerBuilder.Create&lt;TJob&gt;()</c> gives one that does.
/// </remarks>
/// <seealso cref="TriggerBuilder" />
public sealed class TriggerBuilder<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob> : ITriggerConfigurator<TJob> where TJob : IJob
{
    private readonly TimeProvider timeProvider;
    private TriggerKey? key;
    private string? description;
    private DateTimeOffset startTime;
    private DateTimeOffset? endTime;
    private int priority = TriggerConstants.DefaultPriority;
    private string? calendarName;
    private JobKey? jobKey;
    private readonly JobDataMap jobDataMap = new JobDataMap();
    private string? executionGroup;
    private PreferredNode preferredNode;

    private IScheduleBuilder? scheduleBuilder;

    internal TriggerBuilder(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
        this.startTime = timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Produce the <see cref="ITrigger" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>a Trigger that meets the specifications of the builder.</returns>
    public ITrigger Build()
    {
        if (scheduleBuilder is null)
        {
            scheduleBuilder = SimpleScheduleBuilder.Create();
        }

        // Resolve deferred H (hash) tokens using trigger identity
        if (scheduleBuilder is IHashKeyAwareScheduleBuilder hashAware && hashAware.RequiresHashKey)
        {
            if (key is null)
            {
                throw new FormatException(
                    "Trigger identity must be set via WithIdentity() when using H (hash) tokens "
                    + "in cron expressions. The trigger key (name + group) is used as the hash "
                    + "seed to produce deterministic, spread-out fire times.");
            }
            hashAware.SetHashKey(key);
        }

        // Hand the schedule builder this builder's clock, so schedule computation deferred to
        // Build() (EndingDailyAfterCount) runs against the same TimeProvider the trigger does.
        if (scheduleBuilder is ITimeProviderAwareScheduleBuilder timeProviderAware)
        {
            timeProviderAware.SetTimeProvider(timeProvider);
        }

        IMutableTrigger trig = scheduleBuilder.Build();

        // And hand the trigger itself the same clock. The schedule builder constructs it with no
        // clock at all - it has no reason to know about one - so without this every "now" the built
        // trigger reads (the past-due clamp in ComputeFirstFireTimeUtc, the whole of
        // UpdateAfterMisfire) would be the machine's, whatever clock this builder was created with.
        // A trigger from outside the shipped hierarchy is left alone; there is nothing to set on it.
        if (trig is TriggerBase triggerBase)
        {
            triggerBase.TimeProvider = timeProvider;
        }

        trig.CalendarName = calendarName;
        trig.Description = description;
        trig.StartTimeUtc = startTime;
        trig.EndTimeUtc = endTime;
        if (key is null)
        {
            key = new TriggerKey(Guid.NewGuid().ToString());
        }
        trig.Key = key;
        if (jobKey is not null)
        {
            trig.JobKey = jobKey;
        }
        trig.Priority = priority;

        if (!jobDataMap.IsEmpty)
        {
            trig.JobDataMap = jobDataMap;
        }

        trig.ExecutionGroup = executionGroup;

        // Assign unconditionally: a builder-built trigger fully defines the pin, so a definition
        // without WithPreferredNode clears a previously stored value when it replaces an existing
        // trigger (consistent with how ExecutionGroup is persisted). The value carries the
        // auto-claim flag, so GetTriggerBuilder() round-trips an auto-pin faithfully.
        trig.PreferredNode = preferredNode;

        return trig;
    }


    /// <summary>
    /// Use a <see cref="TriggerKey" /> with the given name and default group to
    /// identify the Trigger.
    /// </summary>
    /// <remarks>
    /// <para>If none of the 'withIdentity' methods are set on the TriggerBuilder,
    /// then a random, unique TriggerKey will be generated.</para>
    /// </remarks>
    /// <param name="name">the name element for the Trigger's TriggerKey</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="TriggerKey" />
    /// <seealso cref="ITrigger.Key" />
    public TriggerBuilder<TJob> WithIdentity(string name)
    {
        key = new TriggerKey(name);
        return this;
    }

    /// <summary>
    /// Use a TriggerKey with the given name and group to
    /// identify the Trigger.
    /// </summary>
    /// <remarks>
    /// <para>If none of the 'withIdentity' methods are set on the TriggerBuilder,
    /// then a random, unique TriggerKey will be generated.</para>
    /// </remarks>
    /// <param name="name">the name element for the Trigger's TriggerKey</param>
    /// <param name="group">the group element for the Trigger's TriggerKey</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="TriggerKey" />
    /// <seealso cref="ITrigger.Key" />
    public TriggerBuilder<TJob> WithIdentity(string name, string group)
    {
        key = new TriggerKey(name, group);
        return this;
    }

    /// <summary>
    /// Use the given TriggerKey to identify the Trigger.
    /// </summary>
    /// <remarks>
    /// <para>If none of the 'withIdentity' methods are set on the TriggerBuilder,
    /// then a random, unique TriggerKey will be generated.</para>
    /// </remarks>
    /// <param name="key">the TriggerKey for the Trigger to be built</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="TriggerKey" />
    /// <seealso cref="ITrigger.Key" />
    public TriggerBuilder<TJob> WithIdentity(TriggerKey key)
    {
        this.key = key;
        return this;
    }

    /// <summary>
    /// Set the given (human-meaningful) description of the Trigger.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="description">the description for the Trigger</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.Description" />
    public TriggerBuilder<TJob> WithDescription(string? description)
    {
        this.description = description;
        return this;
    }

    /// <summary>
    /// Set the execution group for the Trigger. Execution groups allow thread
    /// limits to be configured - per node or across the cluster - so that
    /// resource-intensive jobs do not saturate all available threads.
    /// </summary>
    /// <param name="executionGroup">the execution group name, or <see langword="null"/> to clear</param>
    /// <returns>the updated TriggerBuilder</returns>
    public TriggerBuilder<TJob> WithExecutionGroup(string? executionGroup)
    {
        if (string.IsNullOrWhiteSpace(executionGroup))
        {
            this.executionGroup = null;
        }
        else
        {
            executionGroup = executionGroup!.Trim();
            if (ExecutionLimits.IsReservedGroupName(executionGroup))
            {
                throw new ArgumentException(
                    $"Execution group name '{executionGroup}' is reserved for limits configuration.",
                    nameof(executionGroup));
            }
            this.executionGroup = executionGroup;
        }
        return this;
    }

    /// <summary>
    /// Set which cluster node the Trigger prefers to run on. When pinned, only that node executes
    /// this trigger, with automatic failover to other nodes while the preferred node is down.
    /// </summary>
    /// <param name="preferredNode">
    /// The pin: <see cref="Quartz.PreferredNode.None" /> to clear,
    /// <see cref="Quartz.PreferredNode.Auto" /> for automatic first-fire pinning, or
    /// <see cref="Quartz.PreferredNode.For" /> to name a node.
    /// </param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="Quartz.PreferredNode" />
    public TriggerBuilder<TJob> WithPreferredNode(PreferredNode preferredNode)
    {
        this.preferredNode = preferredNode;
        return this;
    }

    /// <summary>
    /// Set the Trigger's priority.  When more than one Trigger have the same
    /// fire time, the scheduler will fire the one with the highest priority
    /// first.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="priority">the priority for the Trigger</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="TriggerConstants.DefaultPriority" />
    /// <seealso cref="ITrigger.Priority" />
    public TriggerBuilder<TJob> WithPriority(int priority)
    {
        this.priority = priority;
        return this;
    }

    /// <summary>
    /// Set the name of the <see cref="ICalendar" /> that should be applied to this
    /// Trigger's schedule.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="calendarName">the name of the Calendar to reference.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ICalendar" />
    /// <seealso cref="ITrigger.CalendarName" />
    public TriggerBuilder<TJob> WithCalendarName(string? calendarName)
    {
        this.calendarName = calendarName;
        return this;
    }

    /// <summary>
    /// Set the time the Trigger should start at - the trigger may or may
    /// not fire at this time - depending upon the schedule configured for
    /// the Trigger.  However the Trigger will NOT fire before this time,
    /// regardless of the Trigger's schedule.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="startTimeUtc">the start time for the Trigger.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.StartTimeUtc" />
    /// <seealso cref="DateBuilder" />
    public TriggerBuilder<TJob> StartAt(DateTimeOffset startTimeUtc)
    {
        startTime = startTimeUtc;
        return this;
    }

    /// <summary>
    /// Set the time the Trigger should start at to the current moment -
    /// the trigger may or may not fire at this time - depending upon the
    /// schedule configured for the Trigger.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.StartTimeUtc" />
    public TriggerBuilder<TJob> StartNow()
    {
        startTime = timeProvider.GetUtcNow();
        return this;
    }

    /// <summary>
    /// Set the time at which the Trigger will no longer fire - even if it's
    /// schedule has remaining repeats.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="endTimeUtc">the end time for the Trigger.  If null, the end time is indefinite.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.EndTimeUtc" />
    /// <seealso cref="DateBuilder" />
    public TriggerBuilder<TJob> EndAt(DateTimeOffset? endTimeUtc)
    {
        endTime = endTimeUtc;
        return this;
    }

    /// <summary>
    /// Set the <see cref="IScheduleBuilder" /> that will be used to define the
    /// Trigger's schedule.
    /// </summary>
    /// <remarks>
    /// <para>The particular <see cref="IScheduleBuilder" /> used will dictate
    /// the concrete type of Trigger that is produced by the TriggerBuilder.</para>
    /// </remarks>
    /// <param name="scheduleBuilder">the QuartzSchedulerBuilder to use.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="IScheduleBuilder" />
    /// <seealso cref="SimpleScheduleBuilder" />
    /// <seealso cref="CronScheduleBuilder" />
    /// <seealso cref="CalendarIntervalScheduleBuilder" />
    public TriggerBuilder<TJob> WithSchedule(IScheduleBuilder scheduleBuilder)
    {
        this.scheduleBuilder = scheduleBuilder;
        return this;
    }

    /// <summary>
    /// Set the identity of the Job which should be fired by the produced
    /// Trigger.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobKey">the identity of the Job to fire.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobKey" />
    public TriggerBuilder<TJob> ForJob(JobKey jobKey)
    {
        this.jobKey = jobKey;
        return this;
    }

    /// <summary>
    /// Set the identity of the Job which should be fired by the produced
    /// Trigger - a <see cref="JobKey" /> will be produced with the given
    /// name and default group.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobName">the name of the job (in default group) to fire.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobKey" />
    public TriggerBuilder<TJob> ForJob(string jobName)
    {
        jobKey = new JobKey(jobName);
        return this;
    }

    /// <summary>
    /// Set the identity of the Job which should be fired by the produced
    /// Trigger - a <see cref="JobKey" /> will be produced with the given
    /// name and group.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobName">the name of the job to fire.</param>
    /// <param name="jobGroup">the group of the job to fire.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobKey" />
    public TriggerBuilder<TJob> ForJob(string jobName, string jobGroup)
    {
        jobKey = new JobKey(jobName, jobGroup);
        return this;
    }

    /// <summary>
    /// Set the identity of the Job which should be fired by the produced
    /// Trigger, by extracting the JobKey from the given job.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobDetail">the Job to fire.</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobKey" />
    public TriggerBuilder<TJob> ForJob(IJobDetail jobDetail)
    {
        JobKey k = jobDetail.Key;
        if (k.Name is null)
        {
            Throw.ArgumentException("The given job has not yet had a name assigned to it.");
        }

        // A typed builder's job data names properties of TJob, so pointing it at some other job would hand
        // that data to a job that has no such properties. Only this overload knows the job's type - pointed
        // at a key or a name there is nothing to check against. A builder for IJob names no properties, so
        // there is nothing to protect and the job's type is left untouched.
        if (typeof(TJob) != typeof(IJob)
            && jobDetail.JobType is { } detailJobType
            && detailJobType.TryResolve(out var resolvedJobType)
            && !typeof(TJob).IsAssignableFrom(resolvedJobType))
        {
            Throw.ArgumentException($"This builder configures a trigger for a {typeof(TJob)}, but the given job is a {resolvedJobType}.", nameof(jobDetail));
        }

        jobKey = k;
        return this;
    }

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// The value is stored as given. A persistent job store can only hold what its serializer
    /// round-trips, and AdoJobStore's <c>UseProperties</c> mode only strings.
    /// </remarks>
    /// <param name="key">the key to store the value under</param>
    /// <param name="value">the value to store</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    public TriggerBuilder<TJob> UsingJobData(string key, object? value)
    {
        jobDataMap[key] = value;
        return this;
    }

    /// <summary>
    /// Add a value to the Trigger's <see cref="JobDataMap" /> under the name of the job property it is
    /// meant to end up on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is how one job is given different inputs per trigger without spelling its property names:
    /// trigger data overrides job data in the map the job finally sees.
    /// </para>
    /// <para>
    /// It has to be a public settable property read directly off the job - a path through another property
    /// has nowhere to land, since the job factory sets properties on the job instance itself. Properties
    /// inherited from a base job are fine. Whether the property belongs to the job this trigger actually
    /// fires can only be checked when the trigger was pointed at the job with
    /// <see cref="ForJob(IJobDetail)" /> and that job's type resolves; pointed at a key, or at a job named
    /// by a type this process cannot load, the job type only names the properties.
    /// </para>
    /// <para>
    /// The value is stored in the property's own type, so an implicit widening at the call site is undone
    /// and a value that does not fit is rejected here. An enum property takes the enum's name.
    /// </para>
    /// <para>
    /// The same care applies as to any other job data: a persistent job store can only hold what its
    /// serializer round-trips, and AdoJobStore's <c>UseProperties</c> mode only strings. Nothing beyond
    /// enums is converted for you.
    /// </para>
    /// </remarks>
    /// <param name="jobProperty">an expression naming the job property, such as <c>job =&gt; job.Parameter</c></param>
    /// <param name="value">the value to bind to that property</param>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    public TriggerBuilder<TJob> UsingJobData<TValue>(Expression<Func<TJob, TValue>> jobProperty, TValue value)
    {
        var property = JobDataExpression.GetProperty(jobProperty);
        jobDataMap[property.Name] = JobDataExpression.NormalizeValue(property, value);
        return this;
    }

    /// <summary>
    /// Add the given key-value pair to the Trigger's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <returns>the updated TriggerBuilder</returns>
    /// <seealso cref="ITrigger.JobDataMap" />
    public TriggerBuilder<TJob> UsingJobData(JobDataMap newJobDataMap)
    {
        // add data from new map to existing map hereby overriding old values
        foreach (string k in newJobDataMap.Keys)
        {
            jobDataMap[k] = newJobDataMap[k];
        }

        return this;
    }

    internal void ClearDirty()
    {
        jobDataMap?.ClearDirtyFlag();
    }

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.WithIdentity(string name) => WithIdentity(name);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.WithIdentity(string name, string group) => WithIdentity(name, group);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.WithIdentity(TriggerKey key) => WithIdentity(key);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.WithDescription(string? description) => WithDescription(description);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.WithPriority(int priority) => WithPriority(priority);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.WithExecutionGroup(string? executionGroup) => WithExecutionGroup(executionGroup);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.WithPreferredNode(PreferredNode preferredNode) => WithPreferredNode(preferredNode);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.WithCalendarName(string? calendarName) => WithCalendarName(calendarName);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.StartAt(DateTimeOffset startTimeUtc) => StartAt(startTimeUtc);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.StartNow() => StartNow();

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.EndAt(DateTimeOffset? endTimeUtc) => EndAt(endTimeUtc);

    ITriggerConfigurator ITriggerConfigurator.WithSchedule(IScheduleBuilder scheduleBuilder) => WithSchedule(scheduleBuilder);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.WithSchedule(IScheduleBuilder scheduleBuilder) => WithSchedule(scheduleBuilder);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.ForJob(JobKey jobKey) => ForJob(jobKey);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.ForJob(string jobName) => ForJob(jobName);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.ForJob(string jobName, string jobGroup) => ForJob(jobName, jobGroup);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.ForJob(IJobDetail jobDetail) => ForJob(jobDetail);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.UsingJobData(JobDataMap newJobDataMap) => UsingJobData(newJobDataMap);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.UsingJobData(string key, object? value) => UsingJobData(key, value);

    ITriggerConfigurator<TJob> ITriggerConfigurator<TJob>.UsingJobData<TValue>(Expression<Func<TJob, TValue>> jobProperty, TValue value) => UsingJobData(jobProperty, value);
}