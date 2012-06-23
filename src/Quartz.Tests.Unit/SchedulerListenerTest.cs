using System.Collections.Specialized;
using System.Threading;

using Common.Logging;

using NUnit.Framework;

using Quartz.Impl;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Tests for <see cref="ISchedulerListener"/>.
    /// </summary>
    /// <author>Zemian Deng</author>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class SchedulerListenerTest
    {
        private static readonly ILog logger = LogManager.GetLogger<SchedulerListenerTest>();
        private static int jobExecutionCount;

        public class Qtz205Job : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
                jobExecutionCount++;
                logger.Info("Job executed. jobExecutionCount=" + jobExecutionCount);
            }
        }

        public class Qtz205TriggerListener : ITriggerListener
        {
            private int fireCount;

            public int FireCount
            {
                get { return fireCount; }
            }

            public string Name
            {
                get { return "Qtz205TriggerListener"; }
            }

            public void TriggerFired(ITrigger trigger, IJobExecutionContext context)
            {
                fireCount++;
                logger.Info("Trigger fired. count " + fireCount);
            }

            public bool VetoJobExecution(ITrigger trigger, IJobExecutionContext context)
            {
                if (fireCount >= 3)
                {
                    return true;
                }
                return false;
            }

            public void TriggerMisfired(ITrigger trigger)
            {
            }

            public void TriggerComplete(ITrigger trigger,
                                        IJobExecutionContext context,
                                        SchedulerInstruction triggerInstructionCode)
            {
            }
        }

        public class Qtz205ScheListener : ISchedulerListener
        {
            private int triggerFinalizedCount;

            public int TriggerFinalizedCount
            {
                get { return triggerFinalizedCount; }
            }

            public void JobScheduled(ITrigger trigger)
            {
            }

            public void JobUnscheduled(TriggerKey triggerKey)
            {
            }

            public void TriggerFinalized(ITrigger trigger)
            {
                triggerFinalizedCount ++;
                logger.Info("triggerFinalized " + trigger);
            }

            public void TriggerPaused(TriggerKey triggerKey)
            {
            }

            public void TriggersPaused(string triggerGroup)
            {
            }

            public void TriggerResumed(TriggerKey triggerKey)
            {
            }

            public void TriggersResumed(string triggerGroup)
            {
            }

            public void JobAdded(IJobDetail jobDetail)
            {
            }

            public void JobDeleted(JobKey jobKey)
            {
            }

            public void JobPaused(JobKey jobKey)
            {
            }

            public void JobsPaused(string jobGroup)
            {
            }

            public void JobResumed(JobKey jobKey)
            {
            }

            public void JobsResumed(string jobGroup)
            {
            }

            public void SchedulerError(string msg, SchedulerException cause)
            {
            }

            public void SchedulerInStandbyMode()
            {
            }

            public void SchedulerStarted()
            {
            }

            public void SchedulerStarting()
            {
            }

            public void SchedulerShutdown()
            {
            }

            public void SchedulerShuttingdown()
            {
            }

            public void SchedulingDataCleared()
            {
            }
        }

        [Test]
        public void TestTriggerFinalized()
        {
            Qtz205TriggerListener triggerListener = new Qtz205TriggerListener();
            Qtz205ScheListener schedulerListener = new Qtz205ScheListener();
            NameValueCollection props = new NameValueCollection();
            props["quartz.scheduler.idleWaitTime"] = "1500";
            props["quartz.threadPool.threadCount"] = "2";
            IScheduler scheduler = new StdSchedulerFactory(props).GetScheduler();
            scheduler.ListenerManager.AddSchedulerListener(schedulerListener);
            scheduler.ListenerManager.AddTriggerListener(triggerListener);

            IJobDetail job = JobBuilder.Create<Qtz205Job>().WithIdentity("test").Build();
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("test")
                .WithSchedule(SimpleScheduleBuilder.RepeatSecondlyForTotalCount(3))
                .Build();

            scheduler.ScheduleJob(job, trigger);
            scheduler.Start();
            Thread.Sleep(5000);

            scheduler.Shutdown(true);

            Assert.AreEqual(2, jobExecutionCount);
            Assert.AreEqual(3, triggerListener.FireCount);
            Assert.AreEqual(1, schedulerListener.TriggerFinalizedCount);
        }
    }
}