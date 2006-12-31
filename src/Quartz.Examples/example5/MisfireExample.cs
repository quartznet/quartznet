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
using Quartz.Examples;
using Quartz.Impl;

namespace Quartz.Examples.Example5
{
	
	/// <summary> Demonstrates the behavior of <code>StatefulJob</code>s, as well as how
	/// misfire instructions affect the firings of triggers of <code>StatefulJob</code>
	/// s - when the jobs take longer to execute that the frequency of the trigger's
	/// repitition.
	/// 
	/// <p>
	/// While the example is running, you should note that there are two triggers
	/// with identical schedules, firing identical jobs. The triggers "want" to fire
	/// every 3 seconds, but the jobs take 10 seconds to execute. Therefore, by the
	/// time the jobs complete their execution, the triggers have already "misfired"
	/// (unless the scheduler's "misfire threshold" has been set to more than 7
	/// seconds). You should see that one of the jobs has its misfire instruction
	/// set to <code>SimpleTrigger.MISFIRE_INSTRUCTION_RESCHEDULE_NOW_WITH_EXISTING_REPEAT_COUNT</code>,
	/// which causes it to fire immediately, when the misfire is detected. The other
	/// trigger uses the default "smart policy" misfire instruction, which causes
	/// the trigger to advance to its next fire time (skipping those that it has
	/// missed) - so that it does not refire immediately, but rather at the next
	/// scheduled time.
	/// </p>
	/// 
	/// </summary>
	/// <author>  <a href="mailto:bonhamcm@thirdeyeconsulting.com">Chris Bonham</a>
	/// </author>
	public class MisfireExample : IExample
	{
		public string Name
		{
			get { throw new NotImplementedException(); }
		}

		public virtual void  Run()
		{
			ILog log = LogManager.GetLogger(typeof(MisfireExample));
			
			log.Info("------- Initializing -------------------");
			
			// First we must get a reference to a scheduler
			ISchedulerFactory sf = new StdSchedulerFactory();
			IScheduler sched = sf.GetScheduler();
			
			log.Info("------- Initialization Complete -----------");
			
			log.Info("------- Scheduling Jobs -----------");
			
			// jobs can be scheduled before start() has been called
			
			// get a "nice round" time a few seconds in the future...
			DateTime ts = TriggerUtils.GetNextGivenSecondDate(null, 15);
			
			// statefulJob1 will run every three seconds
			// (but it will delay for ten seconds)
			JobDetail job = new JobDetail("statefulJob1", "group1", typeof(StatefulDumbJob));
			job.JobDataMap.Put(MisfireJob.EXECUTION_DELAY, 10);
			
			SimpleTrigger trigger = new SimpleTrigger("trigger1", "group1", ts, null, SimpleTrigger.REPEAT_INDEFINITELY, 3000L);
			DateTime ft = sched.ScheduleJob(job, trigger);
			log.Info(job.FullName + " will run at: " + ft.ToString("r") + " and repeat: " + trigger.RepeatCount + " times, every " + (trigger.RepeatInterval / 1000) + " seconds");
			
			// statefulJob2 will run every three seconds
			// (but it will delay for ten seconds)
			job = new JobDetail("statefulJob2", "group1", typeof(StatefulDumbJob));
			job.JobDataMap.Put(MisfireJob.EXECUTION_DELAY, 10);

			trigger = new SimpleTrigger("trigger2", "group1", ts, null, SimpleTrigger.REPEAT_INDEFINITELY, 3000L);
			trigger.MisfireInstruction = SimpleTrigger.MISFIRE_INSTRUCTION_RESCHEDULE_NOW_WITH_EXISTING_REPEAT_COUNT;
			ft = sched.ScheduleJob(job, trigger);

			log.Info(job.FullName + " will run at: " + ft.ToString("r") + " and repeat: " + trigger.RepeatCount + " times, every " + (trigger.RepeatInterval / 1000) + " seconds");
			
			log.Info("------- Starting Scheduler ----------------");
			
			// jobs don't start firing until start() has been called...
			sched.Start();
			
			log.Info("------- Started Scheduler -----------------");
			
			try
			{
				// sleep for ten minutes for triggers to file....
				Thread.Sleep(600 * 1000);
			}
			catch (Exception)
			{
			}
			
			log.Info("------- Shutting Down ---------------------");
			
			sched.Shutdown(true);
			
			log.Info("------- Shutdown Complete -----------------");
			
			SchedulerMetaData metaData = sched.GetMetaData();
			log.Info("Executed " + metaData.NumJobsExecuted + " jobs.");
		}
		
	}
}