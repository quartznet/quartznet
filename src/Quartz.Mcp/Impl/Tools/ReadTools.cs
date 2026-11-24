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

using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.Mcp.Impl.Tools;

// The ten tools that read. Each is a carrier of the HTTP API's read routes over SchedulerOperations, and each
// answers with keys, states, times and the schedule in words, never with a data value.

internal sealed class ListSchedulersTool()
    : QuartzTool<ListSchedulersArguments, ListSchedulersAnswer>(
        "list_schedulers",
        "List schedulers",
        "Lists the schedulers this endpoint reaches: this process's own, the windows onto attached databases, HTTP targets and agents, each with the key to name it by in every other tool, where it is, its status and, for an agent, when it was last heard from.",
        [SchedulerRoutes.GetAllSchedulers],
        McpJsonContext.Default.ListSchedulersArguments,
        McpJsonContext.Default.ListSchedulersAnswer)
{
    protected override async ValueTask<ListSchedulersAnswer> Run(ToolCall call, ListSchedulersArguments arguments, CancellationToken cancellationToken)
    {
        McpAccess access = call.Services.GetRequiredService<McpAccess>();
        ISchedulerRegistry registry = call.Services.GetRequiredService<ISchedulerRegistry>();

        SchedulerHeaderDto[] headers = await SchedulerOperations.GetAllSchedulers(
            registry,
            (key, token) => access.MayView(call.Caller, call.Services, key, token),
            cancellationToken).ConfigureAwait(false);

        ISchedulerRepository repository = call.Services.GetRequiredService<ISchedulerRepository>();
        SchedulerWindowRegistry? windows = call.Services.GetService<SchedulerWindowRegistry>();

        List<ListedScheduler> rows = new(headers.Length);
        foreach (SchedulerHeaderDto header in headers)
        {
            SchedulerRef key = new(header.Name, header.Target);
            ListedScheduler row = new(key.Key, header.Name, header.Target, header.Origin, header.Status)
            {
                LastSeenUtc = header.LastSeenUtc,
                Members = header.Members.Length > 0 ? header.Members : null,
            };

            if (header.Origin == SchedulerOrigin.Window && header.Target is not null
                && SchedulerLookup.Targeted(repository, windows, key) is { IsWindow: true } window)
            {
                try
                {
                    WindowLiveness liveness = await WindowLiveness.Read(window.Scheduler, cancellationToken).ConfigureAwait(false);
                    row = row with { LiveNodes = liveness.LiveNodes, Nodes = liveness.Nodes };
                }
                catch (SchedulerException)
                {
                    // Listed without its nodes, as the listing lists a scheduler it could not ask.
                }
            }

            rows.Add(row);
        }

        return new ListSchedulersAnswer(call.Now(), [.. rows]);
    }
}

internal sealed class GetSchedulerTool()
    : QuartzTool<SchedulerArguments, SchedulerDetailsAnswer>(
        "get_scheduler",
        "Get a scheduler",
        "Says what one scheduler is: its status, whether its store is clustered and persistent, its Quartz version, since when it runs, how many jobs it has run, and which execution groups the node serves or refuses.",
        [SchedulerRoutes.GetSchedulerDetails],
        McpJsonContext.Default.SchedulerArguments,
        McpJsonContext.Default.SchedulerDetailsAnswer)
{
    protected override async ValueTask<SchedulerDetailsAnswer> Run(ToolCall call, SchedulerArguments arguments, CancellationToken cancellationToken)
    {
        SchedulerDto details = await SchedulerOperations.GetSchedulerDetails(call.Scheduler, cancellationToken).ConfigureAwait(false);
        SchedulerFreshness freshness = await call.Freshness(cancellationToken).ConfigureAwait(false);

        // A window is never started, so its own status says only that this process built it; what its cluster's
        // check-ins say is the answer to "is anything running this".
        SchedulerStatus status = details.Status;
        if (call.Resolution!.IsWindow)
        {
            status = (await WindowLiveness.Read(call.Scheduler, cancellationToken).ConfigureAwait(false)).Status;
        }

        return new SchedulerDetailsAnswer(
            call.Now(),
            freshness,
            details.SchedulerInstanceId,
            status,
            details.JobStore.Clustered,
            details.JobStore.Persistent,
            details.Statistics.Version,
            details.Statistics.RunningSince,
            details.Statistics.JobsExecuted,
            details.ServedExecutionGroups,
            details.RefusedExecutionGroups);
    }
}

