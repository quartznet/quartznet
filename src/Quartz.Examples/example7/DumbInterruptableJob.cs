/* 
* Copyright 2007 OpenSymphony 
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
using System;
using System.Threading;
using Common.Logging;
using Quartz;

namespace Quartz.Examples.Example7
{
	
	
	/// <summary>
	/// A dumb implementation of an InterruptableJob, for unittesting purposes.
	/// </summary>
	/// <author>  <a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a>
	/// </author>
	/// <author>Bill Kratzer</author>
	public class DumbInterruptableJob : IInterruptableJob
	{
		
		// logging services
		private static ILog _log = LogManager.GetLogger(typeof(DumbInterruptableJob));
		// has the job been interrupted?
		private bool _interrupted = false;
		// job name 
		private string _jobName = "";
		
		
		/// <summary>
		/// Called by the <see cref="org.quartz.Scheduler" /> when a <see cref="Trigger" />
		/// fires that is associated with the <see cref="IJob" />.
		/// </summary>
		public virtual void  Execute(JobExecutionContext context)
		{
			_jobName = context.JobDetail.FullName;
			_log.Info("---- " + _jobName + " executing at " + System.DateTime.Now.ToString("r"));
			
			try
			{
				// main job loop... see the JavaDOC for InterruptableJob for discussion...
				// do some work... in this example we are 'simulating' work by sleeping... :)
				
				for (int i = 0; i < 4; i++)
				{
					try
					{
						Thread.Sleep(10 * 1000);
					}
					catch (System.Exception ignore)
					{
						Console.WriteLine(ignore.StackTrace);
					}
					
					// periodically check if we've been interrupted...
					if (_interrupted)
					{
						_log.Info("--- " + _jobName + "  -- Interrupted... bailing out!");
						return ; // could also choose to throw a JobExecutionException 
						// if that made for sense based on the particular  
						// job's responsibilities/behaviors
					}
				}
			}
			finally
			{
				_log.Info("---- " + _jobName + " completed at " + System.DateTime.Now.ToString("r"));
			}
		}
		
		/// <summary>
		/// Called by the <see cref="IScheduler" /> when a user
		/// interrupts the <see cref="IJob" />.
		/// </summary>
		public virtual void  Interrupt()
		{
			_log.Info("---" + "  -- INTERRUPTING --");
			_interrupted = true;
		}

	}
}