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

/// <summary>
/// Whether a group is paused, and what its pause recorded.
/// </summary>
/// <remarks>
/// <see cref="Pause" /> is optional so a 4.2 host's answer, which has none, still reads as a paused
/// group with no record.
/// </remarks>
internal record GroupPausedResponse(bool Paused, PauseDto? Pause = null);

/// <summary>
/// The optional body of a pause: why, and who asked.
/// </summary>
/// <remarks>
/// Every member is optional and so is the body, so a pause posted with nothing — which is every pause
/// before 4.3 — means what it always did. <see cref="RequestedBy" /> left out is the authenticated
/// user's name, where there is one.
/// </remarks>
internal sealed record PauseRequest(string? Reason = null, string? RequestedBy = null)
{
    public PauseDetails AsPauseDetails(string? authenticatedUser)
    {
        return new PauseDetails
        {
            Reason = Reason,
            RequestedBy = string.IsNullOrWhiteSpace(RequestedBy) ? authenticatedUser : RequestedBy
        };
    }
}

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
/// The body of a key-set job pause: the keys, and optionally why and who asked.
/// </summary>
/// <remarks>
/// A superset of <see cref="JobKeySetRequest" />, so the body every client before 4.4 sent still binds, and
/// a host before 4.4 reads this one as the keys alone.
/// </remarks>
internal sealed record JobKeySetPauseRequest(KeyDto[] Jobs, string? Reason = null, string? RequestedBy = null) : IValidatable
{
    public IEnumerable<string> Validate() => Jobs is null ? ["Missing job keys"] : Jobs.SelectMany(x => x.Validate());

    /// <inheritdoc cref="KeySetPause.Details" />
    public PauseDetails? AsPauseDetails(string? authenticatedUser) => KeySetPause.Details(Reason, RequestedBy, authenticatedUser);
}

/// <summary>
/// The body of a key-set trigger pause: the keys, and optionally why and who asked.
/// </summary>
/// <remarks>
/// A superset of <see cref="TriggerKeySetRequest" />, so the body every client before 4.4 sent still binds,
/// and a host before 4.4 reads this one as the keys alone.
/// </remarks>
internal sealed record TriggerKeySetPauseRequest(KeyDto[] Triggers, string? Reason = null, string? RequestedBy = null) : IValidatable
{
    public IEnumerable<string> Validate() => Triggers is null ? ["Missing trigger keys"] : Triggers.SelectMany(x => x.Validate());

    /// <inheritdoc cref="KeySetPause.Details" />
    public PauseDetails? AsPauseDetails(string? authenticatedUser) => KeySetPause.Details(Reason, RequestedBy, authenticatedUser);
}

/// <summary>
/// What a key-set pause body says about the pause.
/// </summary>
internal static class KeySetPause
{
    /// <summary>
    /// The pause the body describes, with <paramref name="authenticatedUser" /> as the requester when the
    /// body names none — or <see langword="null" /> when the body names neither a reason nor a requester.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="PauseRequest" />, a key-set body always arrives, because it carries the keys. Every
    /// client before 4.4 sends it without either text, so that body is the reasonless pause and is never
    /// put in the caller's name: filling the requester there would send every authenticated client's
    /// reasonless pause down the path that records one.
    /// </remarks>
    public static PauseDetails? Details(string? reason, string? requestedBy, string? authenticatedUser)
    {
        if (string.IsNullOrWhiteSpace(reason) && string.IsNullOrWhiteSpace(requestedBy))
        {
            return null;
        }

        return new PauseDetails
        {
            Reason = reason,
            RequestedBy = string.IsNullOrWhiteSpace(requestedBy) ? authenticatedUser : requestedBy
        };
    }
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
    /// <summary>
    /// What becomes of a trigger already stored under the key, when the trigger is scheduled on its own.
    /// </summary>
    /// <remarks>
    /// Optional, so a body without it means what it always did. A host older than 4.3 ignores it, which
    /// is why <c>HttpScheduler</c> also sends <see cref="Replace" /> for <see cref="TriggerConflict.Replace" />.
    /// </remarks>
    public TriggerConflict? OnConflict { get; init; }

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

