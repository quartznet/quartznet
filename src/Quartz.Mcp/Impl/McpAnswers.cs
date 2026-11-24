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

namespace Quartz.Mcp.Impl;

// What the tools answer with. None of these carries a data value: a job's and a trigger's data are named by their
// keys alone, and a run's recorded input, its captured log, its metrics and the scheduler's context are not here
// at all. Free text written by a scheduler's users is cut to a length and says so, beside the text.

/// <summary>
/// Which scheduler an answer is about, and how fresh what it says is.
/// </summary>
/// <param name="Key">The key to name it by in the next call: <c>w1/QuartzScheduler</c>, or a bare name.</param>
/// <param name="Origin">Where it is: this process, a window onto a database, an HTTP target, an agent.</param>
internal sealed record SchedulerFreshness(string Key, SchedulerOrigin Origin)
{
    /// <summary>When an agent last spoke to this process; absent for a scheduler that answered live.</summary>
    public DateTimeOffset? LastSeenUtc { get; init; }

    /// <summary>For a window: how many of the cluster's nodes are still checking in.</summary>
    public int? LiveNodes { get; init; }

    /// <summary>For a window: how many nodes have a check-in row at all.</summary>
    public int? Nodes { get; init; }
}

/// <summary>A job or a trigger, by its key.</summary>
internal sealed record McpKey(string Group, string Name)
{
    public static McpKey Of(JobKey key) => new(key.Group, key.Name);

    public static McpKey Of(TriggerKey key) => new(key.Group, key.Name);
}

/// <summary>A pause, as the trigger or job recorded it.</summary>
internal sealed record PauseRow(string? By, string? Reason, DateTimeOffset AtUtc)
{
    public bool? ReasonTruncated { get; init; }
}

internal sealed record ListedScheduler(string Key, string Name, string? Target, SchedulerOrigin Origin, SchedulerStatus? Status)
{
    public DateTimeOffset? LastSeenUtc { get; init; }

    public string[]? Members { get; init; }

    public int? LiveNodes { get; init; }

    public int? Nodes { get; init; }
}

internal sealed record ListSchedulersAnswer(DateTimeOffset AsOfUtc, ListedScheduler[] Schedulers);

internal sealed record SchedulerDetailsAnswer(
    DateTimeOffset AsOfUtc,
    SchedulerFreshness Scheduler,
    string SchedulerInstanceId,
    SchedulerStatus Status,
    bool Clustered,
    bool Persistent,
    string Version,
    DateTimeOffset? RunningSince,
    int JobsExecuted,
    string[] ServedExecutionGroups,
    string[] RefusedExecutionGroups);

internal sealed record ClusterNodeRow(string InstanceId, DateTimeOffset? LastCheckInUtc, ClusterNodeState State, bool Live, bool IsCurrentNode)
{
    public string? QuartzVersion { get; init; }
}

internal sealed record ClusterNodesAnswer(DateTimeOffset AsOfUtc, SchedulerFreshness Scheduler, ClusterNodeRow[] Nodes);

internal sealed record LastRunRow(JobRunResult Result, DateTimeOffset FiredAtUtc, bool Failing);

internal sealed record JobRow(string Group, string Name, string? Description, bool Durable)
{
    public bool? DescriptionTruncated { get; init; }

    public LastRunRow? LastRun { get; init; }
}

internal sealed record ListJobsAnswer(DateTimeOffset AsOfUtc, SchedulerFreshness Scheduler, JobRow[] Jobs, bool HasMore)
{
    /// <summary>Why no row carries its last run: the scheduler's history keeps no per-job status.</summary>
    public string? RunStatusNote { get; init; }
}

internal sealed record JobTriggerRow(string Group, string Name, string Kind, string? Schedule, DateTimeOffset? NextFireUtc, DateTimeOffset? PreviousFireUtc);

internal sealed record RunStatusRow(
    DateTimeOffset LastFiredAtUtc,
    JobRunResult LastResult,
    DateTimeOffset? LastSucceededAtUtc,
    DateTimeOffset? LastFailedAtUtc,
    bool Failing,
    int ConsecutiveFailures,
    long RunCount,
    long FailureCount)
{
    public string? LastFailureMessage { get; init; }

    public bool? LastFailureMessageTruncated { get; init; }
}

internal sealed record JobAnswer(
    DateTimeOffset AsOfUtc,
    SchedulerFreshness Scheduler,
    string Group,
    string Name,
    string JobType,
    string? Description,
    bool Durable,
    bool RequestsRecovery,
    bool? ConcurrentExecutionDisallowed,
    bool? PersistJobDataAfterExecution,
    string[] DataKeys,
    JobTriggerRow[] Triggers)
{
    public bool? DescriptionTruncated { get; init; }

    public RunStatusRow? RunStatus { get; init; }

    public string? RunStatusNote { get; init; }
}

