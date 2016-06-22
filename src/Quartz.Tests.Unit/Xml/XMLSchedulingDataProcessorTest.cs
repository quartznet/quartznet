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
using System.Collections.Specialized;
using System.Data;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
#if TRANSACTIONS
using System.Transactions;
#endif
#if FAKE_IT_EASY
using FakeItEasy;
#endif
using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Matchers;
#if FAKE_IT_EASY
using Quartz.Impl.Triggers;
#endif
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;
using Quartz.Xml;

namespace Quartz.Tests.Unit.Xml
{
    /// <summary>
    /// Tests for <see cref="XMLSchedulingDataProcessor" />.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class XMLSchedulingDataProcessorTest
    {
        private XMLSchedulingDataProcessor processor;
#if FAKE_IT_EASY
        private IScheduler mockScheduler;
#endif
#if TRANSACTIONS
        private TransactionScope scope;
#endif

        [SetUp]
        public void SetUp()
        {
            processor = new XMLSchedulingDataProcessor(new SimpleTypeLoadHelper());
#if FAKE_IT_EASY
            mockScheduler = A.Fake<IScheduler>();
            A.CallTo(() => mockScheduler.GetJobDetail(A<JobKey>._)).Returns(Task.FromResult<IJobDetail>(null));
            A.CallTo(() => mockScheduler.GetTrigger(A<TriggerKey>._)).Returns(Task.FromResult<ITrigger>(null));
#endif
#if TRANSACTIONS
            scope = new TransactionScope();
#endif
        }

        [TearDown]
        public void TearDown()
        {
#if TRANSACTIONS
            scope?.Dispose();
#endif
        }

#if FAKE_IT_EASY
        [Test]
        [Category("database")]
        public async Task TestScheduling_MinimalConfiguration()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("MinimalConfiguration_20.xml");
            await processor.ProcessStream(s, null);
            Assert.IsFalse(processor.OverWriteExistingData);

            await processor.ScheduleJobs(mockScheduler);
        }


        [Test]
        [Category("database")]
        public async Task TestScheduling_RichConfiguration()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration_20.xml");
            await processor.ProcessStream(s, null);
            Assert.IsFalse(processor.OverWriteExistingData);
            Assert.IsTrue(processor.IgnoreDuplicates);

            await processor.ScheduleJobs(mockScheduler);

            A.CallTo(() => mockScheduler.ScheduleJob(A<ITrigger>.That.Not.IsNull())).MustHaveHappened(Repeated.Exactly.Times(5));
        }

        [Test]
        [Category("database")]
        public async Task TestScheduling_QuartzNet250()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("QRTZNET250.xml");
            await processor.ProcessStreamAndScheduleJobs(s, mockScheduler);
            A.CallTo(() => mockScheduler.AddJob(A<IJobDetail>.That.Not.IsNull(), A<bool>.Ignored, A<bool>.That.IsEqualTo(true))).MustHaveHappened(Repeated.Exactly.Twice);
            A.CallTo(() => mockScheduler.ScheduleJob(A<ITrigger>.That.Not.IsNull())).MustHaveHappened(Repeated.Exactly.Twice);
            ;
        }

        [Test]
        public async Task TestSchedulingWhenUpdatingScheduleBasedOnExistingTrigger()
        {
            DateTimeOffset startTime = new DateTimeOffset(2012, 12, 30, 1, 0, 0, TimeSpan.Zero);
            DateTimeOffset previousFireTime = new DateTimeOffset(2013, 2, 15, 15, 0, 0, TimeSpan.Zero);
            SimpleTriggerImpl existing = new SimpleTriggerImpl("triggerToReplace", "groupToReplace", startTime, null, SimpleTriggerImpl.RepeatIndefinitely, TimeSpan.FromHours(1));
            existing.JobKey = new JobKey("jobName1", "jobGroup1");
            existing.SetPreviousFireTimeUtc(previousFireTime);
            existing.GetNextFireTimeUtc();

            A.CallTo(() => mockScheduler.GetTrigger(existing.Key)).Returns(existing);

            Stream s = ReadJobXmlFromEmbeddedResource("ScheduleRelativeToOldTrigger.xml");
            await processor.ProcessStream(s, null);
            await processor.ScheduleJobs(mockScheduler);

            // check that last fire time was taken from existing trigger
            A.CallTo(() => mockScheduler.RescheduleJob(null, null)).WhenArgumentsMatch(args =>
            {
                ITrigger argumentTrigger = (ITrigger) args[1];

                // replacement trigger should have same start time and next fire relative to old trigger's last fire time 
                Assert.That(argumentTrigger, Is.Not.Null);
                Assert.That(argumentTrigger.StartTimeUtc, Is.EqualTo(startTime));
                Assert.That(argumentTrigger.GetNextFireTimeUtc(), Is.EqualTo(previousFireTime.AddSeconds(10)));
                return true;
            }).MustHaveHappened();
        }
