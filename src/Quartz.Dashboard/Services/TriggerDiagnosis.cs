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

using Quartz.Dashboard.Components.Shared;
using Quartz.Extensibility;

namespace Quartz.Dashboard.Services;

/// <summary>
/// "Why isn't it firing?": every check an operator works through for one trigger, each with a verdict and,
/// where there is one, the link or the action that answers it (#4411).
/// </summary>
/// <remarks>
/// <para>
/// Two halves, so the rules are tested without a page. <see cref="Read" /> makes a fixed set of reads, about
/// eight, on load and on Refresh: never on a timer and never per row. <see cref="Evaluate" /> turns what they
/// answered into the checks, and reads nothing.
/// </para>
/// <para>
/// What the dashboard cannot know it does not claim. Nodes publish nothing about what they serve, so the
/// execution-group check speaks for the node that answered only: "this node does not serve it", never "no
/// node does". A window takes the scheduler's status from the cluster's check-ins, as
/// <see cref="IQuartzApiClient.GetScheduler" /> reports it for one. A read that fails leaves its check
/// unknown and the others standing.
/// </para>
/// </remarks>
internal static class TriggerDiagnosis
{
    /// <summary>
    /// How far past its fire time a waiting trigger has to be before it is called overdue: five minutes, the
    /// execution-groups panel's cutoff. "Due" is a waiting trigger past its fire time, as the due-backlog gauges
    /// of #4401 count it, so the two agree.
    /// </summary>
    public static readonly TimeSpan OverdueAfter = TimeSpan.FromMinutes(5);

    /// <summary>How many firings of the job the diagnosis lists at most.</summary>
    private const int FiringsShown = 10;

    /// <summary>
    /// Reads what the checks need, from whichever source the dashboard is on: in process, over HTTP, through an
    /// agent or a window.
    /// </summary>
    /// <param name="api">The data source.</param>
    /// <param name="subject">The trigger, and what the page has read about it already.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static async Task<TriggerDiagnosisFacts> Read(IQuartzApiClient api, DiagnosisSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(subject);

        string schedulerName = subject.SchedulerName;
        ITrigger trigger = subject.Trigger;
        JobKeyDto job = new(trigger.JobKey.Group, trigger.JobKey.Name);
        TriggerKeyDto triggerKey = new(trigger.Key.Group, trigger.Key.Name);

        Answer<SchedulerDetailDto> scheduler = await Ask(() => api.GetScheduler(schedulerName, cancellationToken), cancellationToken).ConfigureAwait(false);

        Answer<HashSet<string>> pausedTriggerGroups = await Ask(
            async () => (await api.QueryTriggerGroups(schedulerName, new DashboardGroupQuery { Paused = true }, cancellationToken).ConfigureAwait(false))
                .Items.Select(static group => group.Name).ToHashSet(StringComparer.Ordinal),
            cancellationToken).ConfigureAwait(false);

        Answer<HashSet<string>> pausedJobGroups = await Ask(
            async () => (await api.QueryJobGroups(schedulerName, new DashboardGroupQuery { Paused = true }, cancellationToken).ConfigureAwait(false))
                .Items.Select(static group => group.Name).ToHashSet(StringComparer.Ordinal),
            cancellationToken).ConfigureAwait(false);

        // Only a paused group has a pause to explain.
        Answer<PauseInfo?> triggerGroupPause = pausedTriggerGroups.Value?.Contains(triggerKey.Group) == true
            ? await Ask(() => api.GetTriggerGroupPause(schedulerName, triggerKey.Group, cancellationToken), cancellationToken).ConfigureAwait(false)
            : default;

        Answer<PauseInfo?> jobGroupPause = pausedJobGroups.Value?.Contains(job.Group) == true
            ? await Ask(() => api.GetJobGroupPause(schedulerName, job.Group, cancellationToken), cancellationToken).ConfigureAwait(false)
            : default;

