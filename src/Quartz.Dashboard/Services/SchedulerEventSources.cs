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

using Quartz.Extensibility;

namespace Quartz.Dashboard.Services;

/// <summary>
/// Where one scheduler's live events are read from.
/// </summary>
/// <remarks>
/// <para>
/// The same decision <c>InProcessQuartzApiClient</c> makes about a scheduler's history, for the same
/// reason: a scheduler registered with <c>AddQuartzHttpClient</c> has a reader of its target's stream
/// keyed by its name, and that registration is what says the scheduler is somewhere else. Every other
/// scheduler's events are this process's, published into the broker
/// <c>AddQuartzSchedulerEvents()</c> registered.
/// </para>
/// <para>
/// One place, because two things read it: the page a visitor is looking at, and the forwarder that feeds
/// the dashboard's hub for clients of an application's own. They must agree about which stream a
/// scheduler's name means.
/// </para>
/// </remarks>
internal static class SchedulerEventSources
{
    /// <summary>
    /// The source <paramref name="schedulerName" />'s events are read from, or <see langword="null" />
    /// when this container holds none at all.
    /// </summary>
    /// <remarks>
    /// Null rather than a throw: a container that registered no event stream is a container whose Live
    /// Logs page has nothing to show, and saying so is the page's job.
    /// </remarks>
    public static ISchedulerEventSource? For(IServiceProvider serviceProvider, string? schedulerName)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        // A container that does not do keyed services holds no per-scheduler source either, so asking it
        // would only be a way to throw.
        if (!string.IsNullOrWhiteSpace(schedulerName) && serviceProvider is IKeyedServiceProvider keyed
            && keyed.GetKeyedService(typeof(ISchedulerEventSource), schedulerName) is ISchedulerEventSource remote)
        {
            return remote;
        }

        return serviceProvider.GetService<ISchedulerEventSource>();
    }
}