#endif

        /// <summary>
        /// The default XMLSchedulingDataProcessor will setOverWriteExistingData(true), and we want to
        /// test programmatically overriding this value.
        /// </summary>
        /// <remarks>
        /// Note that XMLSchedulingDataProcessor#processFileAndScheduleJobs(Scheduler,boolean) will only
        /// read default "quartz_data.xml" in current working directory. So to test this, we must create
        /// this file. If this file already exist, it will be overwritten! 
        /// </remarks>
        [Test]
        public async Task TestOverwriteFlag()
        {
            // create temp file
            string tempFileName = XMLSchedulingDataProcessor.QuartzXmlFileName;
            // Use File.Create (as opposed to File.OpenWrite) so that if the file already exists, it will be completely
            // replaced instead of only overwriting the first N bytes (where N is the length of SimpleJobTrigger.xml)
            using (TextWriter writer = new StreamWriter(File.Create(tempFileName)))
            {
                using (StreamReader reader = new StreamReader(ReadJobXmlFromEmbeddedResource("SimpleJobTrigger.xml")))
                {
                    await writer.WriteAsync(await reader.ReadToEndAsync());
                    await writer.FlushAsync();
                }
            }

            IScheduler scheduler = null;
            try
            {
                var properties = new NameValueCollection();
                properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
                StdSchedulerFactory factory = new StdSchedulerFactory(properties);
                scheduler = await factory.GetScheduler();

                // Let's setup a fixture job data that we know test is not going modify it.
                IJobDetail job = JobBuilder.Create<NoOpJob>()
                    .WithIdentity("job1").UsingJobData("foo", "dont_chg_me").Build();
                ITrigger trigger = TriggerBuilder.Create().WithIdentity("job1").WithSchedule(SimpleScheduleBuilder.RepeatHourlyForever()).Build();
                await scheduler.ScheduleJob(job, trigger);

                XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(new SimpleTypeLoadHelper());
                try
                {
                    await processor.ProcessFileAndScheduleJobs(scheduler, false);
                    Assert.Fail("OverWriteExisting flag didn't work. We should get Exception when overwrite is set to false.");
                }
                catch (ObjectAlreadyExistsException)
                {
                    // This is expected. Do nothing.
                }

                // We should still have what we start with.
                var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("DEFAULT"));
                Assert.AreEqual(1, jobKeys.Count);
                var triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("DEFAULT"));
                Assert.AreEqual(1, triggerKeys.Count);

                job = await scheduler.GetJobDetail(JobKey.Create("job1"));
                string fooValue = job.JobDataMap.GetString("foo");
                Assert.AreEqual("dont_chg_me", fooValue);
            }
            finally
            {
                if (File.Exists(tempFileName))
                {
                    File.Delete(tempFileName);
                }

                // shutdown scheduler
                if (scheduler != null)
                {
                    await scheduler.Shutdown();
                }
            }
        }

        /** QTZ-187 */

        [Test]
        public async Task TesDirectivesNoOverwriteWithIgnoreDups()
        {
            // create temp file
            string tempFileName = XMLSchedulingDataProcessor.QuartzXmlFileName;
            using (TextWriter writer = new StreamWriter(File.OpenWrite(tempFileName)))
            {
                using (StreamReader reader = new StreamReader(ReadJobXmlFromEmbeddedResource("directives_overwrite_no-ignoredups.xml")))
                {
                    await writer.WriteAsync(await reader.ReadToEndAsync());
                    await writer.FlushAsync();
                }
            }

            IScheduler scheduler = null;
            try
            {
                NameValueCollection properties = new NameValueCollection();
                properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;

                StdSchedulerFactory factory = new StdSchedulerFactory(properties);
                scheduler = await factory.GetScheduler();

                // Setup existing job with same names as in xml data.
                IJobDetail job = JobBuilder.Create<NoOpJob>()
                    .WithIdentity("job1")
                    .Build();

                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity("job1")
                    .WithSchedule(SimpleScheduleBuilder.RepeatHourlyForever()).Build();

                await scheduler.ScheduleJob(job, trigger);

                job = JobBuilder.Create<NoOpJob>()
                    .WithIdentity("job2")
                    .Build();

                trigger = TriggerBuilder.Create().WithIdentity("job2").WithSchedule(SimpleScheduleBuilder.RepeatHourlyForever()).Build();
                await scheduler.ScheduleJob(job, trigger);

                // Now load the xml data with directives: overwrite-existing-data=false, ignore-duplicates=true
                ITypeLoadHelper loadHelper = new SimpleTypeLoadHelper();
                loadHelper.Initialize();
                XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(loadHelper);
                await processor.ProcessFileAndScheduleJobs(tempFileName, scheduler);
                var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("DEFAULT"));
                Assert.AreEqual(2, jobKeys.Count);
                var triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("DEFAULT"));
                Assert.AreEqual(2, triggerKeys.Count);
            }
            finally
            {
                if (scheduler != null)
                {
                    await scheduler.Shutdown();
                }
            }
        }

