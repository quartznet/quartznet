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

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.AspNetCore;

namespace Quartz;

/// <summary>
/// How the Quartz MCP endpoint is served: what an AI agent may do through it, and how much. Added in 4.9.
/// </summary>
/// <remarks>
/// <para>
/// One set per process, as the HTTP API's options are: the endpoint serves every scheduler in the container,
/// and those behind its targets, through one set of tools, and a tool call names the scheduler it is for.
/// </para>
/// <para>
/// The caller is an autonomous agent acting on text it reads, and some of that text — descriptions, error
/// messages, pause reasons — was written by whoever can schedule a job. So the defaults are closed: the
/// endpoint is read-only until <see cref="ReadOnly" /> is turned off, and it cannot be turned off without an
/// <see cref="OperationAuthorizationPolicy" /> saying which role may do what.
/// </para>
/// </remarks>
public sealed class QuartzMcpOptions
{
    internal const string DefaultPath = "/quartz-mcp";

    internal static readonly TimeSpan DefaultConfirmationLifetime = TimeSpan.FromMinutes(5);

    internal static readonly TimeSpan MinConfirmationLifetime = TimeSpan.FromSeconds(30);

    internal static readonly TimeSpan MaxConfirmationLifetime = TimeSpan.FromMinutes(30);

    internal const int DefaultMaxChangesPerMinute = 20;

    /// <summary>
    /// Whether the endpoint offers reads only. <see langword="true" /> by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read-only, <c>tools/list</c> offers the ten tools that read and none that change anything, and a call of
    /// a changing tool is refused before anything is looked up. It differs from the HTTP API's and the
    /// dashboard's defaults on purpose: those surfaces had writable releases before they had a switch, and
    /// this one starts with the safe setting.
    /// </para>
    /// <para>
    /// Turning it off needs <see cref="OperationAuthorizationPolicy" />, or the options fail validation at
    /// startup: an agent that may change things is held to the operator roles, never to "whoever reached the
    /// endpoint".
    /// </para>
    /// </remarks>
    public bool ReadOnly { get; set; } = true;

    /// <summary>
    /// The authorization policy every tool call that names a scheduler is held to, evaluated against a
    /// <see cref="SchedulerResource" /> carrying the scheduler's name and target. Null — the default — lets every
    /// caller who reached the endpoint see every scheduler.
    /// </summary>
    /// <remarks>
    /// The same policy name the dashboard's and the HTTP API's options take, so one policy decides the three
    /// surfaces alike. It is asked before the scheduler is looked up, so a refused caller cannot tell a
    /// scheduler that does not exist from one that is not theirs; <c>list_schedulers</c> lists only the
    /// schedulers it allows.
    /// </remarks>
    public string? SchedulerAuthorizationPolicy { get; set; }

    /// <summary>
    /// The authorization policy every tool is held to, per route it calls: evaluated against a
    /// <see cref="SchedulerResource" /> carrying the scheduler's name and target, the route's name and the
    /// operator role it needs. Null — the default — leaves only <see cref="ReadOnly" /> in the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Name a policy built with <c>RequireSchedulerRoles(…)</c>, the one the dashboard and the HTTP API name, and
    /// each tool needs the role of the routes it calls: viewers read, operators pause, resume, trigger and
    /// interrupt, administrators reschedule and unschedule. A tool is authorized by the HTTP API's route names,
    /// so <c>SchedulerRoles.Operations</c> moves an MCP tool where it moves the route.
    /// </para>
    /// <para>
    /// Required when <see cref="ReadOnly" /> is off.
    /// </para>
    /// </remarks>
    public string? OperationAuthorizationPolicy { get; set; }

    /// <summary>
    /// The origins a browser may call the endpoint from, as <c>scheme://host[:port]</c>. Empty, the default,
    /// refuses every request that carries an <c>Origin</c> header.
    /// </summary>
    /// <remarks>
    /// The MCP specification requires a Streamable HTTP server to check <c>Origin</c>, because a page the
    /// operator visits can otherwise reach a server on <c>localhost</c> through DNS rebinding. An agent sends no
    /// <c>Origin</c> and is never affected. Unlike the HTTP API's own list, this check is always on.
    /// </remarks>
    public List<string> AllowedOrigins { get; } = [];

