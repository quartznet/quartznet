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

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Dashboard.Services;

/// <summary>
/// Where one scheduler's live events are read from.
/// </summary>
/// <remarks>
/// <para>
/// The same decision <c>InProcessQuartzApiClient</c> makes about a scheduler's history, for the same
/// reason: a scheduler reached through a target has a reader of that target's stream, and the target is
/// what says the scheduler is somewhere else. A key naming a target asks the target, through
/// <see cref="SchedulerTargets" />; a bare key asks for the reader an untargeted
/// <c>AddQuartzHttpClient</c> registration keyed by the scheduler's name, and otherwise reads this
/// process's own broker, which <c>AddQuartzSchedulerEvents()</c> registered.
/// </para>
/// <para>
/// One place, because two things read it: the page a visitor is looking at, and the forwarder that feeds
/// the dashboard's hub for clients of an application's own. They must agree about which stream a
/// scheduler's key means.
/// </para>
/// </remarks>
internal static class SchedulerEventSources
{
    /// <summary>
    /// The source <paramref name="schedulerKey" />'s events are read from, or <see langword="null" />
    /// when this container holds none for it.
    /// </summary>
    /// <remarks>
    /// Null rather than a throw: a container that registered no event stream is a container whose Live
    /// Logs page has nothing to show, and saying so is the page's job. So is a window, whose database
    /// carries a schedule rather than a feed of what happened to it.
    /// </remarks>
    public static ISchedulerEventSource? For(IServiceProvider serviceProvider, string? schedulerKey)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (string.IsNullOrWhiteSpace(schedulerKey))
        {
            return serviceProvider.GetService<ISchedulerEventSource>();
        }

        // Read the way the repository resolves it, so a scheduler of this process named like a key keeps
        // its own events rather than a target's.
        SchedulerRef key = SchedulerLookup.Read(serviceProvider.GetService<ISchedulerRepository>(), schedulerKey);
        string schedulerName = key.SchedulerName;

        if (key.Target is not null)
        {
            if (serviceProvider.GetService<SchedulerTargets>()?.Find(key.Target) is { } target)
            {
                return target.Events?.Invoke(serviceProvider, schedulerName);
            }

            // A key spelled like a local scheduler whose name contains '/': the bare rule, by the whole key.
            schedulerName = schedulerKey;
        }

        // A window has none, and the container's own broker is emphatically not it: the events in it
        // are this process's schedulers', and showing them under a window's name would attribute
        // another cluster's firings to a scheduler that has never run anything here. A shared database
        // carries no event feed — that is what an HTTP or agent target is for.
        if (serviceProvider.GetService<AttachedStores>()?.IsWindow(schedulerName) == true)
        {
            return null;
        }

        // A container that does not do keyed services holds no per-scheduler source either, so asking it
        // would only be a way to throw.
        if (serviceProvider is IKeyedServiceProvider keyed
            && keyed.GetKeyedService(typeof(ISchedulerEventSource), schedulerName) is ISchedulerEventSource remote)
        {
            return remote;
        }

        return serviceProvider.GetService<ISchedulerEventSource>();
    }
}