internal sealed record TriggerRow(
    string Group,
    string Name,
    McpKey Job,
    TriggerState State,
    string? Schedule,
    string? Expression,
    DateTimeOffset? NextFireUtc,
    DateTimeOffset? PreviousFireUtc)
{
    public PauseRow? Paused { get; init; }

    public string? Description { get; init; }

    public bool? DescriptionTruncated { get; init; }
}

internal sealed record ListTriggersAnswer(DateTimeOffset AsOfUtc, SchedulerFreshness Scheduler, TriggerRow[] Triggers, bool HasMore);

internal sealed record TriggerAnswer(
    DateTimeOffset AsOfUtc,
    SchedulerFreshness Scheduler,
    string Group,
    string Name,
    McpKey Job,
    string Kind,
    string? Schedule,
    string? Expression,
    string? TimeZone,
    string MisfireInstruction,
    string? Calendar,
    int Priority,
    TriggerState State,
    DateTimeOffset[] NextFireTimesUtc,
    string[] DataKeys)
{
    public PauseRow? Paused { get; init; }

    public string? Description { get; init; }

    public bool? DescriptionTruncated { get; init; }

    public DateTimeOffset? EndTimeUtc { get; init; }
}

internal sealed record ExecutingRow(string FireInstanceId, McpKey Trigger, string Node, DateTimeOffset FiredAtUtc, long RunningForSeconds)
{
    public McpKey? Job { get; init; }

    public bool? InterruptRequested { get; init; }
}

internal sealed record ListExecutingAnswer(DateTimeOffset AsOfUtc, SchedulerFreshness Scheduler, ExecutingRow[] Executing, bool HasMore);

internal sealed record HistoryRow(
    McpKey Job,
    McpKey Trigger,
    DateTimeOffset FiredAtUtc,
    double DurationSeconds,
    JobRunResult Result,
    int RetryAttempt,
    bool RetryScheduled,
    string Node)
{
    public string? EntryId { get; init; }

    public string? ErrorMessage { get; init; }

    public bool? ErrorMessageTruncated { get; init; }

    public string? Summary { get; init; }

    public bool? SummaryTruncated { get; init; }

    public bool? Manual { get; init; }
}

internal sealed record HistoryAnswer(DateTimeOffset AsOfUtc, SchedulerFreshness Scheduler, HistoryRow[] Executions, bool HasMore);

internal sealed record JobRunStatusAnswer(DateTimeOffset AsOfUtc, SchedulerFreshness Scheduler, string Group, string Name)
{
    /// <summary>The status, or absent for a job with no recorded run.</summary>
    public RunStatusRow? Status { get; init; }
}

/// <summary>What a change answered: whether it was applied, and what it means.</summary>
internal sealed record ChangeAnswer(DateTimeOffset AsOfUtc, SchedulerFreshness Scheduler, bool Applied)
{
    public string? Note { get; init; }
}

/// <summary>What a change that needs confirmation would do.</summary>
internal sealed record PreviewRow(string Action, string Summary, McpKey Subject, string[] Effects);

/// <summary>
/// A confirmed tool's answer: a preview and a token to confirm it with, or what the confirmed call applied.
/// </summary>
internal sealed record ConfirmAnswer(DateTimeOffset AsOfUtc, SchedulerFreshness Scheduler)
{
    public bool? Applied { get; init; }

    public PreviewRow? Preview { get; init; }

    public string? ConfirmationToken { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public DateTimeOffset? NextFireUtc { get; init; }

    public string? Note { get; init; }
}

/// <summary>Why a call did nothing.</summary>
/// <param name="Code">A class an agent can branch on: <c>invalid_arguments</c>, <c>forbidden</c>, <c>not_found</c>…</param>
/// <param name="Message">What to tell its user, or to fix.</param>
internal sealed record McpError(string Code, string Message)
{
    /// <summary>For a target that did not serve the call: <c>unavailable</c>, <c>timedOut</c>, <c>refused</c> or <c>failed</c>.</summary>
    public string? Fault { get; init; }

    /// <summary>A change whose answer did not arrive may have been applied; read before retrying.</summary>
    public bool? MayHaveApplied { get; init; }

    /// <summary>For <c>rate_limited</c>: when to try again.</summary>
    public int? RetryAfterSeconds { get; init; }
}

internal sealed record ErrorAnswer(McpError Error)
{
    public DateTimeOffset? AsOfUtc { get; init; }

    public string? Scheduler { get; init; }
}

/// <summary>
/// What a confirmation token carries, protected: nothing in it is a value the caller did not send or could not
/// read, and the digests are one-way.
/// </summary>
internal sealed record ConfirmationPayload(
    int V,
    string Tool,
    string Scheduler,
    string Subject,
    string Args,
    string Caller,
    string State,
    string Nonce,
    DateTimeOffset ExpiresAtUtc);