        Answer<PagedResult<FireInstanceDto>> jobFirings = await Ask(
            () => api.QueryFireInstances(schedulerName, new DashboardFireInstanceQuery { Job = job, State = null, Take = FiringsShown }, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        // A trigger whose own firing is running reads Executing, which outranks Blocked (#1416), so a serial job's
        // trigger can be held while it never shows BLOCKED. Whether the running firing holds the trigger's next
        // occurrence depends on the job not allowing concurrent execution, which only the job says: one read, made
        // only when a firing of the job is running and the trigger's next fire time has passed.
        Answer<JobDetailDto> jobDetail = subject.State != TriggerState.Blocked
            && jobFirings.Value is { Items.Count: > 0 }
            && trigger.NextFireTimeUtc is { } due
            && due <= subject.Now
            ? await Ask(() => api.GetJobDetail(schedulerName, job, cancellationToken), cancellationToken).ConfigureAwait(false)
            : default;

        Answer<ExecutionLimitsDto> limits = await Ask(() => api.GetExecutionLimits(schedulerName, cancellationToken), cancellationToken).ConfigureAwait(false);

        // What the trigger's execution group has in flight, asked only when a finite limit governs it: one more
        // read, of the page of firings the execution-groups panel counts.
        Answer<PagedResult<FireInstanceDto>> groupFirings = default;
        if (limits.Value is { CanReport: true } known
            && ExecutionLimitLookup.Resolve(known, GroupKeyOf(trigger, known), out _) is { MaxConcurrent: > 0 })
        {
            groupFirings = await Ask(
                () => api.QueryFireInstances(schedulerName, new DashboardFireInstanceQuery { State = null }, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        Answer<PagedResult<DashboardMisfireEntry>> misfires = await Ask(
            () => api.QueryMisfires(new DashboardMisfireQuery { SchedulerName = schedulerName, Trigger = triggerKey, Take = 1, IncludeTotalCount = true }, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        // Only a trigger in error has a failure to explain, and its last run is the likeliest account of it.
        Answer<PagedResult<DashboardHistoryEntry>> lastRun = subject.State == TriggerState.Error
            ? await Ask(() => api.QueryExecutions(new DashboardHistoryQuery { SchedulerName = schedulerName, Trigger = triggerKey, Take = 1 }, cancellationToken), cancellationToken).ConfigureAwait(false)
            : default;

        return new TriggerDiagnosisFacts(subject)
        {
            Scheduler = scheduler,
            PausedTriggerGroups = pausedTriggerGroups,
            PausedJobGroups = pausedJobGroups,
            TriggerGroupPause = triggerGroupPause,
            JobGroupPause = jobGroupPause,
            JobFirings = jobFirings,
            JobDetail = jobDetail,
            Limits = limits,
            GroupFirings = groupFirings,
            Misfires = misfires,
            LastRun = lastRun,
        };
    }

    /// <summary>
    /// The checks, in the order an operator works through them, each with its verdict.
    /// </summary>
    /// <param name="facts">What <see cref="Read" /> answered.</param>
    /// <param name="formatTime">How the page writes an instant: in the time zone the visitor picked.</param>
    public static List<DiagnosisCheck> Evaluate(TriggerDiagnosisFacts facts, Func<DateTimeOffset?, string> formatTime)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(formatTime);

        return
        [
            CheckScheduler(facts),
            CheckTrigger(facts, formatTime),
            CheckTriggerGroup(facts, formatTime),
            CheckJobGroup(facts, formatTime),
            CheckFirings(facts, formatTime),
            CheckNextFire(facts, formatTime),
            CheckExecutionGroup(facts),
            CheckMisfires(facts, formatTime),
            CheckRetry(facts, formatTime),
        ];
    }

    private static DiagnosisCheck CheckScheduler(TriggerDiagnosisFacts facts)
    {
        const string Name = "Scheduler";
        if (facts.Scheduler.Value is not { } scheduler)
        {
            return Unread(Name, facts.Scheduler);
        }

        bool window = facts.Subject.IsWindow;
        return scheduler.Status switch
        {
            SchedulerStatus.Running => new DiagnosisCheck(Name, DiagnosisVerdict.Ok,
                window ? "Running: a node of the cluster checked in within its interval." : "Running."),
            SchedulerStatus.Standby => new DiagnosisCheck(Name, DiagnosisVerdict.Problem,
                "In standby: nothing fires until it is started.") { LinkText = "Overview", LinkPath = "" },
            SchedulerStatus.Created => new DiagnosisCheck(Name, DiagnosisVerdict.Problem,
                "Not started: nothing fires until it is.") { LinkText = "Overview", LinkPath = "" },
            SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown => new DiagnosisCheck(Name, DiagnosisVerdict.Problem,
                window ? "No node of the cluster is checking in: nothing fires until one starts." : "Shut down: nothing fires here again."),
            _ => new DiagnosisCheck(Name, DiagnosisVerdict.Unknown,
                window
                    ? "The store holds no check-in row for this scheduler, so whether a node runs it cannot be told from here."
                    : "Its status is unknown."),
        };
    }

    private static DiagnosisCheck CheckTrigger(TriggerDiagnosisFacts facts, Func<DateTimeOffset?, string> formatTime)
    {
        const string Name = "Trigger";
        DiagnosisSubject subject = facts.Subject;
        ITrigger trigger = subject.Trigger;

        switch (subject.State)
        {
            case TriggerState.Normal:
                return new DiagnosisCheck(Name, DiagnosisVerdict.Ok, "Waiting for its next fire time.");
            case TriggerState.Executing:
                return new DiagnosisCheck(Name, DiagnosisVerdict.Ok, "Firing now.");
            case TriggerState.Paused:
                return new DiagnosisCheck(Name, DiagnosisVerdict.Problem, "Paused" + PauseText(subject.Pause, formatTime) + ".");
            case TriggerState.Blocked:
                return new DiagnosisCheck(Name, DiagnosisVerdict.Problem,
                    "Blocked: its job does not allow concurrent execution, and a firing of it holds the job.");
            case TriggerState.Complete:
                return new DiagnosisCheck(Name, DiagnosisVerdict.Problem, "Complete: it will not fire again.");
            case TriggerState.Awaiting when trigger.Continuation.Parent is { } parent:
                return new DiagnosisCheck(Name, DiagnosisVerdict.Info,
                    "Waits for a firing of " + DisplayValueHelper.FormatKey(parent.Group, parent.Name) + " to end: "
                    + DisplayValueHelper.FormatContinuationCondition(trigger.Continuation.When) + ".")
                {
                    LinkText = "The trigger it waits for",
                    LinkPath = TriggerPath(parent.Group, parent.Name),
                };
            case TriggerState.Awaiting:
                return new DiagnosisCheck(Name, DiagnosisVerdict.Info, "Waits for the firing it continues after to end.");
            case TriggerState.Error:
                return ErrorCheck(facts, formatTime);
            default:
                return new DiagnosisCheck(Name, DiagnosisVerdict.Unknown, "Its state is " + subject.State + ".");
        }
    }

    private static DiagnosisCheck ErrorCheck(TriggerDiagnosisFacts facts, Func<DateTimeOffset?, string> formatTime)
    {
        const string Name = "Trigger";
        ITrigger trigger = facts.Subject.Trigger;
        if (trigger.Continuation.Parent is { } parent && !facts.Subject.ContinuationParentExists)
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Problem,
                "In error: the trigger it continues after, " + DisplayValueHelper.FormatKey(parent.Group, parent.Name)
                + ", was deleted.")
            {
                Action = DiagnosisAction.ResetError,
            };
        }

        string text = "In error: its job could not be built, or its firing failed five times in a row.";
        DiagnosisCheck check = new(Name, DiagnosisVerdict.Problem, text) { Action = DiagnosisAction.ResetError };

        if (facts.LastRun.Value?.Items is [{ Succeeded: false } run, ..])
        {
            string failure = run.ExceptionMessage is { Length: > 0 } message ? ": " + message : ".";
            check = check with
            {
                Text = text + " Its last run, at " + formatTime(run.FiredAtUtc) + ", failed" + failure,
                LinkText = run.EntryId is null ? null : "The failed run",
                LinkPath = run.EntryId is { } entryId ? "history/" + Uri.EscapeDataString(entryId) : null,
            };
        }

        return check;
    }

    private static DiagnosisCheck CheckTriggerGroup(TriggerDiagnosisFacts facts, Func<DateTimeOffset?, string> formatTime)
    {
        const string Name = "Trigger group";
        if (facts.PausedTriggerGroups.Value is not { } paused)
        {
            return Unread(Name, facts.PausedTriggerGroups);
        }

        string group = facts.Subject.Trigger.Key.Group;
        if (!paused.Contains(group))
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Ok, Quote(group) + " is not paused.");
        }

        return new DiagnosisCheck(
            Name,
            facts.Subject.State == TriggerState.Paused ? DiagnosisVerdict.Problem : DiagnosisVerdict.Info,
            Quote(group) + " is paused" + PauseText(facts.TriggerGroupPause.Value, formatTime)
            + ": a trigger stored into it is stored paused, and resuming the group resumes its triggers.")
        {
            LinkText = "Triggers in the group",
            LinkPath = "triggers?group=" + Uri.EscapeDataString(group),
        };
    }

    private static DiagnosisCheck CheckJobGroup(TriggerDiagnosisFacts facts, Func<DateTimeOffset?, string> formatTime)
    {
        const string Name = "Job group";
        if (facts.PausedJobGroups.Value is not { } paused)
        {
            return Unread(Name, facts.PausedJobGroups);
        }

        string group = facts.Subject.Trigger.JobKey.Group;
        if (!paused.Contains(group))
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Ok, Quote(group) + " is not paused.");
        }

