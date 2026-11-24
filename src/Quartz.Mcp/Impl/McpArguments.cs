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

using System.Text.Json.Serialization;

namespace Quartz.Mcp.Impl;

// The arguments every tool takes, one record per shape. Each refuses a property it does not name, which is
// also what puts additionalProperties: false in the schema JsonSchemaExporter writes for it, and none carries a
// data value: no tool takes job data, a trigger body or a job type. Everything a tool derives from its
// arguments is a method, because a property would be written into the schema as an argument.

/// <summary>
/// What every tool's arguments can be asked: the scheduler key they name, if any, and what is wrong with them
/// beyond their shape.
/// </summary>
internal abstract record McpArguments
{
    /// <summary>The scheduler key, or <see langword="null" /> for the one tool that names none.</summary>
    public virtual string? NamedScheduler() => null;

    /// <summary>
    /// Why the arguments cannot be acted on, or <see langword="null" />. The JSON reader has already refused an
    /// unknown property, a missing one and a value of the wrong type.
    /// </summary>
    public virtual string? Problem() => null;

    protected static string? Blank(string? value, string argument)
    {
        return string.IsNullOrWhiteSpace(value) ? $"'{argument}' is required and cannot be blank." : null;
    }

    protected static string? Page(int skip, int take)
    {
        if (skip < 0)
        {
            return "'skip' must not be negative.";
        }

        if (take < 1 || take > McpLimits.MaxTake)
        {
            return $"'take' must be from 1 to {McpLimits.MaxTake}, was {take}; page with 'skip' for more.";
        }

        return null;
    }
}

/// <summary>
/// The arguments of a tool that changes nothing until it is called again with the token its preview gave.
/// </summary>
internal interface IConfirmedArguments
{
    string? ConfirmationToken { get; }

    /// <summary>The same arguments without the token: what the token's argument digest is taken over.</summary>
    McpArguments WithoutToken();
}

