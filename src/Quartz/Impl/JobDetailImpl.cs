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

using System.Collections.Concurrent;

using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// Model for saving attribute's information in cache
/// Used in key/value pair with <see cref="IJobDetail.JobType"/> as a value
/// and show presence of attributes of specified type
/// </summary>
/// <seealso cref="DisallowConcurrentExecutionAttribute"/>
/// <seealso cref="PersistJobDataAfterExecutionAttribute"/>
internal sealed class JobTypeInformation
{
    private static readonly ConcurrentDictionary<Type, JobTypeInformation> jobTypeCache = new ConcurrentDictionary<Type, JobTypeInformation>();

    public JobTypeInformation(bool concurrentExecutionDisallowed, bool persistJobDataAfterExecution)
    {
        ConcurrentExecutionDisallowed = concurrentExecutionDisallowed;
        PersistJobDataAfterExecution = persistJobDataAfterExecution;
    }

    /// <summary>
    /// Return information about JobType as an instance
    /// </summary>
    /// <param name="jobType">The type for which information will be searched</param>
    /// <returns>
    /// An <see cref="JobTypeInformation"/> object that describe specified type
    /// </returns>
    public static JobTypeInformation GetOrCreate(Type jobType)
    {
        return jobTypeCache.GetOrAdd(jobType, jt => Create(jt));
    }

    private static JobTypeInformation Create(Type jobType)
    {
        var concurrentExecutionDisallowed = ObjectUtils.IsAnyInterfaceAttributePresent(jobType, typeof(DisallowConcurrentExecutionAttribute));
        var persistJobDataAfterExecution = ObjectUtils.IsAnyInterfaceAttributePresent(jobType, typeof(PersistJobDataAfterExecutionAttribute));

        return new JobTypeInformation(concurrentExecutionDisallowed, persistJobDataAfterExecution);
    }

    public bool ConcurrentExecutionDisallowed { get; }
    public bool PersistJobDataAfterExecution { get; }
}

/// <summary>
/// Conveys the detail properties of a given job instance.
/// </summary>
/// <remarks>
/// Quartz does not store an actual instance of a <see cref="IJob" /> type, but
/// instead allows you to define an instance of one, through the use of a <see cref="IJobDetail" />.
/// <para>
/// <see cref="IJob" />s have a name and group associated with them, which
/// should uniquely identify them within a single <see cref="IScheduler" />.
/// </para>
/// <para>
/// <see cref="ITrigger" /> s are the 'mechanism' by which <see cref="IJob" /> s
/// are scheduled. Many <see cref="ITrigger" /> s can point to the same <see cref="IJob" />,
/// but a single <see cref="ITrigger" /> can only point to one <see cref="IJob" />.
/// </para>
/// </remarks>
/// <seealso cref="IJob" />
/// <seealso cref="DisallowConcurrentExecutionAttribute"/>
/// <seealso cref="PersistJobDataAfterExecutionAttribute"/>
/// <seealso cref="JobDataMap"/>
/// <seealso cref="ITrigger"/>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
internal sealed class JobDetailImpl : IJobDetail
{
    private string name = null!;
    private string group = SchedulerConstants.DefaultGroup;
    private string? description;
    private JobDataMap jobDataMap = null!;
    private readonly Type jobType = null!;
    private bool? disallowConcurrentExecution;
    private bool? persistJobDataAfterExecution;

    [NonSerialized] // we have the key in string fields
    private JobKey key = null!;

    /// <summary>
    /// Create a <see cref="IJobDetail" /> with the given name, default group, and
    /// the default settings of all the other properties.
    /// If <see langword="null" />, SchedulerConstants.DefaultGroup will be used.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// If name is null or empty, or the group is an empty string.
    /// </exception>
    public JobDetailImpl(string name, Type jobType) : this(name, SchedulerConstants.DefaultGroup, jobType)
    {
    }

    /// <summary>
    /// Create a <see cref="IJobDetail" /> with the given name, and group, and
    /// the default settings of all the other properties.
    /// If <see langword="null" />, SchedulerConstants.DefaultGroup will be used.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// If name is null or empty, or the group is an empty string.
    /// </exception>
    public JobDetailImpl(string name, string group, Type jobType)
    {
        Name = name;
        Group = group;
        JobType = new JobType(jobType);
    }

    /// <summary>
    /// Create a <see cref="IJobDetail" /> with the given name, and group, and
    /// the given settings of all the other properties.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="group">if <see langword="null" />, SchedulerConstants.DefaultGroup will be used.</param>
    /// <param name="jobType">Type of the job.</param>
    /// <param name="isDurable">if set to <c>true</c>, job will be durable.</param>
    /// <param name="requestsRecovery">if set to <c>true</c>, job will request recovery.</param>
    /// <exception cref="ArgumentException">
    /// ArgumentException if name is null or empty, or the group is an empty string.
    /// </exception>
    public JobDetailImpl(string name, string group, Type jobType, bool isDurable, bool requestsRecovery)
    {
        Name = name;
        Group = group;
        JobType = new JobType(jobType);
        Durable = isDurable;
        RequestsRecovery = requestsRecovery;
    }

