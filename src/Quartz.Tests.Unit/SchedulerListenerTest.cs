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

            public Task TriggerFiredAsync(ITrigger trigger, IJobExecutionContext context)
            {
                fireCount++;
                logger.Info("Trigger fired. count " + fireCount);
                return TaskUtil.CompletedTask;
            }

            public Task<bool> VetoJobExecutionAsync(ITrigger trigger, IJobExecutionContext context)
            {
                if (fireCount >= 3)
                {
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }

            public Task TriggerMisfiredAsync(ITrigger trigger)
            {
                return TaskUtil.CompletedTask;
            }

            public Task TriggerCompleteAsync(ITrigger trigger,
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

            public Task JobScheduledAsync(ITrigger trigger)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobUnscheduledAsync(TriggerKey triggerKey)
            {
                return TaskUtil.CompletedTask;
            }

            public Task TriggerFinalizedAsync(ITrigger trigger)
            {
                triggerFinalizedCount ++;
                logger.Info("triggerFinalized " + trigger);
                return TaskUtil.CompletedTask;
            }

            public Task TriggerPausedAsync(TriggerKey triggerKey)
            {
                return TaskUtil.CompletedTask;
            }

            public Task TriggersPausedAsync(string triggerGroup)
            {
                return TaskUtil.CompletedTask;
            }

            public Task TriggerResumedAsync(TriggerKey triggerKey)
            {
                return TaskUtil.CompletedTask;
            }

            public Task TriggersResumedAsync(string triggerGroup)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobAddedAsync(IJobDetail jobDetail)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobDeletedAsync(JobKey jobKey)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobPausedAsync(JobKey jobKey)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobsPausedAsync(string jobGroup)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobResumedAsync(JobKey jobKey)
            {
                return TaskUtil.CompletedTask;
            }

            public Task JobsResumedAsync(string jobGroup)
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulerErrorAsync(string msg, SchedulerException cause)
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulerInStandbyModeAsync()
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulerStartedAsync()
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulerStartingAsync()
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulerShutdownAsync()
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulerShuttingdownAsync()
            {
                return TaskUtil.CompletedTask;
            }

            public Task SchedulingDataClearedAsync()
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
            IScheduler scheduler = await new StdSchedulerFactory(props).GetScheduler();
            scheduler.ListenerManager.AddSchedulerListener(schedulerListener);
            scheduler.ListenerManager.AddTriggerListener(triggerListener);

            IJobDetail job = JobBuilder.Create<Qtz205Job>().WithIdentity("test").Build();
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("test")
                .WithSchedule(SimpleScheduleBuilder.RepeatSecondlyForTotalCount(3))
                .Build();

            await scheduler.ScheduleJobAsync(job, trigger);
            await scheduler.StartAsync();
            await Task.Delay(5000);

            await scheduler.ShutdownAsync(true);

            Assert.AreEqual(2, jobExecutionCount);
            Assert.AreEqual(3, triggerListener.FireCount);
            Assert.AreEqual(1, schedulerListener.TriggerFinalizedCount);
        }
    }
}