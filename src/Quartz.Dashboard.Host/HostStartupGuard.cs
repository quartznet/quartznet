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

using Microsoft.Extensions.Logging;

namespace Quartz.Dashboard.Host;

/// <summary>
/// The first hosted service to start: it says what the host is about to serve, or why it will not.
/// </summary>
/// <remarks>
/// <para>
/// A host whose configuration has a problem is built with this and nothing else, and this logs each
/// problem as event 9402 and throws, so the process exits non-zero having said why in its own log — which
/// for a container is the only thing anybody reads. Registered before every other service, so it starts
/// before the dashboard's own hosted services would reach for a database or a target.
/// </para>
/// <para>
/// A host that can start says so as event 9400, and in <c>Unsecured</c> mode warns as event 9401, at every
/// start.
/// </para>
/// </remarks>
internal sealed class HostStartupGuard(DashboardHostPlan plan, string dashboardPath, ILoggerFactory loggers) : IHostedService
{
    private readonly ILogger logger = loggers.CreateLogger(DashboardHostLog.Category);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!plan.CanStart)
        {
            foreach (string problem in plan.Problems)
            {
                logger.RefusedToStart(problem);
            }

            throw new InvalidOperationException(
                "The dashboard host refuses to start:" + Environment.NewLine
                + string.Join(Environment.NewLine, plan.Problems.Select(x => "  - " + x)));
        }

        if (plan.Mode == HostAuthenticationMode.Unsecured)
        {
            logger.Unsecured(plan.UnsecuredAccess);
        }

        foreach (HttpTargetPlan target in plan.HttpTargets)
        {
            logger.HttpTarget(target.Name, target.SchedulerName, Address(target.BaseAddress));
        }

        foreach (StoreTargetPlan target in plan.StoreTargets)
        {
            logger.StoreTarget(target.Name, target.Provider, target.Serializer);
        }

        if (plan.AcceptsAgents)
        {
            logger.AcceptingAgents(dashboardPath + "/agents");
        }

        logger.Started(dashboardPath, plan.Mode.ToString(), plan.HttpTargets.Count, plan.StoreTargets.Count, plan.AcceptsAgents);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// An address as it may be logged: no user information and no query, either of which can carry a
    /// credential.
    /// </summary>
    internal static string Address(Uri address)
    {
        UriBuilder builder = new(address) { UserName = "", Password = "", Query = "", Fragment = "" };
        return builder.Uri.AbsoluteUri;
    }
}
