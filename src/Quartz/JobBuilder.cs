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

using Quartz.Impl;
using Quartz.Util;

namespace Quartz;

/// <summary>
/// JobBuilder is used to instantiate <see cref="IJobDetail" />s.
/// </summary>
/// <remarks>
/// <para>
/// The builder will always try to keep itself in a valid state, with
/// reasonable defaults set for calling Build() at any point.  For instance
/// if you do not invoke <i>WithIdentity(..)</i> a job name will be generated
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
///         IJobDetail job = JobBuilder.Create&lt;MyJob&gt;()
///             .WithIdentity("myJob")
///             .Build();
///
///         ITrigger trigger = TriggerBuilder.Create()
///             .WithIdentity("myTrigger", "myTriggerGroup")
///             .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
///             .StartAt(DateTimeOffset.UtcNow.AddMinutes(10))
///             .Build();
///
///         scheduler.scheduleJob(job, trigger);
/// </code>
/// </remarks>
/// <seealso cref="TriggerBuilder" />
/// <seealso cref="DateBuilder" />
/// <seealso cref="IJobDetail" />
public static class JobBuilder
{
    /// <summary>
    /// Create a JobBuilder with which to define a <see cref="IJobDetail" />.
    /// </summary>
    /// <returns>a new JobBuilder</returns>
    public static JobBuilder<IJob> Create()
    {
        return new JobBuilder<IJob>();
    }

    /// <summary>
    /// Create a JobBuilder for a known job type, with which to define a <see cref="IJobDetail" />.
    /// </summary>
    /// <remarks>
    /// The job type stays with the builder, so job data can name the job's properties rather than spell
    /// their keys.
    /// </remarks>
    /// <returns>a new JobBuilder</returns>
    public static JobBuilder<T> Create<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)] T>() where T : IJob
    {
        var b = new JobBuilder<T>();
        b.OfType<T>();
        return b;
    }
}

/// <summary>
/// JobBuilder is used to instantiate <see cref="IJobDetail" />s for a known job type.
/// </summary>
/// <remarks>
/// Knowing the job type is what lets <see cref="UsingJobData{TValue}" /> take the job's property instead of
/// its key. <c>JobBuilder.Create()</c> gives a builder for <see cref="IJob" />, which has no properties to
/// name; <c>JobBuilder.Create&lt;TJob&gt;()</c> gives one that does.
/// </remarks>
/// <seealso cref="JobBuilder" />
public sealed class JobBuilder<TJob> : IJobConfigurator<TJob> where TJob : IJob
{
    private JobKey? _key;
    private string? _description;
    private JobType? _jobType;
    private bool _durability;
    private bool _shouldRecover;
    private bool? _concurrentExecutionDisallowed;
    private bool? _persistJobDataAfterExecution;

    private JobDataMap jobDataMap = new JobDataMap();

    /// <summary>
    /// The key that identifies the job uniquely, or <see langword="null" /> when none was set.
    /// </summary>
    /// <remarks>
    /// Readable so that code building a job and something that has to agree with it — a trigger, a
    /// registration — can tell an identity the caller chose from the one <see cref="Build" /> would
    /// generate. Reading it after <c>Build</c> still reports what the builder was told, not what was
    /// generated.
    /// </remarks>
    public JobKey? Key => _key;

    internal JobBuilder()
    {
    }