        return new DiagnosisCheck(
            Name,
            facts.Subject.State == TriggerState.Paused ? DiagnosisVerdict.Problem : DiagnosisVerdict.Info,
            Quote(group) + " is paused" + PauseText(facts.JobGroupPause.Value, formatTime)
            + ": its jobs' triggers are paused, and a trigger stored later for one of them is stored paused.")
        {
            LinkText = "Jobs in the group",
            LinkPath = "jobs?group=" + Uri.EscapeDataString(group),
        };
    }

    private static DiagnosisCheck CheckFirings(TriggerDiagnosisFacts facts, Func<DateTimeOffset?, string> formatTime)
    {
        const string Name = "Its job's firings";
        if (facts.JobFirings.Value is not { } page)
        {
            return Unread(Name, facts.JobFirings);
        }

        bool blocked = facts.Subject.State == TriggerState.Blocked;
        if (page.Items.Count == 0)
        {
            return blocked
                ? new DiagnosisCheck(Name, DiagnosisVerdict.Warning,
                    "None is running, so nothing holds the job now: the block clears when the firing that held it is completed or recovered. Refresh to check.")
                : new DiagnosisCheck(Name, DiagnosisVerdict.Ok, "None running.");
        }

        FireInstanceDto oldest = page.Items.MinBy(static firing => firing.FireTimeUtc)!;
        int count = page.TotalCount ?? page.Items.Count;

        // Not BLOCKED, yet held all the same: the trigger reads Executing while a firing of its own runs, and a job
        // that does not allow concurrent execution keeps its next occurrence waiting once that is due.
        bool held = !blocked
            && facts.JobDetail.Value?.ConcurrentExecutionDisallowed == true
            && facts.Subject.Trigger.NextFireTimeUtc is { } next
            && next <= facts.Subject.Now;

        string text = held
            ? "Held by its job's running firing " + oldest.FireInstanceId + " on node " + oldest.SchedulerInstanceId
              + " since " + formatTime(oldest.FireTimeUtc) + ": the job does not allow concurrent execution, and this "
              + "trigger's next fire time, " + formatTime(facts.Subject.Trigger.NextFireTimeUtc) + ", has passed."
            : (count == 1 ? "One running" : count.ToString(CultureInfo.InvariantCulture) + " running")
              + ": on node " + oldest.SchedulerInstanceId + " since " + formatTime(oldest.FireTimeUtc)
              + (count > 1 ? ", the oldest" : string.Empty) + ".";

        if (oldest.InterruptRequestedUtc is { } requested)
        {
            text += " An interrupt was requested at " + formatTime(requested) + ".";
        }

        bool holds = blocked || held;
        return new DiagnosisCheck(Name, holds ? DiagnosisVerdict.Problem : DiagnosisVerdict.Info, text)
        {
            LinkText = "Currently Executing",
            LinkPath = "executing",
            Action = holds ? DiagnosisAction.Interrupt : DiagnosisAction.None,
            FireInstanceId = holds ? oldest.FireInstanceId : null,
        };
    }

    private static DiagnosisCheck CheckNextFire(TriggerDiagnosisFacts facts, Func<DateTimeOffset?, string> formatTime)
    {
        const string Name = "Next fire";
        DiagnosisSubject subject = facts.Subject;
        ITrigger trigger = subject.Trigger;
        DateTimeOffset now = subject.Now;

        if (trigger.NextFireTimeUtc is not { } next)
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Problem,
                trigger.EndTimeUtc is { } end && end <= now
                    ? "None: its end time, " + formatTime(end) + ", has passed."
                    : "None: its schedule has no fire time left.");
        }

        if (trigger.StartTimeUtc > now)
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Info,
                formatTime(next) + ", not before its start time, " + formatTime(trigger.StartTimeUtc) + ".");
        }

        if (subject.State == TriggerState.Normal && next < now - OverdueAfter)
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Warning,
                "Overdue: it was due at " + formatTime(next) + ", " + DisplayValueHelper.FormatSince(next, now)
                + ", and has not fired.");
        }

        // Due and not yet five minutes late, as a held trigger's often is: said as due, not as still ahead.
        return new DiagnosisCheck(Name, DiagnosisVerdict.Ok, next <= now
            ? formatTime(next) + " (due " + DisplayValueHelper.FormatSince(next, now) + ")."
            : formatTime(next) + " (" + DisplayValueHelper.FormatUntil(next, now) + ").");
    }

    private static DiagnosisCheck CheckExecutionGroup(TriggerDiagnosisFacts facts)
    {
        const string Name = "Execution group";
        DiagnosisSubject subject = facts.Subject;
        ITrigger trigger = subject.Trigger;
        string label = trigger.ExecutionGroup is { Length: > 0 } group ? Quote(group) : "No execution group";

        // What the answering node routes. A window runs nothing and a cluster is several nodes, so neither
        // speaks for a node; and no node publishes what it serves, so this is never "no node serves it".
        if (!subject.IsWindow && !subject.IsCluster
            && facts.Scheduler.Value is { RoutesExecutionGroups: true } scheduler
            && ExecutionGroupRouting.FromSpellings(scheduler.ServedExecutionGroups, scheduler.RefusedExecutionGroups) is { } routing
            && !routing.Serves(trigger.ExecutionGroup))
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Warning,
                label + ": this node does not serve it, so the trigger fires on a node that does.")
            {
                LinkText = "Cluster",
                LinkPath = "cluster",
            };
        }

        if (facts.Limits.Value is not { } limits)
        {
            return Unread(Name, facts.Limits);
        }

        if (!limits.CanReport)
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Unknown, label + ": this source cannot report execution limits.");
        }

        string key = GroupKeyOf(trigger, limits);
        if (ExecutionLimitLookup.Resolve(limits, key, out _) is not { MaxConcurrent: int max } limit)
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Ok, label + ": unlimited.");
        }

        if (max == 0)
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Problem, label + ": its limit is 0, so nothing in it runs.");
        }

        bool perNode = limit.Scope == ExecutionLimitScope.Node;
        string scope = perNode ? " per node" : " across the cluster";
        if (facts.GroupFirings.Value is not { } firings || (perNode && subject.IsWindow))
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Ok, label + ": limited to " + Number(max) + scope + ".");
        }

        string? answeringNode = facts.Scheduler.Value?.SchedulerInstanceId;
        int inFlight = firings.Items.Count(firing =>
            ExecutionLimitLookup.KeyOf(firing.ExecutionGroup, firing.TriggerKey.Group, limits.UsesTriggerGroupWhenUnset) == key
            && (!perNode || string.Equals(firing.SchedulerInstanceId, answeringNode, StringComparison.Ordinal)));
        string where = perNode ? " on this node" : string.Empty;

        return inFlight >= max
            ? new DiagnosisCheck(Name, DiagnosisVerdict.Warning,
                label + ": at its limit, " + Number(inFlight) + " of " + Number(max) + scope + " in flight" + where + ", so the trigger waits for a slot.")
            {
                LinkText = "Currently Executing",
                LinkPath = "executing",
            }
            : new DiagnosisCheck(Name, DiagnosisVerdict.Ok,
                label + ": " + Number(inFlight) + " of " + Number(max) + scope + " in flight" + where + ".");
    }

    private static DiagnosisCheck CheckMisfires(TriggerDiagnosisFacts facts, Func<DateTimeOffset?, string> formatTime)
    {
        const string Name = "Misfires";
        if (facts.Misfires.Value is not { } page)
        {
            return facts.Misfires.Unsupported
                ? new DiagnosisCheck(Name, DiagnosisVerdict.Unknown, "This source keeps no misfire history.")
                : Unread(Name, facts.Misfires);
        }

        int count = page.TotalCount ?? page.Items.Count;
        if (count == 0 || page.Items.Count == 0)
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Ok, "None recorded.");
        }

        DashboardMisfireEntry latest = page.Items[0];
        ITrigger trigger = facts.Subject.Trigger;
        return new DiagnosisCheck(Name, DiagnosisVerdict.Warning,
            Number(count) + " recorded; the latest at " + formatTime(latest.MisfiredAtUtc) + ", " + latest.Reason + ".")
        {
            LinkText = "Its misfires",
            LinkPath = "history?triggerGroup=" + Uri.EscapeDataString(trigger.Key.Group) + "&triggerName=" + Uri.EscapeDataString(trigger.Key.Name) + "#misfires",
        };
    }

    private static DiagnosisCheck CheckRetry(TriggerDiagnosisFacts facts, Func<DateTimeOffset?, string> formatTime)
    {
        const string Name = "Retry";
        ITrigger trigger = facts.Subject.Trigger;
        if (trigger.RetryAttempt <= 0)
        {
            return new DiagnosisCheck(Name, DiagnosisVerdict.Ok, "None pending.");
        }

        string due = trigger.NextFireTimeUtc is { } next ? ", due at " + formatTime(next) : string.Empty;
        return new DiagnosisCheck(Name, DiagnosisVerdict.Info,
            "Retry " + Number(trigger.RetryAttempt) + " of a failed occurrence is pending" + due + ".");
    }

    private static string GroupKeyOf(ITrigger trigger, ExecutionLimitsDto limits)
    {
        return ExecutionLimitLookup.KeyOf(trigger.ExecutionGroup, trigger.Key.Group, limits.UsesTriggerGroupWhenUnset);
    }

    private static string PauseText(PauseInfo? pause, Func<DateTimeOffset?, string> formatTime)
    {
        if (pause is null)
        {
            return string.Empty;
        }

        string by = pause.RequestedBy is { Length: > 0 } requester ? " by " + requester : string.Empty;
        string why = pause.Reason is { Length: > 0 } reason ? " (" + reason + ")" : string.Empty;
        return by + " at " + formatTime(pause.PausedAtUtc) + why;
    }

    private static DiagnosisCheck Unread<T>(string name, Answer<T> answer)
    {
        return new DiagnosisCheck(name, DiagnosisVerdict.Unknown,
            answer.Error is { Length: > 0 } error ? "Could not be read: " + error : "Could not be read.");
    }

    private static string TriggerPath(string group, string name)
    {
        return "triggers/" + Uri.EscapeDataString(group) + "/" + Uri.EscapeDataString(name);
    }

    private static string Quote(string group) => "'" + group + "'";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// One read, its failure kept as the reason its check is unknown rather than failing the panel. A
    /// cancellation is the page going away, and goes on.
    /// </summary>
    private static async Task<Answer<T>> Ask<T>(Func<ValueTask<T>> read, CancellationToken cancellationToken)
    {
        try
        {
            return new Answer<T>(await read().ConfigureAwait(false), Error: null, Unsupported: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NotSupportedException ex)
        {
            return new Answer<T>(default, ex.Message, Unsupported: true);
        }
        catch (Exception ex)
        {
            return new Answer<T>(default, ex.Message, Unsupported: false);
        }
    }
}