        if (OnConflict is { } onConflict)
        {
            if (onConflict is not (TriggerConflict.Throw or TriggerConflict.Replace or TriggerConflict.Keep or TriggerConflict.KeepEarlier))
            {
                yield return $"onConflict must be Throw, Replace, Keep or KeepEarlier, got {onConflict}";
            }
            else if (Job is not null)
            {
                yield return "onConflict applies to a trigger scheduled on its own; schedule the job first, or use replace";
            }
            else if (Replace && onConflict != TriggerConflict.Replace)
            {
                yield return $"replace and onConflict {onConflict} contradict each other";
            }
        }
    }
}

internal record ScheduleJobResponse(DateTimeOffset FirstFireTimeUtc)
{
    /// <summary>
    /// What the host did with the trigger, when the request named <see cref="ScheduleJobRequest.OnConflict" />;
    /// <see langword="null" /> otherwise, and from a host older than 4.3.
    /// </summary>
    public ScheduleOutcome? Outcome { get; init; }
}

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
/// The body of a backfill: the range, and <see cref="BackfillOptions" />' three members, each optional.
/// </summary>
/// <remarks>
/// Only the presence of the range is checked here. Everything else — an empty range, one reaching past
/// the scheduler's current time, the options' bounds — is <see cref="Backfilling.Run" />'s, so a request
/// and an in-process call are refused by the same words.
/// </remarks>
internal sealed record BackfillRequest(DateTimeOffset? From, DateTimeOffset? To) : IValidatable
{
    public int? MaxSlots { get; init; }

    public TimeSpan? Spacing { get; init; }

    public string? ExecutionGroup { get; init; }

    /// <summary>
    /// The body <c>HttpScheduler</c> sends: the range, and every option as the caller left it.
    /// </summary>
    public static BackfillRequest Create(DateTimeOffset from, DateTimeOffset to, BackfillOptions options)
    {
        return new BackfillRequest(from, to)
        {
            MaxSlots = options.MaxSlots,
            Spacing = options.Spacing,
            ExecutionGroup = options.ExecutionGroup
        };
    }

    public BackfillOptions AsOptions()
    {
        BackfillOptions options = new() { Spacing = Spacing.GetValueOrDefault(), ExecutionGroup = ExecutionGroup };
        return MaxSlots is { } maxSlots ? options with { MaxSlots = maxSlots } : options;
    }

    public IEnumerable<string> Validate()
    {
        if (From is null)
        {
            yield return "Missing from";
        }

        if (To is null)
        {
            yield return "Missing to";
        }
    }
}

/// <summary>
/// What a backfill found and did: <see cref="BackfillResult" /> on the wire.
/// </summary>
internal sealed record BackfillResponse(
    int SlotsFound,
    int Scheduled,
    int AlreadyScheduled,
    DateTimeOffset? FirstSlot,
    DateTimeOffset? LastSlot,
    KeyDto[] Triggers)
{
    public static BackfillResponse Create(BackfillResult result)
    {
        return new BackfillResponse(
            result.SlotsFound,
            result.Scheduled,
            result.AlreadyScheduled,
            result.FirstSlot,
            result.LastSlot,
            [.. result.ScheduledTriggers.Select(KeyDto.Create)]);
    }

    public BackfillResult AsResult()
    {
        return new BackfillResult
        {
            SlotsFound = SlotsFound,
            AlreadyScheduled = AlreadyScheduled,
            FirstSlot = FirstSlot,
            LastSlot = LastSlot,
            ScheduledTriggers = [.. (Triggers ?? []).Select(key => key.AsTriggerKey())]
        };
    }
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

            if (kvp.Key is not null && ExecutionLimits.TryReadPrefixKey(kvp.Key.Trim(), out string? prefix))
            {
                if (!ExecutionLimits.IsValidPrefix(prefix))
                {
                    yield return $"Limit key '{kvp.Key}' names no prefix; a prefix key is the prefix followed by one '*', such as 'tenant:*'";
                }
                else if (kvp.Value.MaxConcurrent is null)
                {
                    yield return $"Limit for prefix '{kvp.Key}' needs a count; a prefix cannot be unlimited";
                }
            }
        }
    }
}