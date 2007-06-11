/* 
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

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
	/// </p>
	/// 
	/// <p>
	/// If you use <see cref="StdSchedulerFactory" /> to
	/// Initialize your Scheduler, it can also create and Initialize your plugins -
	/// look at the configuration docs for details.
	/// </p>
	/// 
	/// </remarks>
	/// <author>James House</author>
	public interface ISchedulerPlugin
	{
		/// <summary>
		/// Called during creation of the <see cref="IScheduler" /> in order to give
		/// the <see cref="ISchedulerPlugin" /> a chance to Initialize.
		/// <p>
		/// At this point, the Scheduler's <see cref="IJobStore" /> is not yet
		/// initialized.
		/// </p>
		/// </summary>
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