/// <summary>
/// The trigger a diagnosis is about, and what its page has already read about it.
/// </summary>
/// <param name="SchedulerName">The scheduler's key.</param>
/// <param name="Trigger">The trigger.</param>
/// <param name="State">Its state.</param>
/// <param name="Pause">Its own pause, when it is paused and the pause recorded something.</param>
/// <param name="ContinuationParentExists">Whether the trigger it continues after is still scheduled.</param>
/// <param name="Now">When the page read it.</param>
internal sealed record DiagnosisSubject(
    string SchedulerName,
    ITrigger Trigger,
    TriggerState State,
    PauseInfo? Pause,
    bool ContinuationParentExists,
    DateTimeOffset Now)
{
    /// <summary>Whether the scheduler is a window onto an attached store.</summary>
    public bool IsWindow { get; init; }

    /// <summary>Whether the scheduler is a cluster behind several targets.</summary>
    public bool IsCluster { get; init; }
}

/// <summary>
/// What one read answered: its value, or why there is none.
/// </summary>
/// <param name="Value">The answer, or the default when the read failed or was not made.</param>
/// <param name="Error">Why the read failed, or <see langword="null" />.</param>
/// <param name="Unsupported">Whether the source said it cannot answer this at all.</param>
internal readonly record struct Answer<T>(T? Value, string? Error, bool Unsupported);

