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

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.HttpApiContract;

namespace Quartz.Mcp.Impl.Tools;

// The two tools an administrator's role allows, each of which changes nothing until it is confirmed: called
// without a token it answers a preview and a token, and called again with the token it applies what was
// previewed, if the trigger is still in the state the preview read.

internal sealed class RescheduleTool()
    : QuartzTool<RescheduleArguments, ConfirmAnswer>(
        "reschedule",
        "Reschedule a trigger",
        "Gives a trigger a new cron schedule. Called without confirmationToken it changes nothing and answers a preview: the old and the new schedule in words and the next five fire times. Show the preview to your user, and call again with the same arguments and the token only after they agree. The trigger keeps its key, job, description, data, priority, calendar, end time and execution settings; a cron trigger keeps its misfire instruction.",
        [SchedulerRoutes.RescheduleJob],
        McpJsonContext.Default.RescheduleArguments,
        McpJsonContext.Default.ConfirmAnswer,
        idempotent: false,
        destructive: true,
        confirms: true)
{
    protected override async ValueTask<ConfirmAnswer> Run(ToolCall call, RescheduleArguments arguments, CancellationToken cancellationToken)
    {
        TriggerKey triggerKey = arguments.AsTriggerKey();
        (ITrigger current, string state) = await Confirmations.Read(call, triggerKey, cancellationToken).ConfigureAwait(false);

        CronExpression expression;
        TimeZoneInfo timeZone;
        try
        {
            timeZone = arguments.TimeZone is { Length: > 0 } id
                ? TimeZones.FindById(id)
                : current is ICronTrigger cron ? cron.TimeZone : TimeZoneInfo.Utc;
            expression = new CronExpression(arguments.CronExpression, timeZone);
        }
        catch (Exception exception) when (exception is FormatException or TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            throw new McpToolException(McpErrorCodes.InvalidArguments, McpToolInvoker.Truncate(exception.Message));
        }

        ITrigger replacement = Replacement(current, expression, call.Now());

        if (call.IsPreview)
        {
            ICalendar? calendar = current.CalendarName is { Length: > 0 } calendarName
                ? await call.Scheduler.GetCalendar(calendarName, cancellationToken).ConfigureAwait(false)
                : null;
            List<DateTimeOffset> next = TriggerFireTimes.Compute(replacement, calendar, McpLimits.UpcomingFireTimes);

            string before = McpText.Schedule(current) ?? McpText.Expression(current) ?? McpText.Kind(current);
            string after = McpText.Schedule(replacement) ?? arguments.CronExpression;
            List<string> effects =
            [
                $"{before} → {after}",
                next.Count > 0 ? "next: " + string.Join(", ", next.Select(static x => McpText.Utc(x.ToUniversalTime()))) : "it would not fire again",
                "keeps its key, job, description, data, priority, calendar, end time and execution settings",
            ];
            if (current is not ICronTrigger)
            {
                effects.Add($"becomes a cron trigger, from {McpText.Kind(current)}, with the cron trigger's smart misfire policy");
            }

            return await Confirmations.Preview(
                call,
                arguments,
                current,
                state,
                new PreviewRow("reschedule", $"Reschedule trigger {triggerKey} of job {current.JobKey}: {before} → {after}", McpKey.Of(triggerKey), [.. effects]),
                cancellationToken).ConfigureAwait(false);
        }

        await Confirmations.Confirm(call, arguments, arguments.ConfirmationToken!, state).ConfigureAwait(false);

        RescheduleJobResponse response = await SchedulerOperations.RescheduleJob(call.Scheduler, triggerKey, new RescheduleJobRequest(replacement), cancellationToken).ConfigureAwait(false);
        call.Applied = response.FirstFireTimeUtc is not null;
        call.Note = $"cron: {arguments.CronExpression} ({timeZone.Id})";

        return new ConfirmAnswer(call.Now(), await call.Freshness(cancellationToken).ConfigureAwait(false))
        {
            Applied = call.Applied,
            NextFireUtc = response.FirstFireTimeUtc,
            Note = call.Applied ? null : "The trigger was gone by the time it was rescheduled, so nothing was applied.",
        };
    }

    /// <summary>
    /// The trigger that replaces <paramref name="current" />: rebuilt from <paramref name="current" />'s own builder —
    /// its key, job, description, data, priority, calendar, end time and execution settings — on
    /// <paramref name="expression" /> from now, and with the misfire instruction of a cron trigger it replaces.
    /// </summary>
    private static ITrigger Replacement(ITrigger current, CronExpression expression, DateTimeOffset now)
    {
        ITrigger replacement = current.GetTriggerBuilder()
            .StartAt(now)
            .WithSchedule(CronScheduleBuilder.Create(expression).InTimeZone(expression.TimeZone))
            .Build();

        if (current is ICronTrigger && replacement is IMutableTrigger mutable)
        {
            mutable.MisfireInstructionCode = current.MisfireInstructionCode;
        }

        return replacement;
    }
}