    /// <summary>
    /// Produce the <see cref="IJobDetail" /> instance defined by this JobBuilder.
    /// </summary>
    /// <returns>the defined JobDetail.</returns>
    public IJobDetail Build()
    {
        if (_jobType is null)
        {
            Throw.InvalidOperationException("Job type has not been set");
        }

        var concurrentExecutionDisallowed = _concurrentExecutionDisallowed;
        var persistJobDataAfterExecution = _persistJobDataAfterExecution;

        // When the user specified a job type, we can deduce the values for
        // ConcurrentExecutionDisallowed and PersistJobDataAfterExecution if
        // no explicit values were specified. The JobType resolves itself, so a type it was given directly
        // is the one we get back rather than whatever its name happens to bind to.
        if (_jobType.TryResolve(out var resolvedJobType))
        {
            // A typed builder that was pointed at some other job through OfType would hand its job data to
            // a job that has no such properties. Nothing else can make these disagree, and a builder for
            // IJob names no properties, so it has nothing to protect.
            if (typeof(TJob) != typeof(IJob) && !typeof(TJob).IsAssignableFrom(resolvedJobType))
            {
                Throw.InvalidOperationException($"This builder configures a {typeof(TJob)}, but the job being built is a {resolvedJobType}.");
            }

            if (!_concurrentExecutionDisallowed.HasValue)
            {
                concurrentExecutionDisallowed = JobTypeInformation.GetOrCreate(resolvedJobType).ConcurrentExecutionDisallowed;
            }

            if (!persistJobDataAfterExecution.HasValue)
            {
                persistJobDataAfterExecution = JobTypeInformation.GetOrCreate(resolvedJobType).PersistJobDataAfterExecution;
            }
        }

        return new JobDetailImpl(Key ?? new JobKey(Guid.NewGuid().ToString()),
            _jobType,
            _description,
            _durability,
            _shouldRecover,
            jobDataMap.IsEmpty ? null : jobDataMap,
            concurrentExecutionDisallowed,
            persistJobDataAfterExecution);
    }


    /// <summary>
    /// Instructs the <see cref="IScheduler" /> whether or not concurrent execution of the job should be disallowed.
    /// </summary>
    /// <param name="concurrentExecutionDisallowed">Indicates whether or not concurrent execution of the job should be disallowed.</param>
    /// <returns>
    /// The updated <see cref="JobBuilder"/>.
    /// </returns>
    /// <remarks>
    /// If not explicitly set, concurrent execution of a job is only disallowed it either the <see cref="IJobDetail.JobType"/> itself,
    /// one of its ancestors or one of the interfaces that it implements, is annotated with <see cref="DisallowConcurrentExecutionAttribute"/>.
    /// </remarks>
    /// <seealso cref="DisallowConcurrentExecutionAttribute"/>
    public JobBuilder<TJob> DisallowConcurrentExecution(bool concurrentExecutionDisallowed = true)
    {
        _concurrentExecutionDisallowed = concurrentExecutionDisallowed;
        return this;
    }

    /// <summary>
    /// Instructs the <see cref="IScheduler" /> whether or not job data should be re-stored when execution of the job completes.
    /// </summary>
    /// <param name="persistJobDataAfterExecution">Indicates whether or not job data should be re-stored when execution of the job completes.</param>
    /// <returns>
    /// The updated <see cref="JobBuilder"/>.
    /// </returns>
    /// <remarks>
    /// If not explicitly set, job data is only re-stored it either the <see cref="IJobDetail.JobType"/> itself, one of
    /// its ancestors or one of the interfaces that it implements, is annotated with <see cref="PersistJobDataAfterExecutionAttribute"/>.
    /// </remarks>
    /// <seealso cref="PersistJobDataAfterExecutionAttribute"/>
    public JobBuilder<TJob> PersistJobDataAfterExecution(bool persistJobDataAfterExecution = true)
    {
        _persistJobDataAfterExecution = persistJobDataAfterExecution;
        return this;
    }

    /// <summary>
    /// Use a <see cref="JobKey" /> with the given name and default group to
    /// identify the JobDetail.
    /// </summary>
    /// <remarks>
    /// <para>If none of the 'withIdentity' methods are set on the JobBuilder,
    /// then a random, unique JobKey will be generated.</para>
    /// </remarks>
    /// <param name="name">the name element for the Job's JobKey</param>
    /// <returns>the updated JobBuilder</returns>
    /// <seealso cref="JobKey" />
    /// <seealso cref="IJobDetail.Key" />
    public JobBuilder<TJob> WithIdentity(string name)
    {
        _key = new JobKey(name);
        return this;
    }

    /// <summary>
    /// Use a <see cref="JobKey" /> with the given name and group to
    /// identify the JobDetail.
    /// </summary>
    /// <remarks>
    /// <para>If none of the 'withIdentity' methods are set on the JobBuilder,
    /// then a random, unique JobKey will be generated.</para>
    /// </remarks>
    /// <param name="name">the name element for the Job's JobKey</param>
    /// <param name="group"> the group element for the Job's JobKey</param>
    /// <returns>the updated JobBuilder</returns>
    /// <seealso cref="JobKey" />
    /// <seealso cref="IJobDetail.Key" />
    public JobBuilder<TJob> WithIdentity(string name, string group)
    {
        _key = new JobKey(name, group);
        return this;
    }

