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

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - Can be null when received from Web API
// ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract

namespace Quartz.HttpApiContract;

internal record JobDetailDto(
    string Name,
    string Group,
    string JobType,
    string? Description,
    bool Durable,
    bool RequestsRecovery,
    bool ConcurrentExecutionDisallowed,
    bool PersistJobDataAfterExecution,
    JobDataMap JobDataMap
) : IValidatable
{
    public IEnumerable<string> Validate()
    {
        if (Name is null)
        {
            yield return "Job detail is missing name";
        }

        if (Group is null)
        {
            yield return "Job detail is missing group";
        }

        if (JobType is null)
        {
            yield return "Job detail is missing job type";
        }
        else
        {
            var jobType = Type.GetType(JobType, throwOnError: false);
            if (jobType is null)
            {
                yield return "Job detail has unknown job type " + JobType;
            }
        }
    }

    public (IJobDetail? JobDetail, string? ErrorReason) AsIJobDetail()
    {
        var jobType = Type.GetType(JobType, throwOnError: false);
        if (jobType is null)
        {
            return (null, "Unknown job type");
        }

        var jobDetail = JobBuilder.Create(jobType)
            .WithIdentity(Name, Group)
            .WithDescription(Description)
            .StoreDurably(Durable)
            .RequestRecovery(RequestsRecovery)
            .DisallowConcurrentExecution(ConcurrentExecutionDisallowed)
            .PersistJobDataAfterExecution(PersistJobDataAfterExecution)
            .UsingJobData(JobDataMap ?? new JobDataMap())
            .Build();

        return (jobDetail, null);
    }

    public static JobDetailDto Create(IJobDetail jobDetail)
    {
        ArgumentNullException.ThrowIfNull(jobDetail);

        return new JobDetailDto(
            Name: jobDetail.Key.Name,
            Group: jobDetail.Key.Group,
            JobType: jobDetail.JobType.FullName,
            Description: jobDetail.Description,
            Durable: jobDetail.Durable,
            RequestsRecovery: jobDetail.RequestsRecovery,
            ConcurrentExecutionDisallowed: jobDetail.ConcurrentExecutionDisallowed,
            PersistJobDataAfterExecution: jobDetail.PersistJobDataAfterExecution,
            JobDataMap: jobDetail.JobDataMap
        );
    }
}