internal sealed class ListClusterNodesTool()
    : QuartzTool<SchedulerArguments, ClusterNodesAnswer>(
        "list_cluster_nodes",
        "List cluster nodes",
        "Lists the nodes of a scheduler's cluster with each one's last check-in and whether the cluster still counts it live.",
        [SchedulerRoutes.GetClusterNodes],
        McpJsonContext.Default.SchedulerArguments,
        McpJsonContext.Default.ClusterNodesAnswer)
{
    protected override async ValueTask<ClusterNodesAnswer> Run(ToolCall call, SchedulerArguments arguments, CancellationToken cancellationToken)
    {
        ClusterNodeDto[] nodes = await SchedulerOperations.GetClusterNodes(call.Scheduler, cancellationToken).ConfigureAwait(false);
        ClusterNodeRow[] rows = [.. nodes.Select(static node => new ClusterNodeRow(node.InstanceId, node.LastCheckInUtc, node.State, node.State != ClusterNodeState.Failed, node.IsCurrentNode)
        {
            QuartzVersion = node.QuartzVersion
        })];

        return new ClusterNodesAnswer(call.Now(), await call.Freshness(cancellationToken).ConfigureAwait(false), rows);
    }
}

internal sealed class ListJobsTool()
    : QuartzTool<ListJobsArguments, ListJobsAnswer>(
        "list_jobs",
        "List jobs",
        "Lists a scheduler's jobs, a page at a time, with each one's description, whether it is durable, and its last run: the result, when it fired, and whether it is failing.",
        [SchedulerRoutes.QueryJobs, SchedulerRoutes.FetchJobRunStatuses],
        McpJsonContext.Default.ListJobsArguments,
        McpJsonContext.Default.ListJobsAnswer)
{
    protected override async ValueTask<ListJobsAnswer> Run(ToolCall call, ListJobsArguments arguments, CancellationToken cancellationToken)
    {
        ListingParameters listing = Listings.Page(arguments.Skip, arguments.Take) with
        {
            GroupContains = arguments.GroupContains,
            NameContains = arguments.NameContains,
        };

        PagedResultDto<JobHeaderDto> page = await SchedulerOperations.QueryJobs(call.Scheduler, listing, cancellationToken).ConfigureAwait(false);

        Dictionary<JobKey, JobRunStatusDto> statuses = [];
        string? note = null;
        if (page.Items.Length > 0)
        {
            try
            {
                JobKeySetRequest request = new([.. page.Items.Select(static job => new KeyDto(job.Name, job.Group))]);
                foreach (JobRunStatusDto status in await SchedulerOperations.FetchJobRunStatuses(call.Scheduler, call.History(), request, cancellationToken).ConfigureAwait(false))
                {
                    statuses[status.Job.AsJobKey()] = status;
                }
            }
            catch (Exception exception) when (exception is NotSupportedException or McpToolException)
            {
                note = "This scheduler's history keeps no per-job run status, so no job carries its last run: " + exception.Message;
            }
        }

        JobRow[] rows = [.. page.Items.Select(job =>
        {
            (string? description, bool? truncated) = McpText.Cut(job.Description, McpLimits.MaxDescriptionLength);
            return new JobRow(job.Group, job.Name, description, job.Durable)
            {
                DescriptionTruncated = truncated,
                LastRun = statuses.TryGetValue(new JobKey(job.Name, job.Group), out JobRunStatusDto? status)
                    ? new LastRunRow(status.LastResult, status.LastFiredAtUtc, status.ConsecutiveFailures > 0)
                    : null,
            };
        })];

        return new ListJobsAnswer(call.Now(), await call.Freshness(cancellationToken).ConfigureAwait(false), rows, page.HasMore)
        {
            RunStatusNote = note
        };
    }
}

