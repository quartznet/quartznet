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
	/// <summary> This plugin catches the event of the JVM terminating (such as upon a CRTL-C)
	/// and tells the scheuler to Shutdown.
	/// 
	/// </summary>
	/// <seealso cref="IScheduler.Shutdown(bool)">
	/// 
	/// </seealso>
	/// <author>  James House
	/// </author>
	public class ShutdownHookPlugin : ISchedulerPlugin
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (ShutdownHookPlugin));

		private class AnonymousClassThread : SupportClass.QuartzThread
		{
			private void InitBlock(IScheduler scheduler, ShutdownHookPlugin enclosingInstance)
			{
				this.scheduler = scheduler;
				this.enclosingInstance = enclosingInstance;
			}

			private IScheduler scheduler;
			private ShutdownHookPlugin enclosingInstance;

			public ShutdownHookPlugin Enclosing_Instance
			{
				get { return enclosingInstance; }
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
					Enclosing_Instance.scheduler.Shutdown(Enclosing_Instance.CleanShutdown);
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
		/// <seealso cref="IScheduler.Shutdown(bool)">
		/// </seealso>
		public virtual bool CleanShutdown
		{
			get { return cleanShutdown; }
			set { cleanShutdown = value; }
		}


		private string name;
		private IScheduler scheduler;
		private bool cleanShutdown = true;


		/// <summary> <p>
		/// Called during creation of the <code>Scheduler</code> in order to give
		/// the <code>SchedulerPlugin</code> a chance to Initialize.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  SchedulerConfigException </throws>
		/// <summary>           if there is an error initializing.
		/// </summary>
		public virtual void Initialize(String pluginName, IScheduler sched)
		{
			name = pluginName;
			scheduler = sched;

			Log.Info(string.Format("Registering Quartz Shutdown hook '{0}.", pluginName));

			SupportClass.QuartzThread t =
				new AnonymousClassThread(sched, this, "Quartz Shutdown-Hook " + sched.SchedulerName);

			// TODO
			// Process.GetCurrentProcess().addShutdownHook(t.Instance);
		}

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