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
using Microsoft.Extensions.Options;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.Configuration;
using Quartz.HttpApiContract;

namespace Quartz.Mcp.Impl;

/// <summary>
/// Why a tool call was refused before anything reached a scheduler.
/// </summary>
/// <param name="Code">The error code the agent is answered with.</param>
/// <param name="Message">What it is told.</param>
/// <param name="Action">
/// Whether the refusal is an action an operator sees in the Action Log: a change reached for and refused. A read
/// refused is logged, and kept off the page.
/// </param>
internal sealed record AccessRefusal(string Code, string Message, bool Action);

/// <summary>
/// Whether a caller may call a tool on a scheduler, asked in the one order every call is: <c>ReadOnly</c>, the
/// scheduler policy, the operation policy for each route the tool calls, and the target's ceiling.
/// </summary>
/// <remarks>
/// <para>
/// The two policies are the HTTP API's own questions, asked through its principal overloads with the same
/// <see cref="SchedulerResource" />: the scheduler's name and target, the route's name and the role it needs. So
/// the one policy the dashboard, the API and MCP name decides the three alike, and
/// <c>SchedulerRoles.Operations</c> moves an MCP tool where it moves the route. The key is asked about in the
/// target's own spelling, so a policy written against <c>w1</c> cannot be stepped round as <c>W1</c>.
/// </para>
/// <para>
/// Everything is asked before the scheduler is looked up, so a refused caller cannot tell a missing scheduler or
/// target from a forbidden one. The ceiling is the target's: an agent's <c>MaxAccess</c>, from
/// <see cref="SchedulerTarget.Ceiling" />, which the agent would refuse anyway, after a round trip.
/// </para>
/// </remarks>
internal sealed class McpAccess
{
    private readonly IOptions<QuartzMcpOptions> options;

    public McpAccess(IOptions<QuartzMcpOptions> options)
    {
        this.options = options;
    }

    /// <summary>
    /// Why <paramref name="caller" /> may not call <paramref name="tool" /> on <paramref name="key" />, or
    /// <see langword="null" /> when they may.
    /// </summary>
    public async ValueTask<AccessRefusal?> Refusal(McpCaller caller, IServiceProvider services, QuartzTool tool, SchedulerRef key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(key);

        if (Unoffered(caller, tool) is { } unoffered)
        {
            return unoffered;
        }

        QuartzMcpOptions settings = options.Value;
        if (!await SchedulerAuthorization.IsAuthorized(caller.User, services, settings.SchedulerAuthorizationPolicy, key, cancellationToken).ConfigureAwait(false))
        {
            return new AccessRefusal(McpErrorCodes.Forbidden, $"Not authorized for scheduler {key.Key}", Action: false);
        }

        foreach (WireRoute route in tool.Routes)
        {
            SchedulerAccess access = SchedulerRoutes.RequiredAccess(route, carriesJobData: false);
            if (!await OperationAuthorization.IsAllowed(caller.User, services, settings.OperationAuthorizationPolicy, key, route, access, cancellationToken).ConfigureAwait(false))
            {
                return new AccessRefusal(McpErrorCodes.Forbidden, OperationAuthorization.Detail(route.Name, key.Key, access), Action: tool.Changes);
            }
        }

        SchedulerAccess ceiling = Ceiling(services, key);
        if (ceiling < tool.Level)
        {
            return new AccessRefusal(
                McpErrorCodes.Forbidden,
                $"Target '{key.Target}' allows at most {ceiling}, and {tool.Name} on scheduler {key.Key} needs {tool.Level}: the target's own setting, which no role here raises.",
                Action: tool.Changes);
        }

        return null;
    }

    /// <summary>
    /// Why the tool is not offered to <paramref name="caller" /> at all, whatever the scheduler: a change while the
    /// endpoint is read-only, and a tool that needs confirmation for a caller it cannot be bound to.
    /// </summary>
    public AccessRefusal? Unoffered(McpCaller caller, QuartzTool tool)
    {
        if (tool.Changes && options.Value.ReadOnly)
        {
            return new AccessRefusal(McpErrorCodes.ReadOnly, "The Quartz MCP endpoint is configured as read-only.", Action: false);
        }

        if (tool.Confirms && !caller.HasStableIdentity)
        {
            return new AccessRefusal(
                McpErrorCodes.Forbidden,
                $"{tool.Name} needs a caller with an identity a confirmation can be bound to: sign in with a credential that names you.",
                Action: true);
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="caller" /> may see <paramref name="key" /> in <c>list_schedulers</c>: the scheduler
    /// policy allows it, and the operation policy allows the listing at <see cref="SchedulerAccess.View" />, as the
    /// HTTP API's listing asks.
    /// </summary>
    public async ValueTask<bool> MayView(McpCaller caller, IServiceProvider services, SchedulerRef key, CancellationToken cancellationToken)
    {
        QuartzMcpOptions settings = options.Value;
        return await SchedulerAuthorization.IsAuthorized(caller.User, services, settings.SchedulerAuthorizationPolicy, key, cancellationToken).ConfigureAwait(false)
               && await OperationAuthorization.IsAllowed(
                   caller.User, services, settings.OperationAuthorizationPolicy, key, SchedulerRoutes.GetAllSchedulers, SchedulerAccess.View, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The most the key's target lets anybody do: <see cref="SchedulerTarget.Ceiling" />, and
    /// <see cref="SchedulerAccess.Administer" /> for a scheduler reached through no target.
    /// </summary>
    internal static SchedulerAccess Ceiling(IServiceProvider services, SchedulerRef key)
    {
        return key.Target is { } target && services.GetService<SchedulerTargets>()?.Find(target) is { } found
            ? found.Ceiling
            : SchedulerAccess.Administer;
    }
}