internal sealed class UnscheduleTool()
    : QuartzTool<UnscheduleArguments, ConfirmAnswer>(
        "unschedule",
        "Unschedule a trigger",
        "Removes a trigger. Called without confirmationToken it changes nothing and answers a preview: the trigger, its job, its next fire time, and whether the job is deleted with it because it is not durable and this is its last trigger. Show the preview to your user, and call again with the same arguments and the token only after they agree.",
        [SchedulerRoutes.UnscheduleJob],
        McpJsonContext.Default.UnscheduleArguments,
        McpJsonContext.Default.ConfirmAnswer,
        idempotent: false,
        destructive: true,
        confirms: true)
{
    protected override async ValueTask<ConfirmAnswer> Run(ToolCall call, UnscheduleArguments arguments, CancellationToken cancellationToken)
    {
        TriggerKey triggerKey = arguments.AsTriggerKey();
        (ITrigger current, string state) = await Confirmations.Read(call, triggerKey, cancellationToken).ConfigureAwait(false);

        if (call.IsPreview)
        {
            IJobDetail? job = await call.Scheduler.GetJobDetail(current.JobKey, cancellationToken).ConfigureAwait(false);
            List<ITrigger> triggers = await call.Scheduler.GetTriggersOfJob(current.JobKey, cancellationToken).ConfigureAwait(false);
            bool jobGoes = job is { Durable: false } && triggers.Count <= 1;

            List<string> effects =
            [
                current.NextFireTimeUtc is { } next ? "it would have fired next at " + McpText.Utc(next) : "it was not going to fire again",
                jobGoes
                    ? $"job {current.JobKey} is not durable and this is its last trigger, so the job is deleted with it"
                    : $"job {current.JobKey} stays",
            ];

            return await Confirmations.Preview(
                call,
                arguments,
                current,
                state,
                new PreviewRow("unschedule", $"Remove trigger {triggerKey} of job {current.JobKey}", McpKey.Of(triggerKey), [.. effects]),
                cancellationToken).ConfigureAwait(false);
        }

        await Confirmations.Confirm(call, arguments, arguments.ConfirmationToken!, state).ConfigureAwait(false);

        OperationAppliedResponse applied = await SchedulerOperations.UnscheduleJob(call.Scheduler, triggerKey, cancellationToken).ConfigureAwait(false);
        call.Applied = applied.Applied;

        return new ConfirmAnswer(call.Now(), await call.Freshness(cancellationToken).ConfigureAwait(false))
        {
            Applied = applied.Applied,
            Note = applied.Applied ? null : "The trigger was gone by the time it was unscheduled, so nothing was applied.",
        };
    }
}

/// <summary>
/// The preview and the confirmation the two tools share.
/// </summary>
internal static class Confirmations
{
    /// <summary>
    /// The trigger as it is now, and the digest of its state a confirmation is bound to.
    /// </summary>
    public static async ValueTask<(ITrigger Trigger, string State)> Read(ToolCall call, TriggerKey triggerKey, CancellationToken cancellationToken)
    {
        ITrigger trigger = await SchedulerOperations.GetTrigger(call.Scheduler, triggerKey, cancellationToken).ConfigureAwait(false)
                           ?? throw new McpToolException(McpErrorCodes.NotFound, $"Unknown trigger {triggerKey}");
        TriggerStateDto state = await SchedulerOperations.GetTriggerState(call.Scheduler, triggerKey, cancellationToken).ConfigureAwait(false);
        return (trigger, McpText.StateDigest(trigger, state.State));
    }

    /// <summary>
    /// A preview's answer: what the call would do, and the token to do it with.
    /// </summary>
    public static async ValueTask<ConfirmAnswer> Preview(ToolCall call, McpArguments arguments, ITrigger current, string state, PreviewRow preview, CancellationToken cancellationToken)
    {
        ConfirmationTokens tokens = call.Services.GetRequiredService<ConfirmationTokens>();
        (string token, DateTimeOffset expires) = tokens.Issue(
            call.Tool.Name,
            call.Key!.Key,
            current.Key.ToString(),
            call.Tool.DigestArguments(arguments),
            call.Caller.Digest,
            state);

        return new ConfirmAnswer(call.Now(), await call.Freshness(cancellationToken).ConfigureAwait(false))
        {
            Applied = false,
            Preview = preview,
            ConfirmationToken = token,
            ExpiresAtUtc = expires,
            Note = "Nothing was changed. Show this preview to your user, and call again with the same arguments and confirmationToken only after they agree.",
        };
    }

    /// <summary>
    /// Holds a confirmed call to its token, in the order the token's checks are documented in: readable and
    /// unexpired, the same tool, scheduler, arguments and caller, the trigger in the state previewed, and not
    /// confirmed before. The caller's role was asked again before this ran.
    /// </summary>
    /// <exception cref="McpToolException">The confirmation is refused, logged as <c>9502</c> by its reason.</exception>
    public static ValueTask Confirm(ToolCall call, McpArguments arguments, string token, string state)
    {
        ConfirmationTokens tokens = call.Services.GetRequiredService<ConfirmationTokens>();
        string schedulerKey = call.Key!.Key;

        ConfirmationPayload? payload = tokens.Read(token, out string? unreadable);
        if (payload is null)
        {
            throw Refused(call, unreadable!, "The confirmation expired or is not valid; preview again.");
        }

        if (ConfirmationTokens.Mismatch(payload, call.Tool.Name, schedulerKey, call.Tool.DigestArguments(arguments), call.Caller.Digest) is { } mismatch)
        {
            throw Refused(call, mismatch + " differs", $"The confirmation was given for another {mismatch}; preview this call to confirm it.");
        }

        if (!string.Equals(payload.State, state, StringComparison.Ordinal))
        {
            throw Refused(call, "state changed", "The trigger changed since the preview; preview again.");
        }

        if (!tokens.TrySpend(payload))
        {
            throw Refused(call, "already used", "The confirmation was used already; preview again.");
        }

        return ValueTask.CompletedTask;
    }

    private static McpToolException Refused(ToolCall call, string reason, string message)
    {
        call.Services.GetRequiredService<McpRecords>().ConfirmationRefused(call.Caller, call.Tool, call.Key!.Key, reason);
        return new McpToolException(McpErrorCodes.ConfirmationRefused, message);
    }
}
