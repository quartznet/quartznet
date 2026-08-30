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

using Quartz.Impl;

namespace Quartz.Extensibility;

/// <summary>
/// Provides an interface for a class to become a "plugin" to Quartz.
/// </summary>
/// <remarks>
/// Plugins can do virtually anything you wish, though the most interesting ones
/// will obviously interact with the scheduler in some way - either actively: by
/// invoking actions on the scheduler, or passively: by being a <see cref="IJobListener" />,
/// <see cref="ITriggerListener" />, and/or <see cref="ISchedulerListener" />.
/// <para>
/// A plugin is registered with the scheduler it extends, either with <c>AddPlugin</c> or by a
/// <c>quartz.plugin.&lt;name&gt;.*</c> key, and the scheduler initializes and starts it -
/// look at the configuration docs for details.
/// </para>
/// <para>
/// If you need direct access your plugin, you can have it explicitly put a
/// reference to itself in the <see cref="IScheduler" />'s
/// <see cref="SchedulerContext" /> as part of its
/// <see cref="Initialize" /> method.
/// </para>
/// </remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface ISchedulerPlugin
{
    /// <summary>
    /// Called during creation of the <see cref="IScheduler" /> in order to give
    /// the <see cref="ISchedulerPlugin" /> a chance to Initialize.
    /// </summary>
    /// <remarks>
    /// At this point, the Scheduler's <see cref="IJobStore" /> is not yet
    /// <para>
    /// If you need direct access your plugin, you can have it explicitly put a
    /// reference to itself in the <see cref="IScheduler" />'s
    /// <see cref="SchedulerContext" /> as part of its
    /// <see cref="Initialize" /> method.
    /// </para>
    /// </remarks>
    /// <param name="pluginName">
    /// The name by which the plugin is identified.
    /// </param>
    /// <param name="scheduler">
    /// The scheduler to which the plugin is registered.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask Initialize(
        string pluginName,
        IScheduler scheduler,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when the associated <see cref="IScheduler" /> is started, in order
    /// to let the plug-in know it can now make calls into the scheduler if it
    /// needs to.
    /// </summary>
    /// <remarks>
    /// Does nothing unless the plugin says otherwise. Most plugins do all their work in
    /// <see cref="Initialize" /> — attaching a listener, registering a resolver — and have nothing to say
    /// at the two lifecycle moments; implement this only when there is something that cannot happen until
    /// the scheduler is running.
    /// </remarks>
    ValueTask Start(CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called in order to inform the <see cref="ISchedulerPlugin" /> that it
    /// should free up all of it's resources because the scheduler is shutting
    /// down.
    /// </summary>
    /// <inheritdoc cref="Start" path="/remarks" />
    ValueTask Shutdown(CancellationToken cancellationToken = default) => default;
}