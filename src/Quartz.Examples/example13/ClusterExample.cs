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

using System;
using System.Collections.Generic;
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
    /// <author>Marko Lahma (.NET)</author>
    public class ClusterExample : IExample
	{
		private static ILog _log = LogManager.GetLogger(typeof (ClusterExample));

		public virtual void CleanUp(IScheduler inScheduler)
		{
			_log.Warn("***** Deleting existing jobs/triggers *****");

			// unschedule jobs
            IList<string> groups = inScheduler.TriggerGroupNames;
			foreach (string groupName in groups)
			{
			    IList<string> names = inScheduler.GetTriggerNames(groupName);
			    foreach (string triggerName in names)
			    {
			        inScheduler.UnscheduleJob(triggerName, groupName);
			    }
			}

		    // delete jobs
			groups = inScheduler.JobGroupNames;
			foreach (string groupName in groups)
			{
			    IList<string> names = inScheduler.GetJobNames(groupName);
			    foreach (string jobName in names)
			    {
			        inScheduler.DeleteJob(jobName, groupName);
			    }
			}
		}

		public virtual void Run(bool inClearJobs, bool inScheduleJobs)
		{
            NameValueCollection properties = new NameValueCollection();

		    properties["quartz.scheduler.instanceName"] = "TestScheduler";
		    properties["quartz.scheduler.instanceId"] = "instance_one";
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
		    properties["quartz.threadPool.threadCount"] = "5";
            properties["quartz.threadPool.threadPriority"] = "Normal";
		    properties["quartz.jobStore.misfireThreshold"] = "60000";
            properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.useProperties"] = "false";
            properties["quartz.jobStore.dataSource"] = "default";
		    properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
		    properties["quartz.jobStore.clustered"] = "true";
            // if running MS SQL Server we need this
            properties["quartz.jobStore.lockHandler.type"] = "Quartz.Impl.AdoJobStore.UpdateLockRowSemaphore, Quartz";

            properties["quartz.dataSource.default.connectionString"] = "Server=(local);Database=quartz;Trusted_Connection=True;";
            properties["quartz.dataSource.default.provider"] = "SqlServer-20";

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
				SimpleTrigger trigger = new SimpleTrigger("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(5000));

				trigger.StartTimeUtc = DateTime.UtcNow.AddMilliseconds(1000);
				sched.ScheduleJob(job, trigger);
                _log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", job.FullName, trigger.GetNextFireTimeUtc(), trigger.RepeatCount, trigger.RepeatInterval.TotalSeconds));

				count++;
				job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.RequestsRecovery = (true);
				trigger = new SimpleTrigger("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(5000));

                trigger.StartTimeUtc = DateTime.UtcNow.AddMilliseconds(2000);
				sched.ScheduleJob(job, trigger);
                _log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", job.FullName, trigger.GetNextFireTimeUtc(), trigger.RepeatCount, trigger.RepeatInterval.TotalSeconds));

				count++;
				job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryStatefulJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.RequestsRecovery = true;
				trigger = new SimpleTrigger("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(3000));

                trigger.StartTimeUtc = DateTime.UtcNow.AddMilliseconds(1000);
				sched.ScheduleJob(job, trigger);
                _log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", job.FullName, trigger.GetNextFireTimeUtc(), trigger.RepeatCount, trigger.RepeatInterval.TotalSeconds));

				count++;
				job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.RequestsRecovery = true;
				trigger = new SimpleTrigger("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(4000));

                trigger.StartTimeUtc = (DateTime.UtcNow.AddMilliseconds(1000L));
				sched.ScheduleJob(job, trigger);
                _log.Info(string.Format("{0} will run at: {1} & repeat: {2}/{3}", job.FullName, trigger.GetNextFireTimeUtc(), trigger.RepeatCount, trigger.RepeatInterval));

				count++;
				job = new JobDetail("job_" + count, schedId, typeof (SimpleRecoveryJob));
				// ask scheduler to re-Execute this job if it was in progress when
				// the scheduler went down...
				job.RequestsRecovery = true;
				trigger = new SimpleTrigger("trig_" + count, schedId, 20, TimeSpan.FromMilliseconds(4500));

                trigger.StartTimeUtc = DateTime.UtcNow.AddMilliseconds(1000);
				sched.ScheduleJob(job, trigger);
                _log.Info(string.Format("{0} will run at: {1} & repeat: {2}/{3}", job.FullName, trigger.GetNextFireTimeUtc(), trigger.RepeatCount, trigger.RepeatInterval));
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
			bool clearJobs = true;
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