    /// <summary>
    /// How long a confirmation token from a preview stays good: five minutes by default, and from 30 seconds
    /// to 30 minutes.
    /// </summary>
    /// <remarks>
    /// <c>reschedule</c> and <c>unschedule</c> change nothing when called without a token: they answer with a
    /// preview of what they would do and a token to call again with once the agent's user has agreed. The
    /// token is bound to the caller, the tool, the scheduler, the arguments and the state of the trigger as
    /// previewed, and is used once. A host with several instances needs a shared Data Protection key ring for
    /// a token from one instance to be read by another.
    /// </remarks>
    public TimeSpan ConfirmationLifetime { get; set; } = DefaultConfirmationLifetime;

    /// <summary>
    /// The most changes one caller may make in a minute, over every tool that changes something: 20 by
    /// default, and <c>0</c> for no limit.
    /// </summary>
    /// <remarks>
    /// An agent in a loop can call <c>trigger_job</c> a thousand times, and a person cannot. The window slides,
    /// a preview does not count, a read never counts, and each caller has a window of their own, so one agent
    /// cannot starve another. The refusal says when to try again. The count is this process's: a host with
    /// several instances allows this many per instance.
    /// </remarks>
    public int MaxChangesPerMinute { get; set; } = DefaultMaxChangesPerMinute;
}

/// <summary>
/// Validates <see cref="QuartzMcpOptions" />, with the HTTP API's validator's one exception type and wording.
/// </summary>
internal sealed class QuartzMcpOptionsValidator : IValidateOptions<QuartzMcpOptions>
{
    private readonly IServiceProviderIsService? registrations;

    public QuartzMcpOptionsValidator(IServiceProviderIsService? registrations = null)
    {
        this.registrations = registrations;
    }

    public ValidateOptionsResult Validate(string? name, QuartzMcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.ReadOnly && string.IsNullOrWhiteSpace(options.OperationAuthorizationPolicy))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(QuartzMcpOptions.ReadOnly)} is off, but no {nameof(QuartzMcpOptions.OperationAuthorizationPolicy)} is set. "
                + "An MCP endpoint that changes things is held to the operator roles: name the policy the dashboard and the HTTP API "
                + "use, built with RequireSchedulerRoles(...), or leave ReadOnly on.");
        }

        if (options.ConfirmationLifetime < QuartzMcpOptions.MinConfirmationLifetime
            || options.ConfirmationLifetime > QuartzMcpOptions.MaxConfirmationLifetime)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(QuartzMcpOptions.ConfirmationLifetime)} must be from {QuartzMcpOptions.MinConfirmationLifetime} to "
                + $"{QuartzMcpOptions.MaxConfirmationLifetime}, was {options.ConfirmationLifetime}: shorter and a person cannot read a "
                + "preview before it expires, longer and an approval outlives the state it approved.");
        }

        if (options.MaxChangesPerMinute < 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(QuartzMcpOptions.MaxChangesPerMinute)} must not be negative, was "
                + options.MaxChangesPerMinute.ToString(CultureInfo.InvariantCulture) + ". Use 0 for no limit.");
        }

        foreach (string origin in options.AllowedOrigins)
        {
            if (QuartzRequestGuard.Problem(origin) is { } problem)
            {
                return ValidateOptionsResult.Fail($"{nameof(QuartzMcpOptions.AllowedOrigins)}: {problem}");
            }
        }

        foreach ((string option, string? policy) in (ReadOnlySpan<(string, string?)>)
                 [
                     (nameof(QuartzMcpOptions.SchedulerAuthorizationPolicy), options.SchedulerAuthorizationPolicy),
                     (nameof(QuartzMcpOptions.OperationAuthorizationPolicy), options.OperationAuthorizationPolicy),
                 ])
        {
            if (!string.IsNullOrWhiteSpace(policy) && registrations?.IsService(typeof(IAuthorizationService)) == false)
            {
                return ValidateOptionsResult.Fail(
                    $"{option} is '{policy}', but the container has no authorization services to evaluate it with - "
                    + "call services.AddAuthorization() and register that policy.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