    /// <summary>
    /// Create a <see cref="IJobDetail" /> with the given name, and group, and
    /// the given settings of all the other properties.
    /// </summary>
    /// <param name="key">The key of the job.</param>
    /// <param name="jobType">Type of the job.</param>
    /// <param name="description">The description given to the <see cref="IJob" /> instance by its creator.</param>
    /// <param name="isDurable">if set to <c>true</c>, job will be durable.</param>
    /// <param name="requestsRecovery">if set to <c>true</c>, job will request recovery.</param>
    /// <param name="jobDataMap">The data that is associated with the <see cref="IJob" />.</param>
    /// <param name="disallowConcurrentExecution">Indicates whether or not concurrent execution of the job should be disallowed.</param>
    /// <param name="persistJobDataAfterExecution">Indicates whether or not job data should re-stored when execution of the job completes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    internal JobDetailImpl(JobKey key,
        JobType jobType,
        string? description,
        bool isDurable,
        bool requestsRecovery,
        JobDataMap? jobDataMap,
        bool? disallowConcurrentExecution,
        bool? persistJobDataAfterExecution)
    {
        Key = key;
        JobType = jobType;
        Description = description;
        Durable = isDurable;
        RequestsRecovery = requestsRecovery;
        this.disallowConcurrentExecution = disallowConcurrentExecution;
        this.persistJobDataAfterExecution = persistJobDataAfterExecution;

        if (jobDataMap is not null)
        {
            this.jobDataMap = jobDataMap;
        }
    }

    /// <summary>
    /// Get or sets the name of this <see cref="IJob" />.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// if name is null or empty.
    /// </exception>
    public string Name
    {
        get => name;

        private set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ThrowHelper.ThrowArgumentException("Job name cannot be empty.");
            }

            name = value;
        }
    }

    /// <summary>
    /// Get or sets the group of this <see cref="IJob" />.
    /// If <see langword="null" />, <see cref="SchedulerConstants.DefaultGroup" /> will be used.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// If the group is an empty string.
    /// </exception>
    public string Group
    {
        get => group;
        private set
        {
            if (value is not null && value.Trim().Length == 0)
            {
                ThrowHelper.ThrowArgumentException("Group name cannot be empty.");
            }

            if (value is null)
            {
                value = SchedulerConstants.DefaultGroup;
            }

            group = value;
        }
    }

    /// <summary>
    /// Returns the 'full name' of the <see cref="ITrigger" /> in the format
    /// "group.name".
    /// </summary>
    public string FullName => group + "." + name;

    /// <summary>
    /// Gets the key.
    /// </summary>
    /// <value>The key.</value>
    public JobKey Key
    {
        get
        {
            if (key is null)
            {
                if (Name is null)
                {
                    return null!;
                }
                key = new JobKey(Name, Group);
            }

            return key;
        }
        internal set
        {
            if (value is null)
            {
                ThrowHelper.ThrowArgumentNullException(nameof(value));
            }

            Name = value.Name;
            Group = value.Group;
            key = value;
        }
    }

    /// <summary>
    /// Get or set the description given to the <see cref="IJob" /> instance by its
    /// creator (if any).
    /// </summary>
    /// <remarks>
    /// May be useful for remembering/displaying the purpose of the job, though the
    /// description has no meaning to Quartz.
    /// </remarks>
    public string? Description
    {
        get => description;
        private set => description = value;
    }

    public JobType JobType { get; private set; }

    /// <summary>
    /// Get or set the <see cref="JobDataMap" /> that is associated with the <see cref="IJob" />.
    /// </summary>
    public JobDataMap JobDataMap
    {
        get
        {
            if (jobDataMap is null)
            {
                jobDataMap = new JobDataMap();
            }

            return jobDataMap;
        }

        private set => jobDataMap = value;
    }

    /// <summary>
    /// Set whether or not the <see cref="IScheduler" /> should re-Execute
    /// the <see cref="IJob" /> if a 'recovery' or 'fail-over' situation is
    /// encountered.
    /// <para>
    /// If not explicitly set, the default value is <see langword="false" />.
    /// </para>
    /// </summary>
    /// <seealso cref="IJobExecutionContext.Recovering" />
    public bool RequestsRecovery { get; private set; }

    /// <summary>
    /// Whether or not the <see cref="IJob" /> should remain stored after it is
    /// orphaned (no <see cref="ITrigger" />s point to it).
    /// <para>
    /// If not explicitly set, the default value is <see langword="false" />.
    /// </para>
    /// </summary>
    /// <returns>
    /// <see langword="true" /> if the Job should remain persisted after
    /// being orphaned.
    /// </returns>
    public bool Durable { get; private set; }

    /// <summary>
    /// Gets a value indicating whether job data should be re-stored when execution of the job completes.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if job data should be re-stored when execution of the job completes; otherwise,
    /// <see langword="false"/>.
    /// </value>
    /// <seealso cref="PersistJobDataAfterExecutionAttribute"/>
    public bool PersistJobDataAfterExecution
    {
        get
        {
            if (!persistJobDataAfterExecution.HasValue)
            {
                persistJobDataAfterExecution = JobTypeInformation.GetOrCreate(JobType).PersistJobDataAfterExecution;
            }

            return persistJobDataAfterExecution.GetValueOrDefault();
        }
    }

    /// <summary>
    /// Gets a value indicating whether concurrent execution of the job should be disallowed.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if concurrent execution is disallowed; otherwise, <see langword="false"/>.
    /// </value>
    /// <seealso cref="DisallowConcurrentExecutionAttribute"/>
    public bool ConcurrentExecutionDisallowed
    {
        get
        {
            if (!disallowConcurrentExecution.HasValue)
            {
                disallowConcurrentExecution = JobTypeInformation.GetOrCreate(JobType).ConcurrentExecutionDisallowed;
            }

            return disallowConcurrentExecution.GetValueOrDefault();
        }
    }

    /// <summary>
    /// Validates whether the properties of the <see cref="IJobDetail" /> are
    /// valid for submission into a <see cref="IScheduler" />.
    /// </summary>
    public void Validate()
    {
        if (name is null)
        {
            ThrowHelper.ThrowSchedulerException("Job's name cannot be null");
        }

        if (group is null)
        {
            ThrowHelper.ThrowSchedulerException("Job's group cannot be null");
        }

        if (jobType is null)
        {
            ThrowHelper.ThrowSchedulerException("Job's class cannot be null");
        }
    }

    /// <summary>
    /// Return a simple string representation of this object.
    /// </summary>
    public override string ToString()
        => $"JobDetail '{FullName}':  jobType: '{JobType?.FullName} persistJobDataAfterExecution: {PersistJobDataAfterExecution} concurrentExecutionDisallowed: {ConcurrentExecutionDisallowed} isDurable: {Durable} requestsRecovers: {RequestsRecovery}";

    /// <summary>
    /// Creates a new object that is a copy of the current instance.
    /// </summary>
    /// <returns>
    /// A new object that is a copy of this instance.
    /// </returns>
    public IJobDetail Clone()
    {
        var copy = (JobDetailImpl) MemberwiseClone();
        if (jobDataMap is not null)
        {
            copy.jobDataMap = (JobDataMap) jobDataMap.Clone();
        }
        return copy;
    }

    /// <summary>
    /// Determines whether the specified detail is equal to this instance.
    /// </summary>
    /// <param name="detail">The detail to examine.</param>
    /// <returns>
    /// 	<c>true</c> if the specified detail is equal; otherwise, <c>false</c>.
    /// </returns>
    internal bool IsEqual(JobDetailImpl detail)
    {
        //doesn't consider job's saved data,
        //durability etc
        return detail is not null && detail.Name == Name && detail.Group == Group && detail.JobType.Equals(JobType);
    }

    /// <summary>
    /// Determines whether the specified <see cref="System.Object"/> is equal to the current <see cref="System.Object"/>.
    /// </summary>
    /// <param name="obj">The <see cref="System.Object"/> to compare with the current <see cref="System.Object"/>.</param>
    /// <returns>
    /// 	<see langword="true"/> if the specified <see cref="System.Object"/> is equal to the
    /// current <see cref="System.Object"/>; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj)
    {
        if (!(obj is JobDetailImpl jd))
        {
            return false;
        }

        return IsEqual(jd);
    }

    /// <summary>
    /// Checks equality between given job detail and this instance.
    /// </summary>
    /// <param name="detail">The detail to compare this instance with.</param>
    /// <returns></returns>
    public bool Equals(JobDetailImpl detail)
    {
        return IsEqual(detail);
    }

    /// <summary>
    /// Serves as a hash function for a particular type, suitable
    /// for use in hashing algorithms and data structures like a hash table.
    /// </summary>
    /// <returns>
    /// A hash code for the current <see cref="System.Object"/>.
    /// </returns>
    public override int GetHashCode()
    {
        return FullName.GetHashCode();
    }

    public JobBuilder GetJobBuilder()
    {
        return JobBuilder.Create()
            .OfType(JobType)
            .RequestRecovery(RequestsRecovery)
            .StoreDurably(Durable)
            .UsingJobData(JobDataMap)
            .DisallowConcurrentExecution(ConcurrentExecutionDisallowed)
            .PersistJobDataAfterExecution(PersistJobDataAfterExecution)
            .WithDescription(description)
            .WithIdentity(Key);
    }
}