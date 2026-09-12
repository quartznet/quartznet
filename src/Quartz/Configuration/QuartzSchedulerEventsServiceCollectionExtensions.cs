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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz;

public static partial class QuartzServiceCollectionExtensions
{
    /// <summary>
    /// Streams what every scheduler in the container does, so that a dashboard, the HTTP API or code of
    /// your own can watch it happen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registers two things: the broker the events are carried by, which is also the process's
    /// <c>ISchedulerEventSource</c>, and one publisher added to every scheduler in the container through
    /// <see cref="ConfigureAllQuartzSchedulers" /> — so a named scheduler is covered too, and the order
    /// against <c>AddQuartz</c> does not matter.
    /// </para>
    /// <para>
    /// Calling it twice installs one publisher. It is called by <c>AddQuartzHttpApi()</c> and by
    /// <c>AddQuartzDashboard()</c>, so an application that maps both — or that calls it itself as well —
    /// must not end up with every event on the stream two or three times. The same shape
    /// <see cref="AddQuartzExecutionHistory" /> uses, for the same reason.
    /// </para>
    /// <para>
    /// A process nobody is watching pays almost nothing: the publisher asks the broker whether the
    /// scheduler has a subscriber before it builds an event, and a publication with no subscriber writes
    /// nothing anywhere.
    /// </para>
    /// <para>
    /// Internal in 4.1. Everything it registers is internal, so there is nothing an application could do
    /// with the registration that the HTTP API's event route and the dashboard do not already do for it.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    internal static IServiceCollection AddQuartzSchedulerEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<SchedulerEventBroker>();

        // The broker is this process's event source, registered under the interface as well so that a
        // reader resolving "the source for this scheduler" gets it when no keyed one says the scheduler is
        // somewhere else. TryAdd, because a container that registered a source of its own said so first.
        services.TryAddSingleton<ISchedulerEventSource>(
            static provider => provider.GetRequiredService<SchedulerEventBroker>());

        if (EventPublisherAlreadyInstalled(services))
        {
            return services;
        }

        services.AddSingleton(new SchedulerEventPublisherMarker());
        services.ConfigureAllQuartzSchedulers(static quartz =>
            quartz.AddPlugin<SchedulerEventPlugin>(SchedulerEventPlugin.PluginName));

        return services;
    }

    private static bool EventPublisherAlreadyInstalled(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(SchedulerEventPublisherMarker))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Says the publisher has been installed, so a second call installs no second one.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="ExecutionHistoryRecorderMarker" path="/remarks" />
    /// </remarks>
    private sealed class SchedulerEventPublisherMarker;
}
