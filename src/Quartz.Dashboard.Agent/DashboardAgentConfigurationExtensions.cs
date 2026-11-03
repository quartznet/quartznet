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

using Microsoft.Extensions.Configuration;
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
    /// Dials this scheduler out to a dashboard and answers what the dashboard asks of it, configured from
    /// the application's configuration alone. Added in 4.8.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads <c>Quartz:DashboardAgent</c> — <c>Endpoint</c>, <c>Token</c>, <c>Target</c>, <c>ReadOnly</c>,
    /// <c>MaxAccess</c>, <c>HeartbeatInterval</c>, <c>MaxConcurrentOperations</c> and <c>MaxPageSize</c>
    /// — from the <c>IConfiguration</c> in the container, or a named scheduler's own
    /// <c>Quartz:Schedulers:{name}:DashboardAgent</c> when it has one. An Aspire AppHost's
    /// <c>WithReference(dashboard)</c> writes exactly those keys as environment variables.
    /// </para>
    /// <para>
    /// The delegates — <see cref="DashboardAgentOptions.IsJobTypeAllowed" />,
    /// <see cref="DashboardAgentOptions.IsOperationAllowed" />, the token provider and the connection — have
    /// no spelling in configuration; set them with the other overload, which reads the same section first.
    /// Unset, the agent refuses every job type, as it always has.
    /// </para>
    /// </remarks>
    /// <param name="builder">The scheduler's builder.</param>
    public static IQuartzBuilder UseDashboardAgent(this IQuartzBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return Install(builder, configure: null);
    }

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
    /// <para>
    /// Since 4.8 the options start from configuration, as the overload without a callback reads it, and
    /// <paramref name="configure" /> runs over them: what code sets wins over what a setting says.
    /// </para>
    /// </remarks>
    /// <param name="builder">The scheduler's builder.</param>
    /// <param name="configure">Where the dashboard is, how to authenticate, and what the agent accepts.</param>
    public static IQuartzBuilder UseDashboardAgent(this IQuartzBuilder builder, Action<DashboardAgentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return Install(builder, configure);
    }

    private static IQuartzBuilder Install(IQuartzBuilder builder, Action<DashboardAgentOptions>? configure)
    {
        builder.Services.AddQuartzSchedulerEvents();
        builder.Services.AddQuartzExecutionHistory();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<DashboardAgentOptions>, DashboardAgentOptionsValidator>());

        // Registered before the callback is, because options are configured in registration order: the
        // settings are the starting point and code has the last word. Asked of the container rather than
        // required, since a container built from a bare ServiceCollection holds no configuration at all.
        string schedulerName = builder.SchedulerName;
        builder.Services.AddOptions<DashboardAgentOptions>(schedulerName)
            .Configure<IServiceProvider>((options, services) =>
                DashboardAgentConfiguration.Apply(options, services.GetService<IConfiguration>(), schedulerName));

        return builder.AddPlugin<DashboardAgentPlugin, DashboardAgentOptions>(configure, PluginName);
    }
}
