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
    /// <summary>
    /// A job type name longer than this is a payload rather than a name, and is rejected before anything
    /// has to parse it.
    /// </summary>
    private const int MaxJobTypeNameLength = 1024;

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
        else if (!IsWellFormedTypeName(JobType))
        {
            yield return "Job detail has malformed job type " + JobType;
        }
    }

    [RequiresUnreferencedCode("Register every job type with AddJob<T>() or reference it from JobBuilder.Create<T>(); a type named only by a string in an HTTP API request is not guaranteed to survive trimming.")]
    public (IJobDetail? JobDetail, string? ErrorReason) AsIJobDetail()
    {
        if (JobType is null || !IsWellFormedTypeName(JobType))
        {
            return (null, "Missing or malformed job type");
        }

        // The name is carried as a name. The cast stores it unresolved, so building the detail neither
        // loads nor probes for an assembly - the side the job actually runs on resolves it through the
        // type load path, when it needs the type. Explicit because that deferral is the leap being made.
        IJobDetail jobDetail = JobBuilder.Create()
            .OfType((JobType) JobType)
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

    /// <summary>
    /// Checks that a job type name has the shape of one, without resolving it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These contract types are shared with the server, so a name this type resolved would be resolved by
    /// the scheduler host on behalf of whoever sent the request. Add-job and schedule-job requests would
    /// then let a caller have the host walk its assembly probing paths for any name it likes and read the
    /// answer off the response - a type disclosure side channel, and one that does real work per request.
    /// </para>
    /// <para>
    /// Resolving here also breaks readers that have every right not to have the job's assembly loaded: a
    /// dashboard or ops process listing jobs only wants to show the name.
    /// </para>
    /// </remarks>
    private static bool IsWellFormedTypeName(string typeName)
    {
        ReadOnlySpan<char> name = typeName.AsSpan().Trim();
        if (name.IsEmpty || name.Length > MaxJobTypeNameLength)
        {
            return false;
        }

        // A name that leads with the assembly separator has no type part at all.
        if (name[0] == ',')
        {
            return false;
        }

        foreach (char character in name)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
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