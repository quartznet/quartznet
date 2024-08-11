#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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

using System.Collections.Specialized;
using System.Data;

using FakeItEasy;

using Microsoft.Extensions.Logging;

using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Tests.Integration.Utils;
using Quartz.Util;
using Quartz.Xml;

namespace Quartz.Tests.Integration.Xml;

/// <summary>
/// Tests for <see cref="XMLSchedulingDataProcessor" />.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
[TestFixture(TestConstants.DefaultSqlServerProvider, Category = "db-sqlserver")]
[TestFixture(TestConstants.PostgresProvider, Category = "db-postgres")]
public class XMLSchedulingDataProcessorTest
{
    private readonly string provider;
    private XMLSchedulingDataProcessor processor;
    private IScheduler mockScheduler;
    private ILogger<XMLSchedulingDataProcessor> logger;

    public XMLSchedulingDataProcessorTest(string provider)
    {
        this.provider = provider;
    }

    [SetUp]
    public void SetUp()
    {
        logger = A.Fake<ILogger<XMLSchedulingDataProcessor>>();
        processor = new XMLSchedulingDataProcessor(logger, new SimpleTypeLoadHelper(), TimeProvider.System);
        mockScheduler = A.Fake<IScheduler>();
        A.CallTo(() => mockScheduler.GetJobDetail(A<JobKey>._, A<CancellationToken>._)).Returns(new ValueTask<IJobDetail>());
        A.CallTo(() => mockScheduler.GetTrigger(A<TriggerKey>._, A<CancellationToken>._)).Returns(new ValueTask<ITrigger>());

    }

    [Test]
    public async Task TestScheduling_MinimalConfiguration()
    {
        Stream s = ReadJobXmlFromEmbeddedResource("MinimalConfiguration_20.xml");
        await processor.ProcessStream(s, null);
        Assert.That(processor.OverWriteExistingData, Is.False);

        await processor.ScheduleJobs(mockScheduler);
    }

    [Test]
    public async Task TestScheduling_RichConfiguration()
    {
        Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration_20.xml");
        await processor.ProcessStream(s, null);
        Assert.That(processor.OverWriteExistingData, Is.False);
        Assert.That(processor.IgnoreDuplicates, Is.True);

        await processor.ScheduleJobs(mockScheduler);

        A.CallTo(() => mockScheduler.ScheduleJob(A<ITrigger>.That.Not.IsNull(), A<CancellationToken>._)).MustHaveHappened(7, Times.Exactly);
    }

    [Test]
    public async Task TestScheduling_QuartzNet250()
    {
        Stream s = ReadJobXmlFromEmbeddedResource("QRTZNET250.xml");
        await processor.ProcessStreamAndScheduleJobs(s, mockScheduler);
        A.CallTo(() => mockScheduler.AddJob(A<IJobDetail>.That.Not.IsNull(), A<bool>.Ignored, A<bool>.That.IsEqualTo(true), A<CancellationToken>._)).MustHaveHappened(2, Times.Exactly);
        A.CallTo(() => mockScheduler.ScheduleJob(A<ITrigger>.That.Not.IsNull(), A<CancellationToken>._)).MustHaveHappened(2, Times.Exactly);
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

        A.CallTo(() => mockScheduler.GetTrigger(existing.Key, A<CancellationToken>._)).Returns(new ValueTask<ITrigger>(existing));

        Stream s = ReadJobXmlFromEmbeddedResource("ScheduleRelativeToOldTrigger.xml");
        await processor.ProcessStream(s, null);
        await processor.ScheduleJobs(mockScheduler);

        // check that last fire time was taken from existing trigger
        A.CallTo(() => mockScheduler.RescheduleJob(null, null, A<CancellationToken>._)).WhenArgumentsMatch(args =>
        {
            ITrigger argumentTrigger = (ITrigger) args[1];

            // replacement trigger should have same start time and next fire relative to old trigger's last fire time
            Assert.That(argumentTrigger, Is.Not.Null);
            Assert.That(argumentTrigger.StartTimeUtc, Is.EqualTo(startTime));
            Assert.That(argumentTrigger.GetNextFireTimeUtc(), Is.EqualTo(previousFireTime.AddSeconds(10)));
            return true;
        }).MustHaveHappened();
    }

