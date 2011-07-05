#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

namespace Quartz.Spi
{
    /// <summary>
    /// Provides an interface for a class to become a "plugin" to Quartz.
    /// </summary>
    /// <remarks>
    /// Plugins can do virtually anything you wish, though the most interesting ones
    /// will obviously interact with the scheduler in some way - either actively: by
    /// invoking actions on the scheduler, or passively: by being a <see cref="IJobListener" />,
    /// <see cref="ITriggerListener" />, and/or <see cref="ISchedulerListener" />.
    /// <para>
    /// If you use <see cref="StdSchedulerFactory" /> to
    /// Initialize your Scheduler, it can also create and Initialize your plugins -
    /// look at the configuration docs for details.
    /// </para>
    /// <para>
    /// If you need direct access your plugin, you can have it explicitly put a 
    /// reference to itself in the <see cref="IScheduler" />'s 
    /// <see cref="SchedulerContext" /> as part of its
    /// <see cref="Initialize(string, IScheduler)" /> method.
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
        /// <see cref="Initialize(string, IScheduler)" /> method.
        /// </para>
        /// </remarks>
        /// <param name="pluginName">
        /// The name by which the plugin is identified.
        /// </param>
        /// <param name="sched">
        /// The scheduler to which the plugin is registered.
        /// </param>
        void Initialize(string pluginName, IScheduler sched);

        /// <summary>
        /// Called when the associated <see cref="IScheduler" /> is started, in order
        /// to let the plug-in know it can now make calls into the scheduler if it
        /// needs to.
        /// </summary>
        void Start();

        /// <summary>
        /// Called in order to inform the <see cref="ISchedulerPlugin" /> that it
        /// should free up all of it's resources because the scheduler is shutting
        /// down.
        /// </summary>
        void Shutdown();
    }
}