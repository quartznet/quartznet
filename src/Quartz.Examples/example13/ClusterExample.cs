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
using System.Collections.Specialized;
using System.Threading;
using Common.Logging;
using Quartz.Impl;

namespace Quartz.Examples.Example13
{
	/// <summary> 
	/// Used to test/show the clustering features of AdoJobStore.
	/// </summary>
	/// <remarks>
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
	/// the scheduler. (quartz.plugins.management.ShutdownHookPlugin).
	/// </p>
	/// 
	/// <p>
	/// <i>Note:</i> Never run clustering on separate machines, unless their
	/// clocks are synchronized using some form of time-sync service (daemon).
	/// </p>
    /// </remarks>
	/// <author>James House</author>
	public class ClusterExample : IExample
	{
		private static ILog _log = LogManager.GetLogger(typeof (ClusterExample));

		public virtual void CleanUp(IScheduler inScheduler)
		{
			_log.Warn("***** Deleting existing jobs/triggers *****");

			// unschedule jobs
			string[] groups = inScheduler.TriggerGroupNames;
			for (int i = 0; i < groups.Length; i++)
			{
				String[] names = inScheduler.GetTriggerNames(groups[i]);
				for (int j = 0; j < names.Length; j++)
					inScheduler.UnscheduleJob(names[j], groups[i]);
			}

			// delete jobs
			groups = inScheduler.JobGroupNames;
			for (int i = 0; i < groups.Length; i++)
			{
				String[] names = inScheduler.GetJobNames(groups[i]);
				for (int j = 0; j < names.Length; j++)
					inScheduler.DeleteJob(names[j], groups[i]);
			}
		}

		public virtual void Run(bool inClearJobs, bool inScheduleJobs)
		{
            NameValueCollection properties = new NameValueCollection();

		    properties["quartz.scheduler.instanceName"] = "TestScheduler";
		    properties["quartz.scheduler.instanceId"] = "instance_one";
            properties["quartz.threadPool.class"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
		    properties["quartz.threadPool.threadCount"] = "5";
            properties["quartz.threadPool.threadPriority"] = "Normal";
		    properties["quartz.jobStore.misfireThreshold"] = "60000";
            properties["quartz.jobStore.class"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MSSQLDelegate, Quartz";
            properties["quartz.jobStore.useProperties"] = "false";
            properties["quartz.jobStore.dataSource"] = "default";
		    properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
		    properties["quartz.jobStore.clustered"] = "true";
            // if running MS SQL Server we need this
		    properties["quartz.jobStore.selectWithLockSQL"] = "SELECT * FROM {0}LOCKS UPDLOCK WHERE LOCK_NAME = ?";

            properties["quartz.dataSource.default.connectionString"] = "Server=(local);Database=quartz;Trusted_Connection=True;";
            properties["quartz.dataSource.default.provider"] = "SqlServer-11";

			// First we must get a reference to a scheduler
			ISchedulerFactory sf = new StdSchedulerFactory(properties);
			IScheduler sched = sf.GetScheduler();

			if (inClearJobs)
			{
				CleanUp(sched);
			}

			_log.Info("------- Initialization Complete -----------");

			if (inScheduleJobs)
			{
				_log.Info("------- Scheduling Jobs ------------------");

				string schedId = sched.SchedulerInstanceId;

				int count = 1;

				JobDetail job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.RequestsRecovery = true;
				SimpleTrigger trigger = new SimpleTrigger("triger_" + count, schedId, 20, 5000L);

				trigger.StartTime = new DateTime((DateTime.Now.Ticks - 621355968000000000)/10000 + 1000L);
				_log.Info(job.FullName + " will run at: " + trigger.GetNextFireTime() + " and repeat: " + trigger.RepeatCount +
				          " times, every " + (trigger.RepeatInterval/1000) + " seconds");
				sched.ScheduleJob(job, trigger);

				count++;
				job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.RequestsRecovery = (true);
				trigger = new SimpleTrigger("trig_" + count, schedId, 20, 5000L);

				trigger.StartTime = (new DateTime((DateTime.Now.Ticks - 621355968000000000)/10000 + 2000L));
				_log.Info(job.FullName + " will run at: " + trigger.GetNextFireTime() + " and repeat: " + trigger.RepeatCount +
				          " times, every " + (trigger.RepeatInterval/1000) + " seconds");
				sched.ScheduleJob(job, trigger);

				count++;
				job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryStatefulJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.RequestsRecovery = (true);
				trigger = new SimpleTrigger("trig_" + count, schedId, 20, 3000L);

				trigger.StartTime = (new DateTime((DateTime.Now.Ticks - 621355968000000000)/10000 + 1000L));
				_log.Info(job.FullName + " will run at: " + trigger.GetNextFireTime() + " and repeat: " + trigger.RepeatCount +
				          " times, every " + (trigger.RepeatInterval/1000) + " seconds");
				sched.ScheduleJob(job, trigger);

				count++;
				job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.RequestsRecovery = (true);
				trigger = new SimpleTrigger("trig_" + count, schedId, 20, 4000L);

				trigger.StartTime = (new DateTime((DateTime.Now.Ticks - 621355968000000000)/10000 + 1000L));
				_log.Info(job.FullName + " will run at: " + trigger.GetNextFireTime() + " & repeat: " + trigger.RepeatCount + "/" +
				          trigger.RepeatInterval);
				sched.ScheduleJob(job, trigger);

				count++;
				job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.RequestsRecovery = (true);
				trigger = new SimpleTrigger("trig_" + count, schedId, 20, 4500L);

				trigger.StartTime = (new DateTime((DateTime.Now.Ticks - 621355968000000000)/10000 + 1000L));
				_log.Info(job.FullName + " will run at: " + trigger.GetNextFireTime() + " & repeat: " + trigger.RepeatCount + "/" +
				          trigger.RepeatInterval);
				sched.ScheduleJob(job, trigger);
			}

			// jobs don't start firing until start() has been called...
			_log.Info("------- Starting Scheduler ---------------");
			sched.Start();
			_log.Info("------- Started Scheduler ----------------");

			_log.Info("------- Waiting for one hour... ----------");

			Thread.Sleep(TimeSpan.FromHours(1));


			_log.Info("------- Shutting Down --------------------");
			sched.Shutdown();
			_log.Info("------- Shutdown Complete ----------------");
		}

		public string Name
		{
			get { throw new NotImplementedException(); }
		}

		public void Run()
		{
			bool clearJobs = false;
			bool scheduleJobs = true;
			/* TODO
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
			*/
			ClusterExample example = new ClusterExample();
			example.Run(clearJobs, scheduleJobs);

		}
	}
}