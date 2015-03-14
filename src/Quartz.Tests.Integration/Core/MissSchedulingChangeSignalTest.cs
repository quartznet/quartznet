using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;

using Common.Logging;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Integration.Core
{
    public class MissSchedulingChangeSignalTest
    {
        private static readonly ILog log = LogManager.GetLogger<MissSchedulingChangeSignalTest>();

        [Test]
        [Explicit]
        public void SimpleScheduleAlwaysFiredUnder20S()
        {
            NameValueCollection properties = new NameValueCollection();
            // Use a custom RAMJobStore to produce context switches leading to the race condition
            properties["quartz.jobStore.type"] = typeof (SlowRAMJobStore).AssemblyQualifiedName;
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = sf.GetScheduler();
            log.Info("------- Initialization Complete -----------");

            log.Info("------- Scheduling Job  -------------------");

            IJobDetail job = JobBuilder.Create<CollectDurationBetweenFireTimesJob>().WithIdentity("job", "group").Build();

            ITrigger trigger = TriggerBuilder.Create()
                                             .WithIdentity("trigger1", "group1")
                                             .StartAt(DateTime.UtcNow.AddSeconds(1))
                                             .WithSimpleSchedule(x => x
                                                                          .WithIntervalInSeconds(1)
                                                                          .RepeatForever()
                                                                          .WithMisfireHandlingInstructionIgnoreMisfires())
                                             .Build();

            sched.ScheduleJob(job, trigger);

            // Start up the scheduler (nothing can actually run until the
            // scheduler has been started)
            sched.Start();

            log.Info("------- Scheduler Started -----------------");

            // wait long enough so that the scheduler has an opportunity to
            // run the job in theory around 50 times
            try
            {
                Thread.Sleep(50000);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            List<TimeSpan> durationBetweenFireTimesInMillis = CollectDurationBetweenFireTimesJob.Durations;

            Assert.False(durationBetweenFireTimesInMillis.Count == 0, "Job was not executed once!");

            // Let's check that every call for around 1 second and not between 23 and 30 seconds
            // which would be the case if the scheduling change signal were not checked
            foreach (TimeSpan durationInMillis in durationBetweenFireTimesInMillis)
            {
                Assert.True(durationInMillis.TotalMilliseconds < 20000, "Missed an execution with one duration being between two fires: " + durationInMillis + " (all: "
                                                                        + durationBetweenFireTimesInMillis + ")");
            }
        }
    }

    /// <summary>
    /// A simple job for collecting fire times in order to check that we did not miss one call, for having the race
    ///  condition the job must be real quick and not allowing concurrent executions.
    /// </summary>
    public class CollectDurationBetweenFireTimesJob : IJob
    {
        private static DateTime? lastFireTime = null;
        private static List<TimeSpan> durationBetweenFireTimes = new List<TimeSpan>();
        private static readonly ILog log = LogManager.GetLogger<CollectDurationBetweenFireTimesJob>();

        public void Execute(IJobExecutionContext context)
        {
            DateTime now = DateTime.UtcNow;
            log.Info("Fire time: " + now);
            if (lastFireTime != null)
            {
                durationBetweenFireTimes.Add(now - lastFireTime.Value);
            }

            lastFireTime = now;
        }

        public static List<TimeSpan> Durations
        {
            get { return durationBetweenFireTimes; }
        }
    }

    /// <summary>
    /// Custom RAMJobStore for producing context switches.
    /// </summary>
    public class SlowRAMJobStore : RAMJobStore
    {
        public override IList<IOperableTrigger> AcquireNextTriggers(DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow)
        {
            IList<IOperableTrigger> nextTriggers = base.AcquireNextTriggers(noLaterThan, maxCount, timeWindow);
            try
            {
                // Wait just a bit for hopefully having a context switch leading to the race condition
                Thread.Sleep(10);
            }
            catch (ThreadInterruptedException)
            {
            }
            return nextTriggers;
        }
    }
}