internal sealed class GetJobTool()
    : QuartzTool<JobArguments, JobAnswer>(
        "get_job",
        "Get a job",
        "Says what one job is: its type, description and flags, the keys of its job data (never the values), its triggers with their schedules, and its run status.",
        [SchedulerRoutes.GetJobDetails, SchedulerRoutes.GetJobTriggers, SchedulerRoutes.GetJobRunStatus],
        McpJsonContext.Default.JobArguments,
        McpJsonContext.Default.JobAnswer)
{
    protected override async ValueTask<JobAnswer> Run(ToolCall call, JobArguments arguments, CancellationToken cancellationToken)
    {
        JobKey jobKey = arguments.AsJobKey();
        JobDetailDto job = await SchedulerOperations.GetJobDetails(call.Scheduler, jobKey, cancellationToken).ConfigureAwait(false)
                           ?? throw new McpToolException(McpErrorCodes.NotFound, $"Unknown job {jobKey}");

        List<ITrigger> triggers = await SchedulerOperations.GetJobTriggers(call.Scheduler, jobKey, cancellationToken).ConfigureAwait(false);
        JobTriggerRow[] triggerRows = [.. triggers.Select(static trigger => new JobTriggerRow(
            trigger.Key.Group,
            trigger.Key.Name,
            McpText.Kind(trigger),
            McpText.Schedule(trigger),
            trigger.NextFireTimeUtc,
            trigger.PreviousFireTimeUtc))];

        RunStatusRow? runStatus = null;
        string? note = null;
        try
        {
            JobRunStatusDto? status = await SchedulerOperations.GetJobRunStatus(call.Scheduler, call.History(), jobKey, cancellationToken).ConfigureAwait(false);
            runStatus = status is null ? null : Rows.RunStatus(status);
        }
        catch (Exception exception) when (exception is NotSupportedException or McpToolException)
        {
            note = "This scheduler's history keeps no per-job run status: " + exception.Message;
        }

        (string? description, bool? truncated) = McpText.Cut(job.Description, McpLimits.MaxDescriptionLength);
        return new JobAnswer(
            call.Now(),
            await call.Freshness(cancellationToken).ConfigureAwait(false),
            job.Group,
            job.Name,
            job.JobType,
            description,
            job.Durable,
            job.RequestsRecovery,
            job.ConcurrentExecutionDisallowed,
            job.PersistJobDataAfterExecution,
            McpText.DataKeys(job.JobDataMap),
            triggerRows)
        {
            DescriptionTruncated = truncated,
            RunStatus = runStatus,
            RunStatusNote = note,
        };
    }
}

internal sealed class ListTriggersTool()
    : QuartzTool<ListTriggersArguments, ListTriggersAnswer>(
        "list_triggers",
        "List triggers",
        "Lists a scheduler's triggers, a page at a time, with each one's job, state, schedule in words and expression, next and previous fire times, and who paused it and why when it is paused. Filter by state (Error lists the triggers that will not fire until reset), by key, by job or by next fire time.",
        [SchedulerRoutes.QueryTriggers],
        McpJsonContext.Default.ListTriggersArguments,
        McpJsonContext.Default.ListTriggersAnswer)
{
    protected override async ValueTask<ListTriggersAnswer> Run(ToolCall call, ListTriggersArguments arguments, CancellationToken cancellationToken)
    {
        ListingParameters listing = Listings.Page(arguments.Skip, arguments.Take) with
        {
            GroupContains = arguments.GroupContains,
            NameContains = arguments.NameContains,
        };

        JobKey? job = arguments.Job is { } key ? new JobKey(key.Name, key.Group) : null;
        PagedResultDto<TriggerHeaderDto> page = await SchedulerOperations.QueryTriggers(
            call.Scheduler, listing, job, calendarName: null, arguments.State, arguments.NextFireBefore, cancellationToken).ConfigureAwait(false);

        TriggerRow[] rows = [.. page.Items.Select(static trigger =>
        {
            (string? description, bool? truncated) = McpText.Cut(trigger.Description, McpLimits.MaxDescriptionLength);
            return new TriggerRow(
                trigger.Group,
                trigger.Name,
                new McpKey(trigger.JobGroup, trigger.JobName),
                trigger.State,
                trigger.ScheduleDescription,
                trigger.ScheduleExpression,
                trigger.NextFireTimeUtc,
                trigger.PreviousFireTimeUtc)
            {
                Paused = McpText.Pause(trigger.Pause?.AsPauseInfo()),
                Description = description,
                DescriptionTruncated = truncated,
            };
        })];

        return new ListTriggersAnswer(call.Now(), await call.Freshness(cancellationToken).ConfigureAwait(false), rows, page.HasMore);
    }
}