/// <summary>A job or a trigger, by its group and name.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record McpKeyArguments(string Group, string Name)
{
    public string? Problem(string argument)
    {
        return string.IsNullOrWhiteSpace(Group) || string.IsNullOrWhiteSpace(Name)
            ? $"'{argument}' needs both 'group' and 'name'."
            : null;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ListSchedulersArguments : McpArguments;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SchedulerArguments(string Scheduler) : McpArguments
{
    public override string? NamedScheduler() => Scheduler;

    public override string? Problem() => Blank(Scheduler, "scheduler");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ListJobsArguments(
    string Scheduler,
    string? GroupContains = null,
    string? NameContains = null,
    int Skip = 0,
    int Take = McpLimits.DefaultTake) : McpArguments
{
    public override string? NamedScheduler() => Scheduler;

    public override string? Problem() => Blank(Scheduler, "scheduler") ?? Page(Skip, Take);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record JobArguments(string Scheduler, string Group, string Name) : McpArguments
{
    public override string? NamedScheduler() => Scheduler;

    public override string? Problem() => Blank(Scheduler, "scheduler") ?? Blank(Group, "group") ?? Blank(Name, "name");

    public JobKey AsJobKey() => new(Name, Group);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record TriggerArguments(string Scheduler, string Group, string Name) : McpArguments
{
    public override string? NamedScheduler() => Scheduler;

    public override string? Problem() => Blank(Scheduler, "scheduler") ?? Blank(Group, "group") ?? Blank(Name, "name");

    public TriggerKey AsTriggerKey() => new(Name, Group);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ListTriggersArguments(
    string Scheduler,
    TriggerState? State = null,
    string? GroupContains = null,
    string? NameContains = null,
    McpKeyArguments? Job = null,
    DateTimeOffset? NextFireBefore = null,
    int Skip = 0,
    int Take = McpLimits.DefaultTake) : McpArguments
{
    public override string? NamedScheduler() => Scheduler;

    public override string? Problem() => Blank(Scheduler, "scheduler") ?? Job?.Problem("job") ?? Page(Skip, Take);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ListExecutingArguments(string Scheduler, int Skip = 0, int Take = McpLimits.DefaultTake) : McpArguments
{
    public override string? NamedScheduler() => Scheduler;

    public override string? Problem() => Blank(Scheduler, "scheduler") ?? Page(Skip, Take);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record QueryHistoryArguments(
    string Scheduler,
    McpKeyArguments? Job = null,
    McpKeyArguments? Trigger = null,
    JobRunResult[]? Results = null,
    DateTimeOffset? FiredFrom = null,
    DateTimeOffset? FiredBefore = null,
    bool? FailedFinally = null,
    int Skip = 0,
    int Take = McpLimits.DefaultTake) : McpArguments
{
    public override string? NamedScheduler() => Scheduler;

    public override string? Problem()
    {
        if (FiredFrom is { } from && FiredBefore is { } before && before <= from)
        {
            return "'firedBefore' must be after 'firedFrom'.";
        }

        return Blank(Scheduler, "scheduler") ?? Job?.Problem("job") ?? Trigger?.Problem("trigger") ?? Page(Skip, Take);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record PauseArguments(string Scheduler, string Group, string Name, string Reason) : McpArguments
{
    public override string? NamedScheduler() => Scheduler;

    public override string? Problem()
    {
        string? blank = Blank(Scheduler, "scheduler") ?? Blank(Group, "group") ?? Blank(Name, "name");
        if (blank is not null)
        {
            return blank;
        }

        int length = Reason?.Trim().Length ?? 0;
        return length is < McpLimits.MinReasonLength or > McpLimits.MaxReasonLength
            ? $"'reason' is required, from {McpLimits.MinReasonLength} to {McpLimits.MaxReasonLength} characters, saying why the pause is made: "
              + "it is written on the trigger and shown to whoever looks at it next."
            : null;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InterruptArguments(string Scheduler, string FireInstanceId) : McpArguments
{
    public override string? NamedScheduler() => Scheduler;

    public override string? Problem() => Blank(Scheduler, "scheduler") ?? Blank(FireInstanceId, "fireInstanceId");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record RescheduleArguments(
    string Scheduler,
    string Group,
    string Name,
    string CronExpression,
    string? TimeZone = null,
    string? ConfirmationToken = null) : McpArguments, IConfirmedArguments
{
    public override string? NamedScheduler() => Scheduler;

    public override string? Problem()
    {
        return Blank(Scheduler, "scheduler") ?? Blank(Group, "group") ?? Blank(Name, "name") ?? Blank(CronExpression, "cronExpression");
    }

    public McpArguments WithoutToken() => this with { ConfirmationToken = null };

    public TriggerKey AsTriggerKey() => new(Name, Group);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record UnscheduleArguments(string Scheduler, string Group, string Name, string? ConfirmationToken = null) : McpArguments, IConfirmedArguments
{
    public override string? NamedScheduler() => Scheduler;

    public override string? Problem() => Blank(Scheduler, "scheduler") ?? Blank(Group, "group") ?? Blank(Name, "name");

    public McpArguments WithoutToken() => this with { ConfirmationToken = null };

    public TriggerKey AsTriggerKey() => new(Name, Group);
}

/// <summary>
/// The bounds the tools hold their arguments and answers to, which the schemas and the documentation state.
/// </summary>
internal static class McpLimits
{
    /// <summary>A listing's page when the call names none.</summary>
    public const int DefaultTake = 50;

    /// <summary>
    /// The largest page a listing serves: an agent's context is the budget, where the HTTP API's 1000 is a
    /// script's. Above it is refused, never quietly cut.
    /// </summary>
    public const int MaxTake = 200;

    public const int MinReasonLength = 3;

    public const int MaxReasonLength = 500;

    public const int MaxDescriptionLength = 1000;

    public const int MaxErrorMessageLength = 2000;

    public const int MaxSummaryLength = 1000;

    /// <summary>The upcoming fire times <c>get_trigger</c> and a reschedule preview list.</summary>
    public const int UpcomingFireTimes = 5;
}