#if FAKE_IT_EASY
        [Test]
        public async Task MultipleScheduleElementsShouldBeSupported()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration_20.xml");
            await processor.ProcessStream(s, null);

            await processor.ScheduleJobs(mockScheduler);

            A.CallTo(() => mockScheduler.ScheduleJob(A<IJobDetail>.That.Matches(p => p.Key.Name == "sched2_job"), A<ITrigger>.Ignored));
            A.CallTo(() => mockScheduler.ScheduleJob(A<ITrigger>.That.Matches(p => p.Key.Name == "sched2_trig"))).MustHaveHappened();
        }
#endif

        [Test]
        [Category("database")]
        public async Task TestSimpleTriggerNoRepeat()
        {
            IScheduler scheduler = await CreateDbBackedScheduler();
            try
            {
                await processor.ProcessStreamAndScheduleJobs(ReadJobXmlFromEmbeddedResource("SimpleTriggerNoRepeat.xml"), scheduler);
                var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("DEFAULT"));
                Assert.That(jobKeys.Count, Is.EqualTo(1));
                var triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("DEFAULT"));
                Assert.That(triggerKeys.Count, Is.EqualTo(1));
            }
            finally
            {
                if (scheduler != null)
                {
                    await scheduler.Shutdown();
                }
            }
        }

        private static Stream ReadJobXmlFromEmbeddedResource(string resourceName)
        {
            string fullName = "Quartz.Tests.Unit.Xml.TestData." + resourceName;
            Stream stream = typeof(XMLSchedulingDataProcessorTest).GetTypeInfo().Assembly.GetManifestResourceStream(fullName);
            Assert.That(stream, Is.Not.Null, "resource " + resourceName + " not found");
            return new StreamReader(stream).BaseStream;
        }

        [Test]
        [Category("database")]
        public async Task TestRemoveJobTypeNotFound()
        {
            var scheduler = await CreateDbBackedScheduler();

            try
            {
                string jobName = "job1";
                IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
                    .WithIdentity(jobName, "DEFAULT")
                    .UsingJobData("foo", "foo")
                    .Build();
                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity(jobName, "DEFAULT")
                    .WithSchedule(CronScheduleBuilder.CronSchedule("* * * * * ?"))
                    .Build();

                await scheduler.ScheduleJob(jobDetail, trigger);

                IJobDetail jobDetail2 = await scheduler.GetJobDetail(jobDetail.Key);
                ITrigger trigger2 = await scheduler.GetTrigger(trigger.Key);
                Assert.That(jobDetail2.JobDataMap.GetString("foo"), Is.EqualTo("foo"));
                Assert.That(trigger2, Is.InstanceOf<ICronTrigger>());

                ModifyStoredJobType();

                XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(new SimpleTypeLoadHelper());

                // when
                await processor.ProcessStreamAndScheduleJobs(ReadJobXmlFromEmbeddedResource("delete-no-job-class.xml"), scheduler);

                jobDetail2 = await scheduler.GetJobDetail(jobDetail.Key);
                trigger2 = await scheduler.GetTrigger(trigger.Key);

                Assert.That(jobDetail2.JobDataMap.GetString("foo"), Is.EqualTo("bar"));
                Assert.That(trigger2, Is.InstanceOf<ISimpleTrigger>());
            }
            finally
            {
                await scheduler.Shutdown(false);
            }
        }

        private static async Task<IScheduler> CreateDbBackedScheduler()
        {
            NameValueCollection properties = new NameValueCollection();

            properties["quartz.scheduler.instanceName"] = "TestScheduler";
            properties["quartz.scheduler.instanceId"] = "AUTO";
            properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
            properties["quartz.jobStore.dataSource"] = "default";
            properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
            properties["quartz.dataSource.default.connectionString"] = "Server=(local);Database=quartz;User Id=quartznet;Password=quartznet;";
            properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;
            properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;

            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler scheduler = await sf.GetScheduler();

            await scheduler.Clear();

            return scheduler;
        }

        [Test]
        [Category("database")]
        public async Task TestOverwriteJobTypeNotFound()
        {
            IScheduler scheduler = await CreateDbBackedScheduler();
            try
            {
                string jobName = "job1";
                IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
                    .WithIdentity(jobName, "DEFAULT")
                    .UsingJobData("foo", "foo")
                    .Build();
                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity(jobName, "DEFAULT")
                    .WithSchedule(CronScheduleBuilder.CronSchedule("* * * * * ?"))
                    .Build();

                await scheduler.ScheduleJob(jobDetail, trigger);

                IJobDetail jobDetail2 = await scheduler.GetJobDetail(jobDetail.Key);
                ITrigger trigger2 = await scheduler.GetTrigger(trigger.Key);
                Assert.That(jobDetail2.JobDataMap.GetString("foo"), Is.EqualTo("foo"));
                Assert.That(trigger2, Is.InstanceOf<ICronTrigger>());

                ModifyStoredJobType();

                XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(new SimpleTypeLoadHelper());

                await processor.ProcessStreamAndScheduleJobs(ReadJobXmlFromEmbeddedResource("overwrite-no-jobclass.xml"), scheduler);

                jobDetail2 = await scheduler.GetJobDetail(jobDetail.Key);
                trigger2 = await scheduler.GetTrigger(trigger.Key);
                Assert.That(jobDetail2.JobDataMap.GetString("foo"), Is.EqualTo("bar"));
                Assert.That(trigger2, Is.InstanceOf<ISimpleTrigger>());
            }
            finally
            {
                await scheduler.Shutdown(false);
            }
        }

        private void ModifyStoredJobType()
        {
            using (var conn = DBConnectionManager.Instance.GetConnection("default"))
            {
                conn.Open();
                using (IDbCommand dbCommand = conn.CreateCommand())
                {
                    dbCommand.CommandType = CommandType.Text;
                    dbCommand.CommandText = "update qrtz_job_details set job_class_name='com.FakeNonExistsJob'";
                    dbCommand.ExecuteNonQuery();
                }
                conn.Close();
            }
        }

        [Test]
        public async Task TestDirectivesOverwriteWithNoIgnoreDups()
        {
            IScheduler scheduler = null;
            try
            {
                NameValueCollection properties = new NameValueCollection();
                properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
                StdSchedulerFactory factory = new StdSchedulerFactory(properties);
                scheduler = await factory.GetScheduler();

                // Setup existing job with same names as in xml data.
                string job1 = Guid.NewGuid().ToString();
                IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(job1).Build();
                ITrigger trigger = TriggerBuilder.Create().WithIdentity(job1).WithSchedule(SimpleScheduleBuilder.RepeatHourlyForever()).Build();
                await scheduler.ScheduleJob(job, trigger);

                string job2 = Guid.NewGuid().ToString();
                job = JobBuilder.Create<NoOpJob>().WithIdentity(job2).Build();
                trigger = TriggerBuilder.Create().WithIdentity(job2).WithSchedule(SimpleScheduleBuilder.RepeatHourlyForever()).Build();
                await scheduler.ScheduleJob(job, trigger);

                // Now load the xml data with directives: overwrite-existing-data=false, ignore-duplicates=true
                XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(new SimpleTypeLoadHelper());
                await processor.ProcessStream(ReadJobXmlFromEmbeddedResource("directives_overwrite_no-ignoredups.xml"), "temp");
                var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("DEFAULT"));
                Assert.That(jobKeys.Count, Is.EqualTo(2));
                var triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("DEFAULT"));
                Assert.That(triggerKeys.Count, Is.EqualTo(2));
            }
            finally
            {
                if (scheduler != null)
                {
                    await scheduler.Shutdown();
                }
            }
        }
    }
}