internal sealed class GetTriggerTool()
    : QuartzTool<TriggerArguments, TriggerAnswer>(
        "get_trigger",
        "Get a trigger",
        "Says what one trigger is: its kind, schedule in words and expression, time zone, misfire instruction, calendar and priority, its state and pause, its next five fire times, and the keys of its data (never the values).",
        [SchedulerRoutes.GetTrigger, SchedulerRoutes.GetTriggerState, SchedulerRoutes.GetTriggerFireTimes],
        McpJsonContext.Default.TriggerArguments,
        McpJsonContext.Default.TriggerAnswer)
{
    protected override async ValueTask<TriggerAnswer> Run(ToolCall call, TriggerArguments arguments, CancellationToken cancellationToken)
    {
        TriggerKey triggerKey = arguments.AsTriggerKey();
        ITrigger trigger = await SchedulerOperations.GetTrigger(call.Scheduler, triggerKey, cancellationToken).ConfigureAwait(false)
                           ?? throw new McpToolException(McpErrorCodes.NotFound, $"Unknown trigger {triggerKey}");

        TriggerStateDto state = await SchedulerOperations.GetTriggerState(call.Scheduler, triggerKey, cancellationToken).ConfigureAwait(false);

        DateTimeOffset[] fireTimes = [];
        try
        {
            TriggerFireTimesResponse? upcoming = await SchedulerOperations.GetTriggerFireTimes(call.Scheduler, triggerKey, McpLimits.UpcomingFireTimes, cancellationToken).ConfigureAwait(false);
            fireTimes = upcoming?.FireTimesUtc ?? [];
        }
        catch (SchedulerException)
        {
            // A calendar it names that is gone: the trigger is still described, with no fire times to show.
        }

        (string? description, bool? truncated) = McpText.Cut(trigger.Description, McpLimits.MaxDescriptionLength);
        return new TriggerAnswer(
            call.Now(),
            await call.Freshness(cancellationToken).ConfigureAwait(false),
            trigger.Key.Group,
            trigger.Key.Name,
            McpKey.Of(trigger.JobKey),
            McpText.Kind(trigger),
            McpText.Schedule(trigger),
            McpText.Expression(trigger),
            McpText.TimeZone(trigger),
            McpText.MisfireInstructionName(trigger),
            string.IsNullOrWhiteSpace(trigger.CalendarName) ? null : trigger.CalendarName,
            trigger.Priority,
            state.State,
            fireTimes,
            McpText.DataKeys(trigger.JobDataMap))
        {
            Paused = McpText.Pause(state.Pause?.AsPauseInfo()),
            Description = description,
            DescriptionTruncated = truncated,
            EndTimeUtc = trigger.EndTimeUtc,
        };
    }
}

