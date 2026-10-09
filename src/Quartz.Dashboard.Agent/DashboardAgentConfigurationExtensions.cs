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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Quartz.Impl;

namespace Quartz;

/// <summary>
/// Installs the dashboard agent on a scheduler.
/// </summary>
public static class DashboardAgentConfigurationExtensions
{
    /// <summary>
    /// The name the scheduler knows the agent plugin by.
    /// </summary>
    internal const string PluginName = "dashboardAgent";

    /// <summary>
    /// Dials this scheduler out to a dashboard and answers what the dashboard asks of it, over one outbound
    /// connection the scheduler opens and keeps open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Use</c>, because the agent is middleware over the scheduler's lifecycle: it starts with the
    /// scheduler, reconnects for as long as the scheduler runs, and says goodbye when it shuts down. The
    /// dashboard it dials has to accept agents: <c>AddQuartzDashboard(o => o.AcceptAgents(…))</c>.
    /// </para>
    /// <para>
    /// A scheduler with an agent records its execution history, as a scheduler serving the HTTP API does,
    /// because the dashboard's History page reads it through the agent: in memory and bounded by default,
    /// in the database with <c>UseExecutionHistory()</c> on a persistent store. It also publishes its live
    /// events, which the Live Logs page reads the same way.
    /// </para>
    /// </remarks>
    /// <param name="builder">The scheduler's builder.</param>
    /// <param name="configure">Where the dashboard is, how to authenticate, and what the agent accepts.</param>
    public static IQuartzBuilder UseDashboardAgent(this IQuartzBuilder builder, Action<DashboardAgentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddQuartzSchedulerEvents();
        builder.Services.AddQuartzExecutionHistory();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<DashboardAgentOptions>, DashboardAgentOptionsValidator>());

        return builder.AddPlugin<DashboardAgentPlugin, DashboardAgentOptions>(configure, PluginName);
    }
}
