using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Quartz;

public interface IJobConfigurator<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob> where TJob : IJob
{
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
    IJobConfigurator<TJob> WithIdentity(string name);

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
    IJobConfigurator<TJob> WithIdentity(string name, string group);

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
    IJobConfigurator<TJob> WithIdentity(JobKey key);

    /// <summary>
    /// Set the given (human-meaningful) description of the Job.
    /// </summary>
    /// <param name="description"> the description for the Job</param>
    /// <returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.Description" />
    IJobConfigurator<TJob> WithDescription(string? description);

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
    IJobConfigurator<TJob> RequestRecovery(bool shouldRecover = true);

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
    IJobConfigurator<TJob> StoreDurably(bool durability = true);

    /// <summary>
    /// Add the given key-value pair to the JobDetail's <see cref="JobDataMap" />.
    /// </summary>
    /// <remarks>
    /// The value is stored as given. A persistent job store can only hold what its serializer
    /// round-trips, and AdoJobStore's <c>StoreJobDataAsStrings</c> mode only strings.
    /// </remarks>
    /// <param name="key">the key to store the value under</param>
    /// <param name="value">the value to store</param>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    IJobConfigurator<TJob> UsingJobData(string key, object? value);

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
    /// serializer round-trips, and AdoJobStore's <c>StoreJobDataAsStrings</c> mode only strings. Nothing beyond
    /// enums is converted for you.
    /// </para>
    /// </remarks>
    /// <param name="jobProperty">an expression naming the job property, such as <c>job =&gt; job.Parameter</c></param>
    /// <param name="value">the value to bind to that property</param>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    IJobConfigurator<TJob> UsingJobData<TValue>(Expression<Func<TJob, TValue>> jobProperty, TValue value);

    /// <summary>
    /// Add all the data from the given <see cref="JobDataMap" /> to the
    /// <see cref="IJobDetail" />'s <see cref="JobDataMap" />.
    /// </summary>
    ///<returns>the updated JobBuilder</returns>
    /// <seealso cref="IJobDetail.JobDataMap" />
    IJobConfigurator<TJob> UsingJobData(JobDataMap newJobDataMap);

    /// <summary>
    /// Instructs the <see cref="IScheduler" /> whether or not concurrent execution of the job should be disallowed.
    /// </summary>
    /// <param name="concurrentExecutionDisallowed">Indicates whether or not concurrent execution of the job should be disallowed.</param>
    /// <returns>
    /// The updated <see cref="IJobConfigurator{TJob}"/>.
    /// </returns>
    /// <remarks>
    /// If not explicitly set, concurrent execution of a job is only disallowed if either the <see cref="IJobDetail.JobType"/> itself,
    /// one of its ancestors or one of the interfaces that it implements, is annotated with <see cref="DisallowConcurrentExecutionAttribute"/>.
    /// </remarks>
    /// <seealso cref="DisallowConcurrentExecutionAttribute"/>
    IJobConfigurator<TJob> DisallowConcurrentExecution(bool concurrentExecutionDisallowed = true);

    /// <summary>
    /// Instructs the <see cref="IScheduler" /> whether or not job data should be re-stored when execution of the job completes.
    /// </summary>
    /// <param name="persistJobDataAfterExecution">Indicates whether or not job data should be re-stored when execution of the job completes.</param>
    /// <returns>
    /// The updated <see cref="IJobConfigurator{TJob}"/>.
    /// </returns>
    /// <remarks>
    /// If not explicitly set, job data is only re-stored if either the <see cref="IJobDetail.JobType"/> itself, one of
    /// its ancestors or one of the interfaces that it implements, is annotated with <see cref="PersistJobDataAfterExecutionAttribute"/>.
    /// </remarks>
    /// <seealso cref="PersistJobDataAfterExecutionAttribute"/>
    IJobConfigurator<TJob> PersistJobDataAfterExecution(bool persistJobDataAfterExecution = true);
}