    /// <summary>
    /// Use a <see cref="JobKey" /> to identify the JobDetail.
    /// </summary>
    /// <remarks>
    /// <para>If none of the 'withIdentity' methods are set on the JobBuilder,
    /// then a random, unique JobKey will be generated.</para>
    /// </remarks>
    /// <param name="key">the Job's JobKey</param>
    /// <returns>the updated JobBuilder</returns>
    /// <seealso cref="JobKey" />
    /// <seealso cref="IJobDetail.Key" />
    public JobBuilder<TJob> WithIdentity(JobKey key)
    {
        this._key = key;
        return this;
    }

    /// <summary>
    /// Set the given (human-meaningful) description of the Job.
    /// </summary>
    /// <param name="description"> the description for the Job</param>
    /// <returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.Description" />
    public JobBuilder<TJob> WithDescription(string? description)
    {
        this._description = description;
        return this;
    }

    /// <summary>
    /// Set the JobType by name
    /// </summary>
    /// <param name="typeName">the Type name</param>
    /// <returns>the updated JobBuilder</returns>
    public JobBuilder<TJob> OfType(string typeName)
    {
        _jobType = typeName;
        return this;
    }

    /// <summary>
    /// Set the job type to one that already knows how to resolve the name it carries.
    /// </summary>
    /// <remarks>
    /// A name read back out of a job store may be spelled the way an older Quartz wrote it, and only the
    /// scheduler's type load helper knows what such a spelling means today. Handing the resolution over
    /// rather than the resolved type keeps the stored name as it was stored.
    /// </remarks>
    /// <param name="jobType">the job type, with whatever resolution it was constructed with</param>
    /// <returns>the updated JobBuilder</returns>
    internal JobBuilder<TJob> OfType(JobType jobType)
    {
        _jobType = jobType;
        return this;
    }