internal sealed class ListExecutingTool()
    : QuartzTool<ListExecutingArguments, ListExecutingAnswer>(
        "list_executing",
        "List running jobs",
        "Lists the firings running now, across the cluster where the store is shared, with each one's fire instance id (what interrupt takes), job, trigger, node, when it fired and how long it has run.",
        [SchedulerRoutes.QueryFireInstances],
        McpJsonContext.Default.ListExecutingArguments,
        McpJsonContext.Default.ListExecutingAnswer)
{
    protected override async ValueTask<ListExecutingAnswer> Run(ToolCall call, ListExecutingArguments arguments, CancellationToken cancellationToken)
    {
        ListingParameters listing = Listings.Page(arguments.Skip, arguments.Take, nameof(FireInstanceState.Executing));
        PagedResultDto<FireInstanceDto> page = await SchedulerOperations.QueryFireInstances(
            call.Scheduler, listing, jobName: null, jobGroup: null, schedulerInstanceId: null, cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = call.Now();
        ExecutingRow[] rows = [.. page.Items.Select(firing => new ExecutingRow(
            firing.FireInstanceId,
            new McpKey(firing.TriggerGroup, firing.TriggerName),
            firing.SchedulerInstanceId,
            firing.FireTimeUtc,
            Math.Max(0, (long) (now - firing.FireTimeUtc).TotalSeconds))
        {
            Job = firing.JobGroup is not null && firing.JobName is not null ? new McpKey(firing.JobGroup, firing.JobName) : null,
            InterruptRequested = firing.InterruptRequestedUtc is not null ? true : null,
        })];

        return new ListExecutingAnswer(call.Now(), await call.Freshness(cancellationToken).ConfigureAwait(false), rows, page.HasMore);
    }
}

internal sealed class QueryHistoryTool()
    : QuartzTool<QueryHistoryArguments, HistoryAnswer>(
        "query_history",
        "Query run history",
        "Lists what a scheduler has run, newest first, a page at a time: each run's job, trigger, when it fired, how long it took, its result, its retry attempt, and its error message and summary, cut to a length. A run's recorded input and captured log are never answered.",
        [SchedulerRoutes.QueryExecutionHistory],
        McpJsonContext.Default.QueryHistoryArguments,
        McpJsonContext.Default.HistoryAnswer)
{
    protected override async ValueTask<HistoryAnswer> Run(ToolCall call, QueryHistoryArguments arguments, CancellationToken cancellationToken)
    {
        HistoryParameters filters = new()
        {
            JobGroup = arguments.Job?.Group,
            JobName = arguments.Job?.Name,
            TriggerGroup = arguments.Trigger?.Group,
            TriggerName = arguments.Trigger?.Name,
            FiredFrom = arguments.FiredFrom,
            FiredBefore = arguments.FiredBefore,
            Results = arguments.Results is { Length: > 0 } results ? [.. results.Select(static result => result.ToString())] : null,
        };

        PagedResultDto<ExecutionHistoryEntryDto> page = await SchedulerOperations.QueryExecutionHistory(
            call.Scheduler,
            call.History(),
            Listings.Page(arguments.Skip, arguments.Take),
            schedulerInstanceId: null,
            jobContains: null,
            triggerContains: null,
            arguments.FailedFinally,
            filters,
            cancellationToken).ConfigureAwait(false);

        HistoryRow[] rows = [.. page.Items.Select(static entry =>
        {
            (string? error, bool? errorTruncated) = McpText.Cut(entry.ExceptionMessage, McpLimits.MaxErrorMessageLength);
            (string? summary, bool? summaryTruncated) = McpText.Cut(entry.Summary, McpLimits.MaxSummaryLength);
            return new HistoryRow(
                new McpKey(entry.JobGroup, entry.JobName),
                new McpKey(entry.TriggerGroup, entry.TriggerName),
                entry.FiredAtUtc,
                Math.Round(entry.Duration.TotalSeconds, 3),
                entry.Result ?? (entry.Succeeded ? JobRunResult.Succeeded : JobRunResult.Failed),
                entry.RetryAttempt,
                entry.RetryScheduled,
                entry.SchedulerInstanceId)
            {
                EntryId = entry.EntryId,
                ErrorMessage = error,
                ErrorMessageTruncated = errorTruncated,
                Summary = summary,
                SummaryTruncated = summaryTruncated,
                Manual = entry.Manual ? true : null,
            };
        })];

        return new HistoryAnswer(call.Now(), await call.Freshness(cancellationToken).ConfigureAwait(false), rows, page.HasMore);
    }
}

internal sealed class GetJobRunStatusTool()
    : QuartzTool<JobArguments, JobRunStatusAnswer>(
        "get_job_run_status",
        "Get a job's run status",
        "Says how a job's runs have gone: its last run and result, its last success and failure, whether it is failing and for how many runs in a row, and its run and failure counts.",
        [SchedulerRoutes.GetJobRunStatus],
        McpJsonContext.Default.JobArguments,
        McpJsonContext.Default.JobRunStatusAnswer)
{
    protected override async ValueTask<JobRunStatusAnswer> Run(ToolCall call, JobArguments arguments, CancellationToken cancellationToken)
    {
        JobRunStatusDto? status = await SchedulerOperations.GetJobRunStatus(call.Scheduler, call.History(), arguments.AsJobKey(), cancellationToken).ConfigureAwait(false);
        return new JobRunStatusAnswer(call.Now(), await call.Freshness(cancellationToken).ConfigureAwait(false), arguments.Group, arguments.Name)
        {
            Status = status is null ? null : Rows.RunStatus(status)
        };
    }
}

/// <summary>
/// The pages and rows the read tools share.
/// </summary>
internal static class Listings
{
    /// <summary>
    /// The page the arguments asked for, read by the catalogue's own rules, at most <see cref="McpLimits.MaxTake" />.
    /// </summary>
    public static ListingParameters Page(int skip, int take, string? state = null)
    {
        return ListingParameters.Read(skip, take.ToString(CultureInfo.InvariantCulture), includeTotalCount: false, McpLimits.MaxTake, state);
    }
}

internal static class Rows
{
    public static RunStatusRow RunStatus(JobRunStatusDto status)
    {
        (string? message, bool? truncated) = McpText.Cut(status.LastFailureMessage, McpLimits.MaxErrorMessageLength);
        return new RunStatusRow(
            status.LastFiredAtUtc,
            status.LastResult,
            status.LastSucceededAtUtc,
            status.LastFailedAtUtc,
            status.ConsecutiveFailures > 0,
            status.ConsecutiveFailures,
            status.RunCount,
            status.FailureCount)
        {
            LastFailureMessage = message,
            LastFailureMessageTruncated = truncated,
        };
    }
}
