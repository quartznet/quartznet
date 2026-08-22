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

namespace Quartz;

/// <summary>
/// Conveniences over <see cref="IJobDetail" /> that need nothing but the interface.
/// </summary>
public static class JobDetailExtensions
{
    /// <summary>
    /// Get a <see cref="JobBuilder{TJob}" /> configured to produce a detail equivalent to this one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An extension rather than an interface member, because a builder can be filled in from the
    /// detail's public state alone, and <see cref="JobBuilder{TJob}" /> is sealed: an implementation
    /// of <see cref="IJobDetail" /> other than Quartz's own could only have satisfied such a member by
    /// returning a builder that produces somebody else's type.
    /// </para>
    /// <para>
    /// The result builds Quartz's own implementation, whatever it was called on — a builder is how a
    /// detail is described, not how a type is preserved. Use <see cref="IJobDetail.WithJobData" /> to
    /// vary the job data of a detail of your own, and <see cref="IJobDetail.Clone" /> to copy one.
    /// </para>
    /// <para>
    /// The job type is carried over as the <see cref="JobType" /> the detail holds, so a detail loaded
    /// from a job store whose stored type name does not resolve in this process rebuilds, and keeps
    /// the name as it was stored, rather than throwing here.
    /// </para>
    /// </remarks>
    /// <param name="jobDetail">The detail to describe.</param>
    public static JobBuilder<IJob> GetJobBuilder(this IJobDetail jobDetail)
    {
        ArgumentNullException.ThrowIfNull(jobDetail);

        return JobBuilder.Create()
            .OfType(jobDetail.JobType)
            .RequestRecovery(jobDetail.RequestsRecovery)
            .StoreDurably(jobDetail.Durable)
            .UsingJobData(jobDetail.JobDataMap)
            .DisallowConcurrentExecution(jobDetail.ConcurrentExecutionDisallowed)
            .PersistJobDataAfterExecution(jobDetail.PersistJobDataAfterExecution)
            .WithDescription(jobDetail.Description)
            .WithIdentity(jobDetail.Key);
    }
}
