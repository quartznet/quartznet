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

namespace Quartz.Impl;

/// <summary>
/// Which retry policy a failed firing is answered with: the trigger's own, else its job type's
/// <see cref="RetryPolicyAttribute" />, else the scheduler's default.
/// </summary>
/// <remarks>
/// <para>
/// Decided when a firing fails rather than when a trigger is stored, so that a policy declared on a job
/// type or set as a default covers triggers stored before it existed, and so that nothing is ever written
/// into a trigger's <c>RETRY_POLICY</c> column on its behalf: the column stays the trigger's own.
/// </para>
/// <para>
/// <see cref="RetryPolicy.None" /> found at any level ends the search with no policy, which is how a
/// trigger or a job type refuses the levels below it.
/// </para>
/// </remarks>
internal static class RetryPolicyResolution
{
    /// <summary>
    /// The policy a failed firing is retried under, or <see langword="null" /> when it is not retried.
    /// </summary>
    /// <param name="triggerPolicy">The trigger's own policy, <see cref="ITrigger.RetryPolicy" />.</param>
    /// <param name="jobDetail">The job, whose type may declare <see cref="RetryPolicyAttribute" />.</param>
    /// <param name="schedulerDefault">The scheduler's default, or <see langword="null" /> when it has none.</param>
    internal static RetryPolicy? Effective(RetryPolicy? triggerPolicy, IJobDetail jobDetail, RetryPolicy? schedulerDefault)
    {
        RetryPolicy? chosen = triggerPolicy ?? OfJobType(jobDetail) ?? schedulerDefault;
        return chosen is null || chosen.IsNone ? null : chosen;
    }

    /// <summary>
    /// What the job's type declares, or <see langword="null" /> when it declares nothing, cannot be
    /// resolved here, or declares something that is not a policy.
    /// </summary>
    /// <remarks>
    /// An attribute that is not a policy reads as none rather than throwing in the middle of a firing:
    /// <see cref="EnsureReadable" /> has already refused it wherever the job was added from this build.
    /// </remarks>
    internal static RetryPolicy? OfJobType(IJobDetail jobDetail)
    {
        return jobDetail.JobType.TryResolve(out Type? jobType) ? JobTypeInformation.GetOrCreate(jobType).RetryPolicy : null;
    }

    /// <summary>
    /// Refuses a job whose type declares a <see cref="RetryPolicyAttribute" /> that is not a policy, so
    /// the mistake is reported where the job is added rather than discovered when it first fails.
    /// </summary>
    /// <exception cref="SchedulerException">The job type's attribute could not be read.</exception>
    internal static void EnsureReadable(IJobDetail jobDetail)
    {
        if (jobDetail.JobType.TryResolve(out Type? jobType)
            && JobTypeInformation.GetOrCreate(jobType).RetryPolicyError is { } error)
        {
            Throw.SchedulerException(
                $"Job '{jobDetail.Key}' cannot be added: its type {jobType.FullName} declares a [RetryPolicy] that is not a retry policy. {error.Message}",
                error);
        }
    }
}
