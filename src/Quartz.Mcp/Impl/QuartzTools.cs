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

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Quartz.Extensibility;
using Quartz.Mcp.Impl.Tools;

namespace Quartz.Mcp.Impl;

/// <summary>
/// The nineteen tools, and the <c>tools/list</c> filter that offers each caller the ones they can call.
/// </summary>
/// <remarks>
/// <para>
/// Not in v1, each additive later: backfill, pause and resume of everything, start, standby and shutdown, adding
/// and deleting jobs, scheduling, calendars, execution limits and editing a trigger's details. Those are the
/// operations whose reach is a whole scheduler, or that name a type, and each needs a preview of its own first.
/// </para>
/// </remarks>
internal static class QuartzTools
{
    /// <summary>Every tool, reads first.</summary>
    public static QuartzTool[] Create() =>
    [
        new ListSchedulersTool(),
        new GetSchedulerTool(),
        new ListClusterNodesTool(),
        new ListJobsTool(),
        new GetJobTool(),
        new ListTriggersTool(),
        new GetTriggerTool(),
        new ListExecutingTool(),
        new QueryHistoryTool(),
        new GetJobRunStatusTool(),
        new PauseTriggerTool(),
        new PauseJobTool(),
        new ResumeTriggerTool(),
        new ResumeJobTool(),
        new TriggerJobTool(),
        new InterruptTool(),
        new ResetTriggerFromErrorTool(),
        new RescheduleTool(),
        new UnscheduleTool(),
    ];

    /// <summary>
    /// The <c>tools/list</c> filter: removes the Quartz tools this caller cannot call on any scheduler they may see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Answered per request, since the endpoint is stateless. Under <c>ReadOnly</c> every tool that changes something
    /// goes; for a caller with no stable identity the two that need confirmation go; and a tool stays only when at
    /// least one scheduler the caller may see lets them call every route it calls, at the route's role and under the
    /// target's ceiling. <c>list_schedulers</c> always stays.
    /// </para>
    /// <para>
    /// The list is a convenience, not the enforcement: <c>tools/call</c> asks again against the scheduler it names. It
    /// is marked private to the caller, so a shared cache does not hand one caller's list to another. It costs at most
    /// tools × visible schedulers policy evaluations, which are role checks in memory.
    /// </para>
    /// </remarks>
    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Filter(McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
    {
        return async (request, cancellationToken) =>
        {
            ListToolsResult result = await next(request, cancellationToken).ConfigureAwait(false);

            IServiceProvider? services = request.Services;
            if (services is null)
            {
                return result;
            }

            McpCaller caller = McpCaller.Of(request, services.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()?.HttpContext);
            HashSet<string> offered = await Offered(caller, services, cancellationToken).ConfigureAwait(false);

            List<Tool> kept = [.. result.Tools.Where(tool => offered.Contains(tool.Name))];
            result.Tools.Clear();
            foreach (Tool tool in kept)
            {
                result.Tools.Add(tool);
            }

            result.CacheScope = CacheScope.Private;
            return result;
        };
    }

    /// <summary>
    /// The names of the tools <paramref name="caller" /> is offered.
    /// </summary>
    internal static async ValueTask<HashSet<string>> Offered(McpCaller caller, IServiceProvider services, CancellationToken cancellationToken)
    {
        McpAccess access = services.GetRequiredService<McpAccess>();
        ISchedulerRegistry registry = services.GetRequiredService<ISchedulerRegistry>();

        List<SchedulerRef> visible = [];
        foreach (SchedulerRegistration registration in await registry.QuerySchedulers(cancellationToken).ConfigureAwait(false))
        {
            SchedulerRef key = new(registration.Name, registration.Target);
            if (await access.MayView(caller, services, key, cancellationToken).ConfigureAwait(false))
            {
                visible.Add(key);
            }
        }

        HashSet<string> offered = new(StringComparer.Ordinal);
        foreach (QuartzTool tool in services.GetServices<McpServerTool>().OfType<QuartzTool>())
        {
            if (!tool.NamesScheduler)
            {
                offered.Add(tool.Name);
                continue;
            }

            if (access.Unoffered(caller, tool) is not null)
            {
                continue;
            }

            foreach (SchedulerRef key in visible)
            {
                if (await access.Refusal(caller, services, tool, key, cancellationToken).ConfigureAwait(false) is null)
                {
                    offered.Add(tool.Name);
                    break;
                }
            }
        }

        return offered;
    }
}
