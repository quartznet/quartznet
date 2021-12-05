using NUnit.Framework;
using Quartz.Impl;
using Quartz.Listener;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Integration test for inheritance DisallowConcurrentExecutionAttribute
    /// and PersistJobDataAfterExecutionAttribute from interfaces
    /// </summary>
    /// <seealso cref="DisallowConcurrentExecutionAttribute"/>
    /// <seealso cref="PersistJobDataAfterExecutionAttribute"/>
    /// <author>Oleg Kurbatov</author>
    /// <author>Aleksei Kuznetsov</author>
    [TestFixture]
    public class JobExecutionAttributesInterfaceInheritanceTest
    {
        private static readonly TimeSpan jobBlockTime = TimeSpan.FromMilliseconds(300);
        private static readonly List<DateTime> jobExecDates = new List<DateTime>();
        private static readonly AutoResetEvent barrier = new AutoResetEvent(false);
        
        [PersistJobDataAfterExecution]
        [DisallowConcurrentExecution]
        public interface ITestJob : IJob
        {
        }
        
        public class TestJob : ITestJob
        {
            public async Task Execute(IJobExecutionContext context)
            {
                jobExecDates.Add(DateTime.UtcNow);

                await Task.Delay(jobBlockTime);
            }
        }

        public class TestJobListener : JobListenerSupport
        {
            private int jobExCount;
            private readonly int jobExecutionCountToSyncAfter;

            public TestJobListener(int jobExecutionCountToSyncAfter)
            {
                this.jobExecutionCountToSyncAfter = jobExecutionCountToSyncAfter;
            }

            public override string Name => "TestJobListener";

            public override Task JobWasExecuted(
                IJobExecutionContext context,
                JobExecutionException jobException,
                CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref jobExCount) == jobExecutionCountToSyncAfter)
                {
                    try
                    {
                        barrier.Set();
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine(e.ToString());
                        throw new AssertionException("Await on barrier was interrupted: " + e);
                    }
                }
                return Task.FromResult(true);
            }
        }

        [SetUp]
        public void SetUp()
        {
            jobExecDates.Clear();
        }

        [Test]
        public void TestWhetherAttributesAreInheritedFromInterfaces()
        {
            IJobDetail job = JobBuilder.Create<TestJob>().Build();
            Assert.IsTrue(job.PersistJobDataAfterExecution);
            Assert.IsTrue(job.ConcurrentExecutionDisallowed);
        }
        
        [Test]
        public async Task TestNoConcurrentExecOnSameJob()
        {
            var startTime = DateTime.Now.AddMilliseconds(100).ToUniversalTime(); // make the triggers fire at the same time.

            IJobDetail job = JobBuilder.Create<TestJob>().Build();

            ITrigger trigger1 = TriggerBuilder.Create().WithSimpleSchedule().StartAt(startTime).Build();
            ITrigger trigger2 = TriggerBuilder.Create().WithSimpleSchedule().StartAt(startTime).ForJob(job).Build();

            var props = new NameValueCollection
            {
                ["quartz.scheduler.idleWaitTime"] = "1500",
                ["quartz.threadPool.threadCount"] = "2",
                ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
            };
            IScheduler scheduler = await new StdSchedulerFactory(props).GetScheduler();
            scheduler.ListenerManager.AddJobListener(new TestJobListener(2));
            await scheduler.ScheduleJob(job, trigger1);
            await scheduler.ScheduleJob(trigger2);

            await scheduler.Start();
            barrier.WaitOne();
            await scheduler.Shutdown(true);

            Assert.AreEqual(2, jobExecDates.Count);
            Assert.That((jobExecDates[1] - jobExecDates[0]).TotalMilliseconds, Is.GreaterThanOrEqualTo(jobBlockTime.TotalMilliseconds).Within(5d));
        }

        /** QTZ-202 */

        [Test]
        public async Task TestNoConcurrentExecOnSameJobWithBatching()
        {
            var startTime = DateTimeOffset.UtcNow.AddMilliseconds(300); // make the triggers fire at the same time.

            var job = JobBuilder.Create<TestJob>().Build();

            var trigger1 = TriggerBuilder.Create().WithSimpleSchedule().StartAt(startTime).Build();
            var trigger2 = TriggerBuilder.Create().WithSimpleSchedule().StartAt(startTime).ForJob(job).Build();

            var props = new NameValueCollection
            {
                ["quartz.scheduler.idleWaitTime"] = "1500",
                ["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "2",
                ["quartz.threadPool.threadCount"] = "2",
                ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
            };

            var scheduler = await new StdSchedulerFactory(props).GetScheduler();
            scheduler.ListenerManager.AddJobListener(new TestJobListener(2));
            await scheduler.ScheduleJob(job, trigger1);
            await scheduler.ScheduleJob(trigger2);

            await scheduler.Start();
            barrier.WaitOne();
            await scheduler.Shutdown(true);
            
            Assert.AreEqual(2, jobExecDates.Count);
            Assert.That((jobExecDates[1] - jobExecDates[0]).TotalMilliseconds, Is.GreaterThanOrEqualTo(jobBlockTime.TotalMilliseconds).Within(5));
        }
    }
}
