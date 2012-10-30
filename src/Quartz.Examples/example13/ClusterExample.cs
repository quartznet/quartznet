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
    /// <para>
    /// All instances MUST use a different properties file, because their instance
    /// Ids must be different, however all other properties should be the same.
    /// </para>
    /// 
    /// <para>
    /// If you want it to clear out existing jobs & triggers, pass a command-line
    /// argument called "clearJobs".
    /// </para>
    /// 
    /// <para>
    /// You should probably start with a "fresh" set of tables (assuming you may
    /// have some data lingering in it from other tests), since mixing data from a
    /// non-clustered setup with a clustered one can be bad.
    /// </para>
    /// 
    /// <para>
    /// Try killing one of the cluster instances while they are running, and see
    /// that the remaining instance(s) recover the in-progress jobs. Note that
    /// detection of the failure may take up to 15 or so seconds with the default
    /// settings.
    /// </para>
    /// 
    /// <para>
    /// Also try running it with/without the shutdown-hook plugin registered with
    /// the scheduler. (quartz.plugins.management.ShutdownHookPlugin).
    /// </para>
    /// 
    /// <para>
    /// <i>Note:</i> Never run clustering on separate machines, unless their
    /// clocks are synchronized using some form of time-sync service (daemon).
    /// </para>
    /// </remarks>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class ClusterExample : IExample
    {
        private static readonly ILog log = LogManager.GetLogger(typeof (ClusterExample));

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
            // if running SQLite we need this
            // properties["quartz.jobStore.lockHandler.type"] = "Quartz.Impl.AdoJobStore.UpdateLockRowSemaphore, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";

            properties["quartz.dataSource.default.connectionString"] = "Server=(local);Database=quartz;Trusted_Connection=True;";
            properties["quartz.dataSource.default.provider"] = "SqlServer-20";

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = sf.GetScheduler();

            if (inClearJobs)
            {
                log.Warn("***** Deleting existing jobs/triggers *****");
                sched.Clear();
            }

            log.Info("------- Initialization Complete -----------");

            if (inScheduleJobs)
            {
                log.Info("------- Scheduling Jobs ------------------");

                string schedId = sched.SchedulerInstanceId;

                int count = 1;


                IJobDetail job = JobBuilder.Create<SimpleRecoveryJob>()
                    .WithIdentity("job_" + count, schedId) // put triggers in group named after the cluster node instance just to distinguish (in logging) what was scheduled from where
                    .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                    .Build();


                ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                                              .WithIdentity("triger_" + count, schedId)
                                                              .StartAt(DateBuilder.FutureDate(1, IntervalUnit.Second))
                                                              .WithSimpleSchedule(x => x.WithRepeatCount(20).WithInterval(TimeSpan.FromSeconds(5)))
                                                              .Build();

                log.InfoFormat("{0} will run at: {1} and repeat: {2} times, every {3} seconds", job.Key, trigger.GetNextFireTimeUtc(), trigger.RepeatCount, trigger.RepeatInterval.TotalSeconds);

                count++;


                job = JobBuilder.Create<SimpleRecoveryJob>()
                    .WithIdentity("job_" + count, schedId) // put triggers in group named after the cluster node instance just to distinguish (in logging) what was scheduled from where
                    .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                    .Build();

                trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                               .WithIdentity("triger_" + count, schedId)
                                               .StartAt(DateBuilder.FutureDate(2, IntervalUnit.Second))
                                               .WithSimpleSchedule(x => x.WithRepeatCount(20).WithInterval(TimeSpan.FromSeconds(5)))
                                               .Build();

                log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", job.Key, trigger.GetNextFireTimeUtc(), trigger.RepeatCount, trigger.RepeatInterval.TotalSeconds));
                sched.ScheduleJob(job, trigger);

                count++;


                job = JobBuilder.Create<SimpleRecoveryStatefulJob>()
                    .WithIdentity("job_" + count, schedId) // put triggers in group named after the cluster node instance just to distinguish (in logging) what was scheduled from where
                    .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                    .Build();

                trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                               .WithIdentity("triger_" + count, schedId)
                                               .StartAt(DateBuilder.FutureDate(1, IntervalUnit.Second))
                                               .WithSimpleSchedule(x => x.WithRepeatCount(20).WithInterval(TimeSpan.FromSeconds(3)))
                                               .Build();

                log.Info(string.Format("{0} will run at: {1} and repeat: {2} times, every {3} seconds", job.Key, trigger.GetNextFireTimeUtc(), trigger.RepeatCount, trigger.RepeatInterval.TotalSeconds));
                sched.ScheduleJob(job, trigger);

                count++;

                job = JobBuilder.Create<SimpleRecoveryJob>()
                    .WithIdentity("job_" + count, schedId) // put triggers in group named after the cluster node instance just to distinguish (in logging) what was scheduled from where
                    .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                    .Build();

                trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                               .WithIdentity("triger_" + count, schedId)
                                               .StartAt(DateBuilder.FutureDate(1, IntervalUnit.Second))
                                               .WithSimpleSchedule(x => x.WithRepeatCount(20).WithInterval(TimeSpan.FromSeconds(4)))
                                               .Build();

                log.Info(string.Format("{0} will run at: {1} & repeat: {2}/{3}", job.Key, trigger.GetNextFireTimeUtc(), trigger.RepeatCount, trigger.RepeatInterval));
                sched.ScheduleJob(job, trigger);

                count++;


                job = JobBuilder.Create<SimpleRecoveryJob>()
                    .WithIdentity("job_" + count, schedId) // put triggers in group named after the cluster node instance just to distinguish (in logging) what was scheduled from where
                    .RequestRecovery() // ask scheduler to re-execute this job if it was in progress when the scheduler went down...
                    .Build();

                trigger = (ISimpleTrigger) TriggerBuilder.Create()
                                               .WithIdentity("triger_" + count, schedId)
                                               .StartAt(DateBuilder.FutureDate(1, IntervalUnit.Second))
                                               .WithSimpleSchedule(x => x.WithRepeatCount(20).WithInterval(TimeSpan.FromMilliseconds(4500)))
                                               .Build();

                log.Info(string.Format("{0} will run at: {1} & repeat: {2}/{3}", job.Key, trigger.GetNextFireTimeUtc(), trigger.RepeatCount, trigger.RepeatInterval));
                sched.ScheduleJob(job, trigger);
            }

            // jobs don't start firing until start() has been called...
            log.Info("------- Starting Scheduler ---------------");
            sched.Start();
            log.Info("------- Started Scheduler ----------------");

            log.Info("------- Waiting for one hour... ----------");

            Thread.Sleep(TimeSpan.FromHours(1));


            log.Info("------- Shutting Down --------------------");
            sched.Shutdown();
            log.Info("------- Shutdown Complete ----------------");
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