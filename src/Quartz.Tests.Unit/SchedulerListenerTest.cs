using System.Collections.Specialized;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Logging;
using Quartz.Util;

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
        private static readonly ILog logger = LogProvider.GetLogger(typeof(SchedulerListenerTest));
        private static int jobExecutionCount;

        public class Qtz205Job : IJob
        {
            public Task Execute(IJobExecutionContext context)
            {
                jobExecutionCount++;
                logger.Info("Job executed. jobExecutionCount=" + jobExecutionCount);
                return Task.FromResult(0);
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

            public Task TriggerFired(ITrigger trigger, IJobExecutionContext context)
            {
                fireCount++;
                logger.Info("Trigger fired. count " + fireCount);
                return TaskUtil.CompletedTask;
            }

            public Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context)
            {
                if (fireCount >= 3)
                {
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task TriggerMisfired(ITrigger trigger)
            {
                return TaskUtil.CompletedTask;
            }

            public Task TriggerComplete(ITrigger trigger,
                                        IJobExecutionContext context,
                                        SchedulerInstruction triggerInstructionCode)
            {
                return TaskUtil.CompletedTask;
            }
        }

        public class Qtz205ScheListener : ISchedulerListener
        {
            private int triggerFinalizedCount;

            public int TriggerFinalizedCount => triggerFinalizedCount;

            public Task JobScheduled(ITrigger trigger)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobUnscheduled(TriggerKey triggerKey)
            {
                return TaskUtil.CompletedTask;
            }

            public Task TriggerFinalized(ITrigger trigger)
            {
                triggerFinalizedCount ++;
                logger.Info("triggerFinalized " + trigger);
                return TaskUtil.CompletedTask;
            }

            public Task TriggerPaused(TriggerKey triggerKey)
            {
                return TaskUtil.CompletedTask;
            }

            public Task TriggersPaused(string triggerGroup)
            {
                return TaskUtil.CompletedTask;
            }

            public Task TriggerResumed(TriggerKey triggerKey)
            {
                return TaskUtil.CompletedTask;
            }

            public Task TriggersResumed(string triggerGroup)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobAdded(IJobDetail jobDetail)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobDeleted(JobKey jobKey)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobPaused(JobKey jobKey)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobsPaused(string jobGroup)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobResumed(JobKey jobKey)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobsResumed(string jobGroup)
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulerError(string msg, SchedulerException cause)
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulerInStandbyMode()
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulerStarted()
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulerStarting()
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulerShutdown()
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulerShuttingdown()
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulingDataCleared()
            {
                return TaskUtil.CompletedTask;
            }
        }

        [Test]
        public async Task TestTriggerFinalized()
        {
            Qtz205TriggerListener triggerListener = new Qtz205TriggerListener();
            Qtz205ScheListener schedulerListener = new Qtz205ScheListener();
            NameValueCollection props = new NameValueCollection();
            props["quartz.scheduler.idleWaitTime"] = "1500";
            props["quartz.threadPool.threadCount"] = "2";
            props["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
            IScheduler scheduler = await new StdSchedulerFactory(props).GetScheduler();
            scheduler.ListenerManager.AddSchedulerListener(schedulerListener);
            scheduler.ListenerManager.AddTriggerListener(triggerListener);

            IJobDetail job = JobBuilder.Create<Qtz205Job>().WithIdentity("test").Build();
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("test")
                .WithSchedule(SimpleScheduleBuilder.RepeatSecondlyForTotalCount(3))
                .Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();
            await Task.Delay(5000);

            await scheduler.Shutdown(true);

            Assert.AreEqual(2, jobExecutionCount);
            Assert.AreEqual(3, triggerListener.FireCount);
            Assert.AreEqual(1, schedulerListener.TriggerFinalizedCount);
        }
    }
}