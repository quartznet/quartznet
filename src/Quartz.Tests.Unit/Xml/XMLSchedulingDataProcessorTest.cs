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
using System.Transactions;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;
using Quartz.Xml;

using Rhino.Mocks;

using Is = NUnit.Framework.Is;

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
        private IScheduler mockScheduler;
        private TransactionScope scope;

        [SetUp]
        public void SetUp()
        {
            processor = new XMLSchedulingDataProcessor(new SimpleTypeLoadHelper());
            mockScheduler = MockRepository.GenerateMock<IScheduler>();
            scope = new TransactionScope();
        }

        [TearDown]
        public void TearDown()
        {
            if (scope != null)
            {
                scope.Dispose();
            }
        }

        [Test]
        public void TestScheduling_RichConfiguration()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration_20.xml");
            processor.ProcessStream(s, null);
            Assert.IsFalse(processor.OverWriteExistingData);
            Assert.IsTrue(processor.IgnoreDuplicates);

            processor.ScheduleJobs(mockScheduler);

            mockScheduler.AssertWasCalled(x => x.ScheduleJob(Arg<ITrigger>.Is.NotNull), options => options.Repeat.Times(5));
        }

        [Test]
        public void TestScheduling_MinimalConfiguration()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("MinimalConfiguration_20.xml");
            processor.ProcessStream(s, null);
            Assert.IsFalse(processor.OverWriteExistingData);

            processor.ScheduleJobs(mockScheduler);
        }

        [Test]
        public void TestScheduling_QuartzNet250()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("QRTZNET250.xml");
            processor.ProcessStreamAndScheduleJobs(s, mockScheduler);
            mockScheduler.AssertWasCalled(x => x.AddJob(Arg<IJobDetail>.Is.NotNull, Arg<bool>.Is.Anything, Arg<bool>.Is.Equal(true)), constraints => constraints.Repeat.Twice());
            mockScheduler.AssertWasCalled(x => x.ScheduleJob(Arg<ITrigger>.Is.NotNull), constraints => constraints.Repeat.Twice());
        }

        [Test]
        public void TestSchedulingWhenUpdatingScheduleBasedOnExistingTrigger()
        {
            DateTimeOffset startTime = new DateTimeOffset(2012, 12, 30, 1, 0, 0, TimeSpan.Zero);
            DateTimeOffset previousFireTime = new DateTimeOffset(2013, 2, 15, 15, 0, 0, TimeSpan.Zero);
            SimpleTriggerImpl existing = new SimpleTriggerImpl("triggerToReplace", "groupToReplace", startTime, null, SimpleTriggerImpl.RepeatIndefinitely, TimeSpan.FromHours(1));
            existing.JobKey = new JobKey("jobName1", "jobGroup1");
            existing.SetPreviousFireTimeUtc(previousFireTime);
            existing.GetNextFireTimeUtc();

            mockScheduler.Stub(x => x.GetTrigger(existing.Key)).Return(existing);

            Stream s = ReadJobXmlFromEmbeddedResource("ScheduleRelativeToOldTrigger.xml");
            processor.ProcessStream(s, null);
            processor.ScheduleJobs(mockScheduler);

            // check that last fire time was taken from existing trigger
            mockScheduler.Stub(x => x.RescheduleJob(null, null)).IgnoreArguments();
            var args = mockScheduler.GetArgumentsForCallsMadeOn(x => x.RescheduleJob(null, null));
            ITrigger argumentTrigger = (ITrigger) args[0][1];

            // replacement trigger should have same start time and next fire relative to old trigger's last fire time 
            Assert.That(argumentTrigger, Is.Not.Null);
            Assert.That(argumentTrigger.StartTimeUtc, Is.EqualTo(startTime));
            Assert.That(argumentTrigger.GetNextFireTimeUtc(), Is.EqualTo(previousFireTime.AddSeconds(10)));
        }

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
        public void TestOverwriteFlag()
        {
            // create temp file
            string tempFileName = XMLSchedulingDataProcessor.QuartzXmlFileName;
            using (TextWriter writer = new StreamWriter(tempFileName, false))
            {
                using (StreamReader reader = new StreamReader(ReadJobXmlFromEmbeddedResource("SimpleJobTrigger.xml")))
                {
                    writer.Write(reader.ReadToEnd());
                    writer.Flush();
                    writer.Close();
                }
            }

            IScheduler scheduler = null;
            try
            {
                StdSchedulerFactory factory = new StdSchedulerFactory();
                scheduler = StdSchedulerFactory.GetDefaultScheduler();

                // Let's setup a fixture job data that we know test is not going modify it.
                IJobDetail job = JobBuilder.Create<NoOpJob>()
                    .WithIdentity("job1").UsingJobData("foo", "dont_chg_me").Build();
                ITrigger trigger = TriggerBuilder.Create().WithIdentity("job1").WithSchedule(SimpleScheduleBuilder.RepeatHourlyForever()).Build();
                scheduler.ScheduleJob(job, trigger);

                XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(new SimpleTypeLoadHelper());
                try
                {
                    processor.ProcessFileAndScheduleJobs(scheduler, false);
                    Assert.Fail("OverWriteExisting flag didn't work. We should get Exception when overwrite is set to false.");
                }
                catch (ObjectAlreadyExistsException)
                {
                    // This is expected. Do nothing.
                }

                // We should still have what we start with.
                Assert.AreEqual(1, scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("DEFAULT")).Count);
                Assert.AreEqual(1, scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("DEFAULT")).Count);

                job = scheduler.GetJobDetail(JobKey.Create("job1"));
                String fooValue = job.JobDataMap.GetString("foo");
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
                    scheduler.Shutdown();
                }
            }
        }

        /** QTZ-187 */

        [Test]
        public void TesDirectivesNoOverwriteWithIgnoreDups()
        {
            // create temp file
            string tempFileName = XMLSchedulingDataProcessor.QuartzXmlFileName;
            using (TextWriter writer = new StreamWriter(tempFileName, false))
            {
                using (StreamReader reader = new StreamReader(ReadJobXmlFromEmbeddedResource("directives_overwrite_no-ignoredups.xml")))
                {
                    writer.Write(reader.ReadToEnd());
                    writer.Flush();
                    writer.Close();
                }
            }

            IScheduler scheduler = null;
            try
            {
                StdSchedulerFactory factory = new StdSchedulerFactory();
                scheduler = StdSchedulerFactory.GetDefaultScheduler();

                // Setup existing job with same names as in xml data.
                IJobDetail job = JobBuilder.Create<NoOpJob>()
                    .WithIdentity("job1")
                    .Build();

                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity("job1")
                    .WithSchedule(SimpleScheduleBuilder.RepeatHourlyForever()).Build();

                scheduler.ScheduleJob(job, trigger);

                job = JobBuilder.Create<NoOpJob>()
                    .WithIdentity("job2")
                    .Build();

                trigger = TriggerBuilder.Create().WithIdentity("job2").WithSchedule(SimpleScheduleBuilder.RepeatHourlyForever()).Build();
                scheduler.ScheduleJob(job, trigger);

                // Now load the xml data with directives: overwrite-existing-data=false, ignore-duplicates=true
                ITypeLoadHelper loadHelper = new SimpleTypeLoadHelper();
                loadHelper.Initialize();
                XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(loadHelper);
                processor.ProcessFileAndScheduleJobs(tempFileName, scheduler);
                Assert.AreEqual(2, scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("DEFAULT")).Count);
                Assert.AreEqual(2, scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("DEFAULT")).Count);
            }
            finally
            {
                if (scheduler != null)
                {
                    scheduler.Shutdown();
                }
            }
        }

        [Test]
        public void MultipleScheduleElementsShouldBeSupported()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration_20.xml");
            processor.ProcessStream(s, null);

            processor.ScheduleJobs(mockScheduler);

            mockScheduler.AssertWasCalled(x => x.ScheduleJob(Arg<IJobDetail>.Matches(p => p.Key.Name == "sched2_job"), Arg<ITrigger>.Is.Anything));
            mockScheduler.AssertWasCalled(x => x.ScheduleJob(Arg<ITrigger>.Matches(p => p.Key.Name == "sched2_trig")));
        }

        [Test]
        public void TestSimpleTriggerNoRepeat()
        {
            IScheduler scheduler = CreateDbBackedScheduler();
            try
            {
                processor.ProcessStreamAndScheduleJobs(ReadJobXmlFromEmbeddedResource("SimpleTriggerNoRepeat.xml"), scheduler);
                Assert.That(scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("DEFAULT")).Count, Is.EqualTo(1));
                Assert.That(scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("DEFAULT")).Count, Is.EqualTo(1));
            }
            finally
            {
                if (scheduler != null)
                {
                    scheduler.Shutdown();
                }
            }
        }

        private static Stream ReadJobXmlFromEmbeddedResource(string resourceName)
        {
            string fullName = "Quartz.Tests.Unit.Xml.TestData." + resourceName;
            Stream stream = typeof (XMLSchedulingDataProcessorTest).Assembly.GetManifestResourceStream(fullName);
            Assert.That(stream, Is.Not.Null, "resource " + resourceName + " not found");
            return new StreamReader(stream).BaseStream;
        }

        [Test]
        public void TestRemoveJobTypeNotFound()
        {
            var scheduler = CreateDbBackedScheduler();

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

                scheduler.ScheduleJob(jobDetail, trigger);

                IJobDetail jobDetail2 = scheduler.GetJobDetail(jobDetail.Key);
                ITrigger trigger2 = scheduler.GetTrigger(trigger.Key);
                Assert.That(jobDetail2.JobDataMap.GetString("foo"), Is.EqualTo("foo"));
                Assert.That(trigger2, Is.InstanceOf<ICronTrigger>());

                ModifyStoredJobType();

                XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(new SimpleTypeLoadHelper());

                // when
                processor.ProcessStreamAndScheduleJobs(ReadJobXmlFromEmbeddedResource("delete-no-job-class.xml"), scheduler);

                jobDetail2 = scheduler.GetJobDetail(jobDetail.Key);
                trigger2 = scheduler.GetTrigger(trigger.Key);
                Assert.That(jobDetail2.JobDataMap.GetString("foo"), Is.EqualTo("bar"));
                Assert.That(trigger2, Is.InstanceOf<ISimpleTrigger>());
            }
            finally
            {
                scheduler.Shutdown(false);
            }
        }

        private static IScheduler CreateDbBackedScheduler()
        {
            NameValueCollection properties = new NameValueCollection();

            properties["quartz.scheduler.instanceName"] = "TestScheduler";
            properties["quartz.scheduler.instanceId"] = "AUTO";
            properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
            properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
            properties["quartz.jobStore.dataSource"] = "default";
            properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
            properties["quartz.dataSource.default.connectionString"] = "Server=(local);Database=quartz;Trusted_Connection=True;";
            properties["quartz.dataSource.default.provider"] = "SqlServer-20";

            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler scheduler = sf.GetScheduler();

            scheduler.Clear();

            return scheduler;
        }

        [Test]
        public void TestOverwriteJobTypeNotFound()
        {
            IScheduler scheduler = CreateDbBackedScheduler();
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

                scheduler.ScheduleJob(jobDetail, trigger);

                IJobDetail jobDetail2 = scheduler.GetJobDetail(jobDetail.Key);
                ITrigger trigger2 = scheduler.GetTrigger(trigger.Key);
                Assert.That(jobDetail2.JobDataMap.GetString("foo"), Is.EqualTo("foo"));
                Assert.That(trigger2, Is.InstanceOf<ICronTrigger>());

                ModifyStoredJobType();

                XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(new SimpleTypeLoadHelper());

                processor.ProcessStreamAndScheduleJobs(ReadJobXmlFromEmbeddedResource("overwrite-no-jobclass.xml"), scheduler);

                jobDetail2 = scheduler.GetJobDetail(jobDetail.Key);
                trigger2 = scheduler.GetTrigger(trigger.Key);
                Assert.That(jobDetail2.JobDataMap.GetString("foo"), Is.EqualTo("bar"));
                Assert.That(trigger2, Is.InstanceOf<ISimpleTrigger>());
            }
            finally
            {
                scheduler.Shutdown(false);
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
        public void TestDirectivesOverwriteWithNoIgnoreDups()
        {
            IScheduler scheduler = null;
            try
            {
                StdSchedulerFactory factory = new StdSchedulerFactory();
                scheduler = factory.GetScheduler();

                // Setup existing job with same names as in xml data.
                string job1 = Guid.NewGuid().ToString();
                IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(job1).Build();
                ITrigger trigger = TriggerBuilder.Create().WithIdentity(job1).WithSchedule(SimpleScheduleBuilder.RepeatHourlyForever()).Build();
                scheduler.ScheduleJob(job, trigger);

                string job2 = Guid.NewGuid().ToString();
                job = JobBuilder.Create<NoOpJob>().WithIdentity(job2).Build();
                trigger = TriggerBuilder.Create().WithIdentity(job2).WithSchedule(SimpleScheduleBuilder.RepeatHourlyForever()).Build();
                scheduler.ScheduleJob(job, trigger);

                // Now load the xml data with directives: overwrite-existing-data=false, ignore-duplicates=true
                XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(new SimpleTypeLoadHelper());
                processor.ProcessStream(ReadJobXmlFromEmbeddedResource("directives_overwrite_no-ignoredups.xml"), "temp");
                Assert.That(scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("DEFAULT")).Count, Is.EqualTo(2));
                Assert.That(scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("DEFAULT")).Count, Is.EqualTo(2));
            }
            finally
            {
                if (scheduler != null)
                {
                    scheduler.Shutdown();
                }
            }
        }
    }
}