/// <summary>
/// What <see cref="TriggerDiagnosis.Read" /> answered, which <see cref="TriggerDiagnosis.Evaluate" /> judges.
/// </summary>
internal sealed record TriggerDiagnosisFacts(DiagnosisSubject Subject)
{
    public Answer<SchedulerDetailDto> Scheduler { get; init; }

    public Answer<HashSet<string>> PausedTriggerGroups { get; init; }

    public Answer<HashSet<string>> PausedJobGroups { get; init; }

    public Answer<PauseInfo?> TriggerGroupPause { get; init; }

    public Answer<PauseInfo?> JobGroupPause { get; init; }

    public Answer<PagedResult<FireInstanceDto>> JobFirings { get; init; }

    /// <summary>The trigger's job, read only to learn whether a running firing of it holds the trigger.</summary>
    public Answer<JobDetailDto> JobDetail { get; init; }

    public Answer<ExecutionLimitsDto> Limits { get; init; }

    public Answer<PagedResult<FireInstanceDto>> GroupFirings { get; init; }

    public Answer<PagedResult<DashboardMisfireEntry>> Misfires { get; init; }

    public Answer<PagedResult<DashboardHistoryEntry>> LastRun { get; init; }
}

/// <summary>How much a check's answer matters.</summary>
internal enum DiagnosisVerdict
{
    /// <summary>Nothing here holds the trigger back.</summary>
    Ok,

