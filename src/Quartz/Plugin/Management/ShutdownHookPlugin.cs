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
using System;

using Common.Logging;

using Quartz.Spi;

namespace Quartz.Plugins.Management
{
	/// <summary> 
	/// This plugin catches the event of the VM terminating (such as upon a CRTL-C)
	/// and tells the scheuler to Shutdown.
	/// </summary>
	/// <seealso cref="IScheduler.Shutdown(bool)" />
	/// <author>James House</author>
	public class ShutdownHookPlugin : ISchedulerPlugin
	{
        private string name;
        private IScheduler scheduler;
        private bool cleanShutdown = true;

		private static readonly ILog Log = LogManager.GetLogger(typeof (ShutdownHookPlugin));

		private class AnonymousClassThread : QuartzThread
		{
            private IScheduler scheduler;
            private ShutdownHookPlugin encInstance;

			private void InitBlock(IScheduler sched, ShutdownHookPlugin enclosingInstance)
			{
				scheduler = sched;
				encInstance = enclosingInstance;
			}



			public ShutdownHookPlugin EnclosingInstance
			{
				get { return encInstance; }
			}

			internal AnonymousClassThread(IScheduler scheduler, ShutdownHookPlugin enclosingInstance, string Param1)
				: base(Param1)
			{
				InitBlock(scheduler, enclosingInstance);
			}

			public override void Run()
			{
				Log.Info("Shutting down Quartz...");
				try
				{
					EnclosingInstance.scheduler.Shutdown(EnclosingInstance.CleanShutdown);
				}
				catch (SchedulerException e)
				{
					Log.Info("Error shutting down Quartz: " + e.Message, e);
				}
			}
		}

		/// <summary> 
		/// Determine whether or not the plug-in is configured to cause a clean
		/// Shutdown of the scheduler.
		/// <p>
		/// The default value is <code>true</code>.
		/// </p>
		/// </summary>
		/// <seealso cref="IScheduler.Shutdown(bool)" />
		public virtual bool CleanShutdown
		{
			get { return cleanShutdown; }
			set { cleanShutdown = value; }
		}


		/// <summary>
		/// Called during creation of the <code>Scheduler</code> in order to give
		/// the <code>SchedulerPlugin</code> a chance to Initialize.
		/// </summary>
		public virtual void Initialize(String pluginName, IScheduler sched)
		{
			name = pluginName;
			scheduler = sched;

			Log.Info(string.Format("Registering Quartz Shutdown hook '{0}.", pluginName));

			QuartzThread t =
				new AnonymousClassThread(sched, this, "Quartz Shutdown-Hook " + sched.SchedulerName);

			// TODO
			//Process.GetCurrentProcess().addShutdownHook(t.Instance);
		}

        /// <summary>
        /// Called when the associated <code>Scheduler</code> is started, in order
        /// to let the plug-in know it can now make calls into the scheduler if it
        /// needs to.
        /// </summary>
		public virtual void Start()
		{
			// do nothing.
		}

		/// <summary>
		/// Called in order to inform the <code>SchedulerPlugin</code> that it
		/// should free up all of it's resources because the scheduler is shutting
		/// down.
		/// </summary>
		public virtual void Shutdown()
		{
			// nothing to do in this case (since the scheduler is already shutting
			// down)
		}
	}
}