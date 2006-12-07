/* 
* Copyright 2005 OpenSymphony 
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
//UPGRADE_TODO: The type 'org.apache.commons.logging.Log' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using Log = org.apache.commons.logging.Log;
//UPGRADE_TODO: The type 'org.apache.commons.logging.LogFactory' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using LogFactory = org.apache.commons.logging.LogFactory;
//UPGRADE_TODO: The type 'org.quartz.JobDetail' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using JobDetail = org.quartz.JobDetail;
//UPGRADE_TODO: The type 'org.quartz.Scheduler' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using Scheduler = org.quartz.Scheduler;
//UPGRADE_TODO: The type 'org.quartz.SchedulerFactory' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using SchedulerFactory = org.quartz.SchedulerFactory;
//UPGRADE_TODO: The type 'org.quartz.SimpleTrigger' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using SimpleTrigger = org.quartz.SimpleTrigger;
//UPGRADE_TODO: The type 'org.quartz.impl.StdSchedulerFactory' could not be found. If it was not included in the conversion, there may be compiler issues. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1262'"
using StdSchedulerFactory = org.quartz.impl.StdSchedulerFactory;
namespace org.quartz.examples.example13
{
	
	/// <summary> Used to test/show the clustering features of JDBCJobStore (JobStoreTX or
	/// JobStoreCMT).
	/// 
	/// <p>
	/// All instances MUST use a different properties file, because their instance
	/// Ids must be different, however all other properties should be the same.
	/// </p>
	/// 
	/// <p>
	/// If you want it to clear out existing jobs & triggers, pass a command-line
	/// argument called "clearJobs".
	/// </p>
	/// 
	/// <p>
	/// You should probably start with a "fresh" set of tables (assuming you may
	/// have some data lingering in it from other tests), since mixing data from a
	/// non-clustered setup with a clustered one can be bad.
	/// </p>
	/// 
	/// <p>
	/// Try killing one of the cluster instances while they are running, and see
	/// that the remaining instance(s) recover the in-progress jobs. Note that
	/// detection of the failure may take up to 15 or so seconds with the default
	/// settings.
	/// </p>
	/// 
	/// <p>
	/// Also try running it with/without the shutdown-hook plugin registered with
	/// the scheduler. (org.quartz.plugins.management.ShutdownHookPlugin).
	/// </p>
	/// 
	/// <p>
	/// <i>Note:</i> Never run clustering on separate machines, unless their
	/// clocks are synchronized using some form of time-sync service (daemon).
	/// </p>
	/// 
	/// </summary>
	/// <seealso cref="DumbRecoveryJob">
	/// 
	/// </seealso>
	/// <author>  James House
	/// </author>
	public class ClusterExample
	{
		
		//UPGRADE_NOTE: The initialization of  '_log' was moved to static method 'org.quartz.examples.example13.ClusterExample'. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1005'"
		private static Log _log;
		
		public virtual void  cleanUp(Scheduler inScheduler)
		{
			_log.warn("***** Deleting existing jobs/triggers *****");
			
			// unschedule jobs
			System.String[] groups = inScheduler.getTriggerGroupNames();
			for (int i = 0; i < groups.Length; i++)
			{
				System.String[] names = inScheduler.getTriggerNames(groups[i]);
				for (int j = 0; j < names.Length; j++)
					inScheduler.unscheduleJob(names[j], groups[i]);
			}
			
			// delete jobs
			groups = inScheduler.getJobGroupNames();
			for (int i = 0; i < groups.Length; i++)
			{
				System.String[] names = inScheduler.getJobNames(groups[i]);
				for (int j = 0; j < names.Length; j++)
					inScheduler.deleteJob(names[j], groups[i]);
			}
		}
		
		public virtual void  run(bool inClearJobs, bool inScheduleJobs)
		{
			
			// First we must get a reference to a scheduler
			SchedulerFactory sf = new StdSchedulerFactory();
			Scheduler sched = sf.getScheduler();
			
			if (inClearJobs)
			{
				cleanUp(sched);
			}
			
			_log.info("------- Initialization Complete -----------");
			
			if (inScheduleJobs)
			{
				
				_log.info("------- Scheduling Jobs ------------------");
				
				System.String schedId = sched.getSchedulerInstanceId();
				
				int count = 1;
				
				JobDetail job = new JobDetail("job_" + count, schedId, typeof(SimpleRecoveryJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.setRequestsRecovery(true);
				SimpleTrigger trigger = new SimpleTrigger("triger_" + count, schedId, 20, 5000L);
				//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'System.DateTime.DateTime' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javautilDateDate_long'"
				trigger.setStartTime(new System.DateTime((System.DateTime.Now.Ticks - 621355968000000000) / 10000 + 1000L));
				_log.info(job.getFullName() + " will run at: " + trigger.getNextFireTime() + " and repeat: " + trigger.getRepeatCount() + " times, every " + (trigger.getRepeatInterval() / 1000) + " seconds");
				sched.scheduleJob(job, trigger);
				
				count++;
				job = new JobDetail("job_" + count, schedId, typeof(SimpleRecoveryJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.setRequestsRecovery(true);
				trigger = new SimpleTrigger("trig_" + count, schedId, 20, 5000L);
				//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'System.DateTime.DateTime' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javautilDateDate_long'"
				trigger.setStartTime(new System.DateTime((System.DateTime.Now.Ticks - 621355968000000000) / 10000 + 2000L));
				_log.info(job.getFullName() + " will run at: " + trigger.getNextFireTime() + " and repeat: " + trigger.getRepeatCount() + " times, every " + (trigger.getRepeatInterval() / 1000) + " seconds");
				sched.scheduleJob(job, trigger);
				
				count++;
				job = new JobDetail("job_" + count, schedId, typeof(SimpleRecoveryStatefulJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.setRequestsRecovery(true);
				trigger = new SimpleTrigger("trig_" + count, schedId, 20, 3000L);
				//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'System.DateTime.DateTime' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javautilDateDate_long'"
				trigger.setStartTime(new System.DateTime((System.DateTime.Now.Ticks - 621355968000000000) / 10000 + 1000L));
				_log.info(job.getFullName() + " will run at: " + trigger.getNextFireTime() + " and repeat: " + trigger.getRepeatCount() + " times, every " + (trigger.getRepeatInterval() / 1000) + " seconds");
				sched.scheduleJob(job, trigger);
				
				count++;
				job = new JobDetail("job_" + count, schedId, typeof(SimpleRecoveryJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.setRequestsRecovery(true);
				trigger = new SimpleTrigger("trig_" + count, schedId, 20, 4000L);
				//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'System.DateTime.DateTime' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javautilDateDate_long'"
				trigger.setStartTime(new System.DateTime((System.DateTime.Now.Ticks - 621355968000000000) / 10000 + 1000L));
				_log.info(job.getFullName() + " will run at: " + trigger.getNextFireTime() + " & repeat: " + trigger.getRepeatCount() + "/" + trigger.getRepeatInterval());
				sched.scheduleJob(job, trigger);
				
				count++;
				job = new JobDetail("job_" + count, schedId, typeof(SimpleRecoveryJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.setRequestsRecovery(true);
				trigger = new SimpleTrigger("trig_" + count, schedId, 20, 4500L);
				//UPGRADE_TODO: Constructor 'java.util.Date.Date' was converted to 'System.DateTime.DateTime' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javautilDateDate_long'"
				trigger.setStartTime(new System.DateTime((System.DateTime.Now.Ticks - 621355968000000000) / 10000 + 1000L));
				_log.info(job.getFullName() + " will run at: " + trigger.getNextFireTime() + " & repeat: " + trigger.getRepeatCount() + "/" + trigger.getRepeatInterval());
				sched.scheduleJob(job, trigger);
			}
			
			// jobs don't start firing until start() has been called...
			_log.info("------- Starting Scheduler ---------------");
			sched.start();
			_log.info("------- Started Scheduler ----------------");
			
			_log.info("------- Waiting for one hour... ----------");
			try
			{
				//UPGRADE_TODO: Method 'java.lang.Thread.sleep' was converted to 'System.Threading.Thread.Sleep' which has a different behavior. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1073_javalangThreadsleep_long'"
				System.Threading.Thread.Sleep(new System.TimeSpan((System.Int64) 10000 * 3600L * 1000L));
			}
			catch (System.Exception e)
			{
			}
			
			_log.info("------- Shutting Down --------------------");
			sched.shutdown();
			_log.info("------- Shutdown Complete ----------------");
		}
		
		[STAThread]
		public static void  Main(System.String[] args)
		{
			bool clearJobs = false;
			bool scheduleJobs = true;
			
			for (int i = 0; i < args.Length; i++)
			{
				if (args[i].ToUpper().Equals("clearJobs".ToUpper()))
				{
					clearJobs = true;
				}
				else if (args[i].ToUpper().Equals("dontScheduleJobs".ToUpper()))
				{
					scheduleJobs = false;
				}
			}
			
			ClusterExample example = new ClusterExample();
			example.run(clearJobs, scheduleJobs);
		}
		static ClusterExample()
		{
			_log = LogFactory.getLog(typeof(ClusterExample));
		}
	}
}