    [Test]
    public async Task TestComplexCronValidation()
    {
        var s = ReadJobXmlFromEmbeddedResource("ComplexCron.xml");
        await processor.ProcessStream(s, null);
        await processor.ScheduleJobs(mockScheduler);
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

            XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(logger, new SimpleTypeLoadHelper(), TimeProvider.System);
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
            Assert.That(jobKeys.Count, Is.EqualTo(1));
            var triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("DEFAULT"));
            Assert.That(triggerKeys.Count, Is.EqualTo(1));

            job = await scheduler.GetJobDetail(JobKey.Create("job1"));
            string fooValue = job.JobDataMap.GetString("foo");
            Assert.That(fooValue, Is.EqualTo("dont_chg_me"));
        }
        finally
        {
            if (File.Exists(tempFileName))
            {
                File.Delete(tempFileName);
            }

            // shutdown scheduler
            if (scheduler is not null)
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
            XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(logger, loadHelper, TimeProvider.System);
            await processor.ProcessFileAndScheduleJobs(tempFileName, scheduler);
            var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("DEFAULT"));
            Assert.That(jobKeys.Count, Is.EqualTo(2));
            var triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("DEFAULT"));
            Assert.That(triggerKeys.Count, Is.EqualTo(2));
        }
        finally
        {
            if (scheduler is not null)
            {
                await scheduler.Shutdown();
            }
        }
    }

    [Test]
    public async Task MultipleScheduleElementsShouldBeSupported()
    {
        Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration_20.xml");
        await processor.ProcessStream(s, null);

        await processor.ScheduleJobs(mockScheduler);

        A.CallTo(() => mockScheduler.ScheduleJob(A<IJobDetail>.That.Matches(p => p.Key.Name == "sched2_job"), A<ITrigger>.Ignored, A<CancellationToken>._));
        A.CallTo(() => mockScheduler.ScheduleJob(A<ITrigger>.That.Matches(p => p.Key.Name == "sched2_trig"), A<CancellationToken>._)).MustHaveHappened();
    }

    [Test]
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
            if (scheduler is not null)
            {
                await scheduler.Shutdown();
            }
        }
    }

    private static Stream ReadJobXmlFromEmbeddedResource(string resourceName)
    {
        string fullName = "Quartz.Tests.Integration.Xml.TestData." + resourceName;
        Stream stream = typeof(XMLSchedulingDataProcessorTest).Assembly.GetManifestResourceStream(fullName);
        Assert.That(stream, Is.Not.Null, "resource " + resourceName + " not found");
        return new StreamReader(stream).BaseStream;
    }

    [Test]
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

            await ModifyStoredJobType();

            XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(logger, new SimpleTypeLoadHelper(), TimeProvider.System);

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

    private async Task<IScheduler> CreateDbBackedScheduler()
    {
        var properties = DatabaseHelper.CreatePropertiesForProvider(provider);

        properties["quartz.scheduler.instanceName"] = "TestScheduler";
        properties["quartz.scheduler.instanceId"] = "AUTO";

        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler scheduler = await sf.GetScheduler();

        await scheduler.Clear();

        return scheduler;
    }

    [Test]
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

            await ModifyStoredJobType();

            XMLSchedulingDataProcessor processor = new(logger, new SimpleTypeLoadHelper(), TimeProvider.System);

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

    private static async Task ModifyStoredJobType()
    {
        using var conn = DBConnectionManager.Instance.GetConnection("default");
        await conn.OpenAsync();
        using (IDbCommand dbCommand = conn.CreateCommand())
        {
            dbCommand.CommandType = CommandType.Text;
            dbCommand.CommandText = "update qrtz_job_details set job_class_name='com.FakeNonExistsJob'";
            dbCommand.ExecuteNonQuery();
        }
        conn.Close();
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
            XMLSchedulingDataProcessor processor = new XMLSchedulingDataProcessor(logger, new SimpleTypeLoadHelper(), TimeProvider.System);
            await processor.ProcessStream(ReadJobXmlFromEmbeddedResource("directives_overwrite_no-ignoredups.xml"), "temp");
            var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("DEFAULT"));
            Assert.That(jobKeys.Count, Is.EqualTo(2));
            var triggerKeys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("DEFAULT"));
            Assert.That(triggerKeys.Count, Is.EqualTo(2));
        }
        finally
        {
            if (scheduler is not null)
            {
                await scheduler.Shutdown();
            }
        }
    }
}