    /// <summary>Worth knowing, and not what holds it back.</summary>
    Info,

    /// <summary>May hold it back.</summary>
    Warning,

    /// <summary>Holds it back.</summary>
    Problem,

    /// <summary>Could not be told.</summary>
    Unknown
}

/// <summary>What a check offers to do about it.</summary>
internal enum DiagnosisAction
{
    None,

    /// <summary>Interrupt the firing that holds the job.</summary>
    Interrupt,

    /// <summary>Reset the trigger from its error state.</summary>
    ResetError
}

/// <summary>
/// One check of a diagnosis: what was checked, the verdict, and what to do about it.
/// </summary>
/// <param name="Name">What was checked.</param>
/// <param name="Verdict">The verdict.</param>
/// <param name="Text">The finding, in a sentence.</param>
internal sealed record DiagnosisCheck(string Name, DiagnosisVerdict Verdict, string Text)
{
    /// <summary>The link's text, when the check has one.</summary>
    public string? LinkText { get; init; }

    /// <summary>Where the link goes, relative to the dashboard's root.</summary>
    public string? LinkPath { get; init; }

    /// <summary>What the check offers to do; the page offers it only where the visitor's role allows it.</summary>
    public DiagnosisAction Action { get; init; }

    /// <summary>The firing <see cref="DiagnosisAction.Interrupt" /> interrupts.</summary>
    public string? FireInstanceId { get; init; }

    /// <summary>Whether the check found something that holds the trigger back or may.</summary>
    public bool HoldsBack => Verdict is DiagnosisVerdict.Problem or DiagnosisVerdict.Warning;
}
