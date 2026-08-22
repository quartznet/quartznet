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

using Quartz.Impl;
namespace Quartz;

/// <summary>
/// Conveys the detail properties of a given job instance. 
/// JobDetails are to be created/defined with <see cref="JobBuilder" />.
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
/// <para>
/// The interface is implementable. Everything Quartz asks of a detail is declared here, including
/// <see cref="WithJobData" />, which is how a job store re-stores the data of a
/// <see cref="PersistJobDataAfterExecutionAttribute" /> job without having to rebuild the detail
/// itself — a store cannot know how to construct an implementation it has never seen. How far such
/// an implementation travels depends on the store:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="RAMJobStore" /> holds the instances it is given and hands back copies of them, so a
/// detail of your own round-trips as itself.
/// </description>
/// </item>
/// <item>
/// <description>
/// Anything that keeps a detail as data does not. The ADO.NET job store writes the columns of
/// <c>QRTZ_JOB_DETAILS</c> and rebuilds every detail it loads through <see cref="JobBuilder" />, so
/// what comes back is Quartz's own implementation; the HTTP client rebuilds one the same way from
/// its wire payload. Whatever your type carries beyond the members declared here is gone by then,
/// so anything that has to survive belongs in the <see cref="JobDataMap" />.
/// </description>
/// </item>
/// </list>
/// </remarks>
/// <seealso cref="IJob" />
/// <seealso cref="DisallowConcurrentExecutionAttribute"/>
/// <seealso cref="PersistJobDataAfterExecutionAttribute"/>
/// <seealso cref="Quartz.JobDataMap"/>
/// <seealso cref="ITrigger"/>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface IJobDetail
{
    /// <summary>
    /// The key that identifies this jobs uniquely.
    /// </summary>
    JobKey Key { get; }

    /// <summary>
    /// Get or set the description given to the <see cref="IJob" /> instance by its
    /// creator (if any).
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Get the instance of <see cref="IJob" /> that will be executed.
    /// </summary>
    JobType JobType { get; }

    /// <summary>
    /// Get or set the <see cref="JobDataMap" /> that is associated with the <see cref="IJob" />.
    /// </summary>
    JobDataMap JobDataMap { get; }

    /// <summary>
    /// Whether or not the <see cref="IJob" /> should remain stored after it is
    /// orphaned (no <see cref="ITrigger" />s point to it).
    /// </summary>
    /// <remarks>
    /// If not explicitly set, the default value is <see langword="false" />.
    /// </remarks>
    /// <returns> 
    /// <see langword="true" /> if the Job should remain persisted after being orphaned.
    /// </returns>
    bool Durable { get; }

    /// <summary>
    /// Whether the associated Job class carries the <see cref="PersistJobDataAfterExecutionAttribute" />.
    /// </summary>
    /// <seealso cref="PersistJobDataAfterExecutionAttribute" />
    bool PersistJobDataAfterExecution { get; }

    /// <summary>
    /// Whether the associated Job class carries the <see cref="DisallowConcurrentExecutionAttribute" />.
    /// </summary>
    /// <seealso cref="DisallowConcurrentExecutionAttribute"/>
    bool ConcurrentExecutionDisallowed { get; }

    /// <summary>
    /// Set whether or not the <see cref="IScheduler" /> should re-Execute
    /// the <see cref="IJob" /> if a 'recovery' or 'fail-over' situation is
    /// encountered.
    /// </summary>
    /// <remarks>
    /// If not explicitly set, the default value is <see langword="false" />.
    /// </remarks>
    /// <seealso cref="IJobExecutionContext.Recovering" />
    bool RequestsRecovery { get; }

    /// <summary>
    /// Return a detail like this one but carrying <paramref name="jobDataMap" /> as its
    /// <see cref="JobDataMap" />, leaving this instance untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A job store calls this when a <see cref="PersistJobDataAfterExecutionAttribute" /> job finishes
    /// and the data it left behind has to become the data the next firing sees. It is the one
    /// mutation-shaped member on the interface, and it exists so that a store re-storing job data does
    /// not have to construct a detail itself — which it could only do as Quartz's own implementation,
    /// silently swapping out yours.
    /// </para>
    /// <para>
    /// The map is taken as given rather than copied: the caller hands over a map it does not keep.
    /// </para>
    /// </remarks>
    /// <param name="jobDataMap">The job data the returned detail carries.</param>
    IJobDetail WithJobData(JobDataMap jobDataMap);

    IJobDetail Clone();
}