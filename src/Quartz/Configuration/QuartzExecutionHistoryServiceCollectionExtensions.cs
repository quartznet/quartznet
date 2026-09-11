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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz;

public static partial class QuartzServiceCollectionExtensions
{
    /// <summary>
    /// Records what every scheduler in the container runs and misses, so that a dashboard, the HTTP API
    /// or code of your own can read it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registers three things: the recorder, added to every scheduler in the container through
    /// <see cref="ConfigureAllQuartzSchedulers" /> so a named scheduler is covered too; the bounds the
    /// history is kept under; and an <see cref="IExecutionHistoryStore" /> to record into, with
    /// <c>TryAdd</c> — so an application that registers a store of its own <em>before</em> this keeps it,
    /// and one that wants history it can query after a restart registers that store rather than
    /// configuring this one.
    /// </para>
    /// <para>
    /// Calling it twice installs one recorder. It is called by <c>AddQuartzHttpApi()</c> and by
    /// <c>AddQuartzDashboard()</c>, so an application that maps both — or that calls it itself as well —
    /// must not end up with every execution recorded two or three times. As with
    /// <see cref="ConfigureAllQuartzSchedulers" />, the order against <c>AddQuartz</c> does not matter.
    /// </para>
    /// <para>
    /// Set <see cref="ExecutionHistoryOptions.MaxEntriesPerScheduler" /> to <c>0</c> to record nothing,
    /// which is how a process that maps the HTTP API opts out of the history it turns on.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Configures the bounds the history is kept under.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    public static IServiceCollection AddQuartzExecutionHistory(
        this IServiceCollection services,
        Action<ExecutionHistoryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ExecutionHistoryOptions>, ExecutionHistoryOptionsValidator>());

        OptionsServiceCollectionExtensions.AddOptions<ExecutionHistoryOptions>(services).ValidateOnStart();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        // An implementation type rather than a factory, so that a caller can recognise the default -
        // the dashboard replaces it, and only it, when the application registered a history store of
        // its own through the 4.0 dashboard seam.
        services.TryAddSingleton<IExecutionHistoryStore, InMemoryExecutionHistoryStore>();

        if (HistoryRecorderAlreadyInstalled(services))
        {
            return services;
        }

        services.AddSingleton(new ExecutionHistoryRecorderMarker());
        services.ConfigureAllQuartzSchedulers(static quartz =>
            quartz.AddPlugin<ExecutionHistoryPlugin>(ExecutionHistoryPlugin.PluginName));

        return services;
    }

    private static bool HistoryRecorderAlreadyInstalled(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(ExecutionHistoryRecorderMarker))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Says the recorder has been installed, so a second call installs no second one.
    /// </summary>
    /// <remarks>
    /// A descriptor rather than a field, because the thing being made idempotent is a registration into
    /// one <see cref="IServiceCollection" /> — two collections in one process are two applications, and
    /// each needs its own recorder.
    /// </remarks>
    private sealed class ExecutionHistoryRecorderMarker;
}