    /// <summary>
    /// Set the class which will be instantiated and executed when a
    /// Trigger fires that is associated with this JobDetail.
    /// </summary>
    /// <returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobType" />
    public JobBuilder<TJob> OfType<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)] T>() where T : TJob
    {
        return OfType(typeof(T));
    }

    /// <summary>
    /// Set the class which will be instantiated and executed when a
    /// Trigger fires that is associated with this JobDetail.
    /// </summary>
    /// <returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobType" />
    public JobBuilder<TJob> OfType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
    {
        // The type is known here, so the mismatch is reported at the call that caused it rather than at
        // the build - which, configured through the container, happens somewhere else entirely.
        if (typeof(TJob) != typeof(IJob) && !typeof(TJob).IsAssignableFrom(type))
        {
            Throw.ArgumentException($"This builder configures a {typeof(TJob)}, but {type} is not one.", nameof(type));
        }

        _jobType = new JobType(type);
        return this;
    }

    /// <summary>
    /// Instructs the <see cref="IScheduler" /> whether or not the job
    /// should be re-executed if a 'recovery' or 'fail-over' situation is
    /// encountered.
    /// </summary>
    /// <remarks>
    /// If not explicitly set, the default value is <see langword="false" />.
    /// </remarks>
    /// <param name="shouldRecover"></param>
    /// <returns>the updated JobBuilder</returns>
    public JobBuilder<TJob> RequestRecovery(bool shouldRecover = true)
    {
        this._shouldRecover = shouldRecover;
        return this;
    }

    /// <summary>
    /// Whether or not the job should remain stored after it is
    /// orphaned (no <see cref="ITrigger" />s point to it).
    /// </summary>
    /// <remarks>
    /// If not explicitly set, the default value is <see langword="false" />.
    /// </remarks>
    /// <param name="durability">the value to set for the durability property.</param>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.Durable" />
    public JobBuilder<TJob> StoreDurably(bool durability = true)
    {
        this._durability = durability;
        return this;
    }

    /// <summary>
    /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// The value is stored as given. A persistent job store can only hold what its serializer
    /// round-trips, and AdoJobStore's <c>UseProperties</c> mode only strings.
    /// </remarks>
    /// <param name="key">the key to store the value under</param>
    /// <param name="value">the value to store</param>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    public JobBuilder<TJob> UsingJobData(string key, object? value)
    {
        jobDataMap[key] = value;
        return this;
    }

    /// <summary>
    /// Add a value to the JobDetail's <see cref="JobDataMap" /> under the name of the job property it is
    /// meant to end up on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property is named rather than spelled, so the key cannot be mistyped and the value cannot be of
    /// the wrong type. It has to be a public settable property read directly off the job - a path through
    /// another property has nowhere to land, since the job factory sets properties on the job instance
    /// itself - and it is rejected here rather than dropped silently when the job runs. Properties
    /// inherited from a base job are fine.
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
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    public JobBuilder<TJob> UsingJobData<TValue>(Expression<Func<TJob, TValue>> jobProperty, TValue value)
    {
        var property = JobDataExpression.GetProperty(jobProperty);
        jobDataMap[property.Name] = JobDataExpression.NormalizeValue(property, value);
        return this;
    }

    /// <summary>
    /// Add all the data from the given <see cref="JobDataMap" /> to the
    /// <see cref="IJobDetail" />'s <see cref="JobDataMap" />.
    /// </summary>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    public JobBuilder<TJob> UsingJobData(JobDataMap newJobDataMap)
    {
        if (newJobDataMap is null)
        {
            Throw.ArgumentNullException(nameof(newJobDataMap));
        }
        foreach (var pair in newJobDataMap)
        {
            jobDataMap[pair.Key] = pair.Value;
        }
        return this;
    }

    /// <summary>
    /// Replace the <see cref="IJobDetail" />'s <see cref="JobDataMap" /> with the given
    /// <see cref="JobDataMap" />, discarding whatever the builder held.
    /// </summary>
    /// <remarks>
    /// Internal because replacing is only ever what a job store rebuilding a stored job wants:
    /// everything else is adding to what the builder already carries, which
    /// <see cref="UsingJobData(JobDataMap)" /> does.
    /// </remarks>
    internal JobBuilder<TJob> ReplaceJobData(JobDataMap newJobDataMap)
    {
        if (newJobDataMap is null)
        {
            Throw.ArgumentNullException(nameof(newJobDataMap));
        }
        jobDataMap = newJobDataMap;
        return this;
    }

    IJobConfigurator<TJob> IJobConfigurator<TJob>.WithIdentity(string name) => WithIdentity(name);

    IJobConfigurator<TJob> IJobConfigurator<TJob>.WithIdentity(string name, string group) => WithIdentity(name, group);

    IJobConfigurator<TJob> IJobConfigurator<TJob>.WithIdentity(JobKey key) => WithIdentity(key);

    IJobConfigurator<TJob> IJobConfigurator<TJob>.WithDescription(string? description) => WithDescription(description);

    IJobConfigurator<TJob> IJobConfigurator<TJob>.RequestRecovery(bool shouldRecover) => RequestRecovery(shouldRecover);

    IJobConfigurator<TJob> IJobConfigurator<TJob>.StoreDurably(bool durability) => StoreDurably(durability);

    IJobConfigurator<TJob> IJobConfigurator<TJob>.UsingJobData(string key, object? value) => UsingJobData(key, value);

    IJobConfigurator<TJob> IJobConfigurator<TJob>.UsingJobData<TValue>(Expression<Func<TJob, TValue>> jobProperty, TValue value) => UsingJobData(jobProperty, value);

    IJobConfigurator<TJob> IJobConfigurator<TJob>.UsingJobData(JobDataMap newJobDataMap) => UsingJobData(newJobDataMap);

    IJobConfigurator<TJob> IJobConfigurator<TJob>.DisallowConcurrentExecution(bool concurrentExecutionDisallowed) => DisallowConcurrentExecution(concurrentExecutionDisallowed);

    IJobConfigurator<TJob> IJobConfigurator<TJob>.PersistJobDataAfterExecution(bool persistJobDataAfterExecution) => PersistJobDataAfterExecution(persistJobDataAfterExecution);
}