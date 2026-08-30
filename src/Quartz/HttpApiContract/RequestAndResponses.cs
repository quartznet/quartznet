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

// When updating this, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.AddCalendarRequest
internal record AddCalendarRequest(string CalendarName, ICalendar Calendar, bool Replace, bool UpdateTriggers) : IValidatable
{
    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(CalendarName))
        {
            yield return "Missing calendar name";
        }

        if (Calendar is null)
        {
            yield return "Missing calendar details missing";
        }
    }
}

internal record AddJobRequest(JobDetailDto Job, bool Replace, bool? StoreNonDurableWhileAwaitingScheduling) : IValidatable
{
    public IEnumerable<string> Validate() => Job is null ? ["Missing job details"] : Job.Validate();
}

internal record ExistsResponse(bool Exists);

internal record GroupPausedResponse(bool Paused);

/// <summary>
/// The answer of a mutation aimed at one entity whose effect may be a no-op: <c>Applied</c> is
/// <see langword="true" /> when the entity existed and the operation changed it.
/// </summary>
/// <remarks>
/// Every such endpoint answers with this one shape, so the body follows from what the operation is
/// rather than from which endpoint it was. A mutation that always acts answers with an empty body
/// instead, and one aimed at a key set answers with what it applied to.
/// </remarks>
internal record OperationAppliedResponse(bool Applied);

/// <summary>
/// Answer of a group-matcher pause/resume: the names of the groups the operation affected.
/// </summary>
internal record AffectedGroupsResponse(string[] Groups);

/// <summary>
/// The job keys a key-set pause or resume is aimed at.
/// </summary>
internal record JobKeySetRequest(KeyDto[] Jobs) : IValidatable
{
    public IEnumerable<string> Validate() => Jobs is null ? ["Missing job keys"] : Jobs.SelectMany(x => x.Validate());
}

/// <summary>
/// The trigger keys a key-set pause, resume or error-state reset is aimed at.
/// </summary>
internal record TriggerKeySetRequest(KeyDto[] Triggers) : IValidatable
{
    public IEnumerable<string> Validate() => Triggers is null ? ["Missing trigger keys"] : Triggers.SelectMany(x => x.Validate());
}

/// <summary>
/// Answer of a key-set job mutation — pause, resume or delete: the keys the operation applied to, the
/// plural of <see cref="OperationAppliedResponse" />. A key that was not found is simply absent.
/// </summary>
internal record AppliedJobKeysResponse(KeyDto[] Jobs);

/// <summary>
/// Answer of a key-set trigger mutation — pause, resume, error-state reset or unschedule: the keys the
/// operation applied to, the plural of <see cref="OperationAppliedResponse" />. A key that did not
/// move is simply absent.
/// </summary>
internal record AppliedTriggerKeysResponse(KeyDto[] Triggers);

internal record DeleteJobsRequest(KeyDto[] Jobs) : IValidatable
{
    public IEnumerable<string> Validate() => Jobs is null ? ["Missing job keys"] : Jobs.SelectMany(x => x.Validate());
}

// When updating this, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.ScheduleJobRequest
internal record ScheduleJobRequest(ITrigger Trigger, JobDetailDto? Job, bool Replace = false) : IValidatable
{
    public IEnumerable<string> Validate()
    {
        if (Trigger is null)
        {
            yield return "Missing trigger details";
        }

        if (Job is not null)
        {
            foreach (var errorMessage in Job.Validate())
            {
                yield return errorMessage;
            }
        }
    }
}

internal record ScheduleJobResponse(DateTimeOffset FirstFireTimeUtc);

// When updating these, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.ScheduleJobsRequest/ScheduleJobsRequestItem
internal record ScheduleJobsRequest(ScheduleJobsRequestItem[] JobsAndTriggers, bool Replace) : IValidatable
{
    public IEnumerable<string> Validate() => JobsAndTriggers is null ? ["Missing jobs and triggers"] : JobsAndTriggers.SelectMany(x => x.Validate());
}

internal record ScheduleJobsRequestItem(JobDetailDto Job, ITrigger[] Triggers) : IValidatable
{
    public IEnumerable<string> Validate()
    {
        if (Job is null)
        {
            yield return "Missing job details";
        }
        else
        {
            foreach (var errorMessage in Job.Validate())
            {
                yield return errorMessage;
            }
        }

        if (Triggers is null)
        {
            yield return "Missing triggers";
        }
    }
}

internal record TriggerJobRequest(JobDataMap JobData);

// When updating these, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.RescheduleJobRequest
internal record RescheduleJobRequest(ITrigger NewTrigger) : IValidatable
{
    public IEnumerable<string> Validate()
    {
        if (NewTrigger is null)
        {
            yield return "Missing new trigger details";
        }
    }
}

internal record RescheduleJobResponse(DateTimeOffset? FirstFireTimeUtc);

internal record UnscheduleJobsRequest(KeyDto[] Triggers) : IValidatable
{
    public IEnumerable<string> Validate() => Triggers is null ? ["Missing trigger keys"] : Triggers.SelectMany(x => x.Validate());
}

/// <summary>
/// One group's limit on the wire: the count, and what it is counted against.
/// </summary>
/// <remarks>
/// A record rather than a bare number because a limit that lost its scope in transit would come back
/// as a per-node one, which is a quieter lie than a missing field. <see cref="Scope" /> defaults to
/// <see cref="ExecutionLimitScope.Node" />, so a body that omits it says what it always said.
/// </remarks>
internal record ExecutionLimitDto(int? MaxConcurrent, ExecutionLimitScope Scope = ExecutionLimitScope.Node);

internal record ExecutionLimitsResponse(Dictionary<string, ExecutionLimitDto>? Limits, bool UseTriggerGroupWhenUnset = false);

internal record SetExecutionLimitsRequest(Dictionary<string, ExecutionLimitDto>? Limits, bool UseTriggerGroupWhenUnset = false) : IValidatable
{
    public IEnumerable<string> Validate()
    {
        if (Limits is null)
        {
            yield break;
        }

        foreach (var kvp in Limits)
        {
            bool isValidKey = kvp.Key is not null && (kvp.Key is "" or "*" or "_" || !string.IsNullOrWhiteSpace(kvp.Key));
            if (!isValidKey)
            {
                yield return $"Limit key '{kvp.Key}' is invalid";
            }

            if (kvp.Value is null)
            {
                yield return $"Limit for group '{kvp.Key}' is missing";
                continue;
            }

            if (kvp.Value.MaxConcurrent < 0)
            {
                yield return $"Limit value for group '{kvp.Key}' must be non-negative, got {kvp.Value.MaxConcurrent}";
            }

            if (kvp.Value.Scope is not (ExecutionLimitScope.Node or ExecutionLimitScope.Cluster))
            {
                yield return $"Limit scope for group '{kvp.Key}' must be Node or Cluster, got {kvp.Value.Scope}";
            }
        }
    }
}