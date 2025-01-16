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

using Quartz.Impl;

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
///             .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
///             .StartAt(DateBuilder.FutureDate(10, IntervalUnit.Minute))
///             .Build();
///
///         scheduler.scheduleJob(job, trigger);
/// </code>
/// </remarks>
/// <seealso cref="TriggerBuilder" />
/// <seealso cref="DateBuilder" />
/// <seealso cref="IJobDetail" />
public sealed class JobBuilder : IJobConfigurator
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
    /// The key that identifies the job uniquely.
    /// </summary>
    internal JobKey? Key => _key;

    private JobBuilder()
    {
    }

    /// <summary>
    /// Create a JobBuilder with which to define a <see cref="IJobDetail" />.
    /// </summary>
    /// <returns>a new JobBuilder</returns>
    public static JobBuilder Create()
    {
        return new JobBuilder();
    }

    /// <summary>
    /// Create a JobBuilder with which to define a <see cref="IJobDetail" />,
    /// and set the class name of the job to be executed.
    /// </summary>
    /// <returns>a new JobBuilder</returns>
    public static JobBuilder Create([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] Type jobType)
    {
        JobBuilder b = new JobBuilder();
        b.OfType(jobType);
        return b;
    }

    /// <summary>
    /// Create a JobBuilder with which to define a <see cref="IJobDetail" />,
    /// and set the class name of the job to be executed.
    /// </summary>
    /// <returns>a new JobBuilder</returns>
    public static JobBuilder Create<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>() where T : IJob
    {
        JobBuilder b = new JobBuilder();
        b.OfType<T>();
        return b;
    }

    /// <summary>
    /// Produce the <see cref="IJobDetail" /> instance defined by this JobBuilder.
    /// </summary>
    /// <returns>the defined JobDetail.</returns>
    public IJobDetail Build()
    {
        if (_jobType is null)
        {
            ThrowHelper.ThrowInvalidOperationException("Job type has not been set");
        }

        var concurrentExecutionDisallowed = _concurrentExecutionDisallowed;
        var persistJobDataAfterExecution = _persistJobDataAfterExecution;

        // When the user specified a job type, we can deduce the values for
        // ConcurrentExecutionDisallowed and PersistJobDataAfterExecution if
        // no explicit values were specified
        var resolvedJobType = Type.GetType(_jobType.FullName);
        if (resolvedJobType is not null)
        {
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
    public JobBuilder DisallowConcurrentExecution(bool concurrentExecutionDisallowed = true)
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
    public JobBuilder PersistJobDataAfterExecution(bool persistJobDataAfterExecution = true)
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
    public JobBuilder WithIdentity(string name)
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
    public JobBuilder WithIdentity(string name, string group)
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
    public JobBuilder WithIdentity(JobKey key)
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
    public JobBuilder WithDescription(string? description)
    {
        this._description = description;
        return this;
    }

    /// <summary>
    /// Set the JobType by name
    /// </summary>
    /// <param name="typeName">the Type name</param>
    /// <returns>the updated JobBuilder</returns>
    public JobBuilder OfType(string typeName)
    {
        _jobType = typeName;
        return this;
    }

    /// <summary>
    /// Set the class which will be instantiated and executed when a
    /// Trigger fires that is associated with this JobDetail.
    /// </summary>
    /// <returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobType" />
    public JobBuilder OfType<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
    {
        return OfType(typeof(T));
    }

    /// <summary>
    /// Set the class which will be instantiated and executed when a
    /// Trigger fires that is associated with this JobDetail.
    /// </summary>
    /// <returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobType" />
    public JobBuilder OfType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] Type type)
    {
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
    public JobBuilder RequestRecovery(bool shouldRecover = true)
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
    public JobBuilder StoreDurably(bool durability = true)
    {
        this._durability = durability;
        return this;
    }

    /// <summary>
    /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
    /// </summary>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    public JobBuilder UsingJobData(string key, string? value)
    {
        jobDataMap.Put(key, value);
        return this;
    }

    /// <summary>
    /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
    /// </summary>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    public JobBuilder UsingJobData(string key, int value)
    {
        jobDataMap.Put(key, value);
        return this;
    }

    /// <summary>
    /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
    /// </summary>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    public JobBuilder UsingJobData(string key, long value)
    {
        jobDataMap.Put(key, value);
        return this;
    }

    /// <summary>
    /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
    /// </summary>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    public JobBuilder UsingJobData(string key, float value)
    {
        jobDataMap.Put(key, value);
        return this;
    }

    /// <summary>
    /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
    /// </summary>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    public JobBuilder UsingJobData(string key, double value)
    {
        jobDataMap.Put(key, value);
        return this;
    }

    /// <summary>
    /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
    /// </summary>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    public JobBuilder UsingJobData(string key, bool value)
    {
        jobDataMap.Put(key, value);
        return this;
    }

    /// <summary>
    /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
    /// </summary>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    public JobBuilder UsingJobData(string key, Guid value)
    {
        jobDataMap.Put(key, value);
        return this;
    }

    /// <summary>
    /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
    /// </summary>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    public JobBuilder UsingJobData(string key, char value)
    {
        jobDataMap.Put(key, value);
        return this;
    }

    /// <summary>
    /// Add all the data from the given <see cref="JobDataMap" /> to the
    /// <see cref="IJobDetail" />'s <see cref="JobDataMap" />.
    /// </summary>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    public JobBuilder UsingJobData(JobDataMap newJobDataMap)
    {
        if (newJobDataMap is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(newJobDataMap));
        }
        jobDataMap.PutAll(newJobDataMap);
        return this;
    }

    /// <summary>
    /// Replace the <see cref="IJobDetail" />'s <see cref="JobDataMap" /> with the
    /// given <see cref="JobDataMap" />.
    /// </summary>
    /// <param name="newJobDataMap"></param>
    /// <returns></returns>
    public JobBuilder SetJobData(JobDataMap newJobDataMap)
    {
        if (newJobDataMap is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(newJobDataMap));
        }
        jobDataMap = newJobDataMap;
        return this;
    }
}