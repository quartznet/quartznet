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

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;

namespace Quartz.Mcp.Impl.Tools;

// The seven tools an operator's role allows: pause, resume, trigger now, interrupt, reset from error. Each is one
// route of the catalogue, at that route's role, and answers whether it was applied.

internal sealed class PauseTriggerTool()
    : QuartzTool<PauseArguments, ChangeAnswer>(
        "pause_trigger",
        "Pause a trigger",
        "Pauses one trigger, so it fires nothing until it is resumed. The reason is required and is written on the trigger, with the caller's name, for whoever looks at it next.",
        [SchedulerRoutes.PauseTrigger],
        McpJsonContext.Default.PauseArguments,
        McpJsonContext.Default.ChangeAnswer,
        idempotent: true,
        destructive: false)
{
    protected override async ValueTask<ChangeAnswer> Run(ToolCall call, PauseArguments arguments, CancellationToken cancellationToken)
    {
        PauseDetails details = Pauses.Details(call, arguments);
        OperationAppliedResponse applied = await SchedulerOperations.PauseTrigger(call.Scheduler, new TriggerKey(arguments.Name, arguments.Group), details, cancellationToken).ConfigureAwait(false);
        return await ChangeAnswers.Answer(call, applied.Applied, applied.Applied ? null : "No trigger has that key, so nothing was paused.", cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class PauseJobTool()
    : QuartzTool<PauseArguments, ChangeAnswer>(
        "pause_job",
        "Pause a job",
        "Pauses every trigger of one job, so it runs nothing until it is resumed. The reason is required and is written on its triggers, with the caller's name, for whoever looks at them next.",
        [SchedulerRoutes.PauseJob],
        McpJsonContext.Default.PauseArguments,
        McpJsonContext.Default.ChangeAnswer,
        idempotent: true,
        destructive: false)
{
    protected override async ValueTask<ChangeAnswer> Run(ToolCall call, PauseArguments arguments, CancellationToken cancellationToken)
    {
        PauseDetails details = Pauses.Details(call, arguments);
        OperationAppliedResponse applied = await SchedulerOperations.PauseJob(call.Scheduler, new JobKey(arguments.Name, arguments.Group), details, cancellationToken).ConfigureAwait(false);
        return await ChangeAnswers.Answer(call, applied.Applied, applied.Applied ? null : "No job has that key, so nothing was paused.", cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ResumeTriggerTool()
    : QuartzTool<TriggerArguments, ChangeAnswer>(
        "resume_trigger",
        "Resume a trigger",
        "Resumes one paused trigger. The answer says what its misfire instruction does about the fire times it missed while paused.",
        [SchedulerRoutes.ResumeTrigger],
        McpJsonContext.Default.TriggerArguments,
        McpJsonContext.Default.ChangeAnswer,
        idempotent: true,
        destructive: false)
{
    protected override async ValueTask<ChangeAnswer> Run(ToolCall call, TriggerArguments arguments, CancellationToken cancellationToken)
    {
        TriggerKey triggerKey = arguments.AsTriggerKey();
        ITrigger? trigger = await SchedulerOperations.GetTrigger(call.Scheduler, triggerKey, cancellationToken).ConfigureAwait(false);
        OperationAppliedResponse applied = await SchedulerOperations.ResumeTrigger(call.Scheduler, triggerKey, cancellationToken).ConfigureAwait(false);

        string? note = trigger is null
            ? "No trigger has that key, so nothing was resumed."
            : McpText.MisfireOnResume(trigger);
        return await ChangeAnswers.Answer(call, applied.Applied, note, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ResumeJobTool()
    : QuartzTool<JobArguments, ChangeAnswer>(
        "resume_job",
        "Resume a job",
        "Resumes every trigger of one paused job. Each trigger's misfire instruction decides what happens to the fire times it missed while paused.",
        [SchedulerRoutes.ResumeJob],
        McpJsonContext.Default.JobArguments,
        McpJsonContext.Default.ChangeAnswer,
        idempotent: true,
        destructive: false)
{
    protected override async ValueTask<ChangeAnswer> Run(ToolCall call, JobArguments arguments, CancellationToken cancellationToken)
    {
        OperationAppliedResponse applied = await SchedulerOperations.ResumeJob(call.Scheduler, arguments.AsJobKey(), cancellationToken).ConfigureAwait(false);
        return await ChangeAnswers.Answer(call, applied.Applied, applied.Applied ? null : "No job has that key, so nothing was resumed.", cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class TriggerJobTool()
    : QuartzTool<JobArguments, ChangeAnswer>(
        "trigger_job",
        "Run a job now",
        "Runs one job once, now, with the job data it already has: no data is taken. Not idempotent: every call is another run.",
        [SchedulerRoutes.TriggerJob],
        McpJsonContext.Default.JobArguments,
        McpJsonContext.Default.ChangeAnswer,
        idempotent: false,
        destructive: false)
{
    protected override async ValueTask<ChangeAnswer> Run(ToolCall call, JobArguments arguments, CancellationToken cancellationToken)
    {
        JobKey jobKey = arguments.AsJobKey();

        // The one-off trigger is stored in the default group and fires nothing while that group or the job's own
        // group is paused: the store places it paused, as it places any trigger added to a paused group.
        bool storedPaused = await call.Scheduler.IsTriggerGroupPaused(TriggerKey.DefaultGroup, cancellationToken).ConfigureAwait(false)
                            || await call.Scheduler.IsJobGroupPaused(jobKey.Group, cancellationToken).ConfigureAwait(false);

        // No body: the run takes the job's own data, and supplying data is an administrator's (SchedulerRoutes.RequiredAccess).
        await SchedulerOperations.TriggerJob(call.Scheduler, jobKey, request: null, cancellationToken).ConfigureAwait(false);

        string? note = storedPaused
            ? $"The run's trigger was stored paused, because trigger group {TriggerKey.DefaultGroup} or job group {jobKey.Group} is paused: it runs when the group is resumed."
            : null;
        return await ChangeAnswers.Answer(call, applied: true, note, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class InterruptTool()
    : QuartzTool<InterruptArguments, ChangeAnswer>(
        "interrupt",
        "Interrupt a running job",
        "Asks one running firing to stop, by its fire instance id from list_executing. The job stops only if it watches its cancellation token.",
        [SchedulerRoutes.InterruptJobInstance],
        McpJsonContext.Default.InterruptArguments,
        McpJsonContext.Default.ChangeAnswer)
{
    protected override async ValueTask<ChangeAnswer> Run(ToolCall call, InterruptArguments arguments, CancellationToken cancellationToken)
    {
        OperationAppliedResponse applied = await SchedulerOperations.InterruptJobInstance(call.Scheduler, arguments.FireInstanceId, cancellationToken).ConfigureAwait(false);
        return await ChangeAnswers.Answer(call, applied.Applied, applied.Applied ? null : "No running firing has that id: it may have finished.", cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ResetTriggerFromErrorTool()
    : QuartzTool<TriggerArguments, ChangeAnswer>(
        "reset_trigger_from_error",
        "Reset a trigger from error",
        "Puts a trigger in the Error state back to normal (or paused, if its group is), so it fires again on its schedule. A trigger is put in Error when its job could not be run at all, and fires nothing until reset.",
        [SchedulerRoutes.ResetTriggerFromErrorState],
        McpJsonContext.Default.TriggerArguments,
        McpJsonContext.Default.ChangeAnswer,
        idempotent: true,
        destructive: false)
{
    protected override async ValueTask<ChangeAnswer> Run(ToolCall call, TriggerArguments arguments, CancellationToken cancellationToken)
    {
        OperationAppliedResponse applied = await SchedulerOperations.ResetTriggerFromErrorState(call.Scheduler, arguments.AsTriggerKey(), cancellationToken).ConfigureAwait(false);
        return await ChangeAnswers.Answer(call, applied.Applied, applied.Applied ? null : "The trigger was not in the Error state, or has no such key.", cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// The pause the two pause tools make, in the caller's name.
/// </summary>
internal static class Pauses
{
    public static PauseDetails Details(ToolCall call, PauseArguments arguments)
    {
        PauseDetails details = new()
        {
            Reason = arguments.Reason.Trim(),
            RequestedBy = call.Caller.Name + " via MCP",
        };

        call.Note = AuditNote.PauseReason(details);
        return details;
    }
}

internal static class ChangeAnswers
{
    /// <summary>
    /// A change's answer, with what it says entered for its record.
    /// </summary>
    public static async ValueTask<ChangeAnswer> Answer(ToolCall call, bool applied, string? note, CancellationToken cancellationToken)
    {
        call.Applied = applied;
        if (!applied && note is not null)
        {
            call.Note = call.Note is null ? note : call.Note + "; " + note;
        }

        SchedulerFreshness freshness = await call.Freshness(cancellationToken).ConfigureAwait(false);
        return new ChangeAnswer(call.Now(), freshness, applied) { Note = note };
    }
}
