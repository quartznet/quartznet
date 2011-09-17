using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;

using NUnit.Framework;

using Quartz.Impl;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Integration test for using DisallowConcurrentExecution attribute.
    /// </summary>
    /// <author>Zemian Deng</author>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class DisallowConcurrentExecutionJobTest
    {
        private static readonly TimeSpan jobBlockTime = TimeSpan.FromMilliseconds(300);
        private static readonly List<DateTime> jobExecDates = new List<DateTime>();

        [DisallowConcurrentExecution]
        public class TestJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
                jobExecDates.Add(DateTime.UtcNow);
                try
                {
                    Thread.Sleep(jobBlockTime);
                }
                catch (ThreadInterruptedException)
                {
                    throw new JobExecutionException("Failed to pause job for testing.");
                }
            }
        }

        [SetUp]
        public void SetUp()
        {
            jobExecDates.Clear();
        }

        [Test]
        public void TestNoConcurrentExecOnSameJob()
        {
            DateTime startTime = DateTime.Now.AddMilliseconds(100).ToUniversalTime(); // make the triggers fire at the same time.

            IJobDetail job1 = JobBuilder.Create<TestJob>().WithIdentity("job1").Build();
            ITrigger trigger1 = TriggerBuilder.Create().WithSimpleSchedule().StartAt(startTime).Build();

            ITrigger trigger2 = TriggerBuilder.Create().WithSimpleSchedule().StartAt(startTime).ForJob(job1).Build();

            NameValueCollection props = new NameValueCollection();
            props["quartz.scheduler.idleWaitTime"] = "1500";
            props["quartz.threadPool.threadCount"] = "2";
            IScheduler scheduler = new StdSchedulerFactory(props).GetScheduler();
            scheduler.ScheduleJob(job1, trigger1);
            scheduler.ScheduleJob(trigger2);

            scheduler.Start();
            Thread.Sleep(5000);
            scheduler.Shutdown(true);

            Assert.AreEqual(2, jobExecDates.Count);
            Assert.Greater(jobExecDates[1] - jobExecDates[0], jobBlockTime);
        }

        /** QTZ-202 */

        [Test]
        public void TestNoConcurrentExecOnSameJobWithBatching()
        {
            DateTime startTime = DateTime.Now.AddMilliseconds(300).ToUniversalTime(); // make the triggers fire at the same time.

            IJobDetail job1 = JobBuilder.Create<TestJob>().WithIdentity("job1").Build();
            ITrigger trigger1 = TriggerBuilder.Create().WithSimpleSchedule().StartAt(startTime).Build();

            ITrigger trigger2 = TriggerBuilder.Create().WithSimpleSchedule().StartAt(startTime).ForJob(job1).Build();

            NameValueCollection props = new NameValueCollection();
            props["quartz.scheduler.idleWaitTime"] = "1500";
            props["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "2";
            props["quartz.threadPool.threadCount"] = "2";
            IScheduler scheduler = new StdSchedulerFactory(props).GetScheduler();
            scheduler.ScheduleJob(job1, trigger1);
            scheduler.ScheduleJob(trigger2);

            scheduler.Start();
            Thread.Sleep(5000);
            scheduler.Shutdown(true);

            Assert.AreEqual(2, jobExecDates.Count);
            Assert.Greater(jobExecDates[1] - jobExecDates[0], jobBlockTime);
        }
    }
}