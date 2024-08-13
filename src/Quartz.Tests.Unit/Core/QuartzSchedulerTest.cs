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
using System.Globalization;

using FakeItEasy;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Core;

/// <author>Marko Lahma (.NET)</author>
[TestFixture]
public class QuartzSchedulerTest
{
    [Test]
    public void TestVersionInfo()
    {
        var versionInfo = typeof(QuartzScheduler).Assembly.GetName().Version;
        Assert.Multiple(() =>
        {
            Assert.That(QuartzScheduler.VersionMajor, Is.EqualTo(versionInfo.Major.ToString(CultureInfo.InvariantCulture)));
            Assert.That(QuartzScheduler.VersionMinor, Is.EqualTo(versionInfo.Minor.ToString(CultureInfo.InvariantCulture)));
            Assert.That(QuartzScheduler.VersionIteration, Is.EqualTo(versionInfo.Build.ToString(CultureInfo.InvariantCulture)));
        });
    }

    [Test]
    public async Task TestInvalidCalendarScheduling()
    {
        const string ExpectedError = "Calendar not found: FOOBAR";

        NameValueCollection properties = new NameValueCollection();
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler sched = await sf.GetScheduler();

        DateTime runTime = DateTime.Now.AddMinutes(10);

        // define the job and tie it to our HelloJob class
        var job = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey("job1", "group1"))
            .Build();

        // Trigger the job to run on the next round minute
        IOperableTrigger trigger = new SimpleTriggerImpl("trigger1", "group1", runTime);

        // set invalid calendar
        trigger.CalendarName = "FOOBAR";

        try
        {
            await sched.ScheduleJob(job, trigger);
            Assert.Fail("No error for non-existing calendar");
        }
        catch (SchedulerException ex)
        {
            Assert.That(ex.Message, Is.EqualTo(ExpectedError));
        }

        try
        {
            await sched.ScheduleJob(trigger);
            Assert.Fail("No error for non-existing calendar");
        }
        catch (SchedulerException ex)
        {
            Assert.That(ex.Message, Is.EqualTo(ExpectedError));
        }

        await sched.Shutdown(false);
    }

    [Test]
    public async Task TestStartDelayed()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        var sf = new StdSchedulerFactory(properties);

        IScheduler sched = await sf.GetScheduler();
        await sched.StartDelayed(TimeSpan.FromMilliseconds(100));
        Assert.That(sched.IsStarted, Is.False);
        await Task.Delay(2000);
        Assert.That(sched.IsStarted, Is.True);
    }

    [Test]
    public async Task TestRescheduleJob_SchedulerListenersCalledOnReschedule()
    {
        const string TriggerName = "triggerName";
        const string TriggerGroup = "triggerGroup";
        const string JobName = "jobName";
        const string JobGroup = "jobGroup";

        NameValueCollection properties = new NameValueCollection();
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler scheduler = await sf.GetScheduler();
        DateTime startTimeUtc = DateTime.UtcNow.AddSeconds(2);
        var jobDetail = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey(JobName, JobGroup))
            .Build();
        SimpleTriggerImpl jobTrigger = new SimpleTriggerImpl(TriggerName, TriggerGroup, JobName, JobGroup, startTimeUtc, null, 1, TimeSpan.FromMilliseconds(1000));

        ISchedulerListener listener = A.Fake<ISchedulerListener>();

        await scheduler.ScheduleJob(jobDetail, jobTrigger);
        // add listener after scheduled
        scheduler.ListenerManager.AddSchedulerListener(listener);

        // act
        await scheduler.RescheduleJob(new TriggerKey(TriggerName, TriggerGroup), jobTrigger);

        // assert
        // expect unschedule and schedule
        A.CallTo(() => listener.JobUnscheduled(new TriggerKey(TriggerName, TriggerGroup), A<CancellationToken>._)).MustHaveHappened();
        A.CallTo(() => listener.JobScheduled(jobTrigger, A<CancellationToken>._)).MustHaveHappened();
    }

    [Test]
    [Ignore("Flaky in CI")]
    public void CurrentlyExecutingJobs()
    {
        IReadOnlyCollection<IJobExecutionContext> executingJobs;

        var scheduler = CreateQuartzScheduler("A", "B", 5);

        executingJobs = scheduler.GetCurrentlyExecutingJobs();
        Assert.That(executingJobs, Is.Empty);

        scheduler.Start().GetAwaiter().GetResult();

        executingJobs = scheduler.GetCurrentlyExecutingJobs();
        Assert.That(executingJobs, Is.Empty);

        ScheduleJobs<DelayedJob>(scheduler, 3, true, false, 1, TimeSpan.FromMilliseconds(1), 1);
        ScheduleJobs<DelayedJob>(scheduler, 1, true, false, 1, TimeSpan.FromMilliseconds(1), 0);

        Thread.Sleep(150);

        executingJobs = scheduler.GetCurrentlyExecutingJobs();
        Assert.That(executingJobs, Has.Count.EqualTo(4));

        Thread.Sleep(150);

        executingJobs = scheduler.GetCurrentlyExecutingJobs();
        Assert.That(executingJobs, Has.Count.EqualTo(3));

        Thread.Sleep(300);

        executingJobs = scheduler.GetCurrentlyExecutingJobs();
        Assert.That(executingJobs, Is.Empty);

        scheduler.Shutdown(true).GetAwaiter().GetResult();
    }

    [Test]
    [Ignore("Flaky in CI")]
    public void NumJobsExecuted()
    {
        var scheduler = CreateQuartzScheduler("A", "B", 5);

        Assert.That(scheduler.NumJobsExecuted, Is.EqualTo(0));

        scheduler.Start().GetAwaiter().GetResult();

        Assert.That(scheduler.NumJobsExecuted, Is.EqualTo(0));

        ScheduleJobs<DelayedJob>(scheduler, 3, true, false, 1, TimeSpan.FromMilliseconds(1), 1);
        ScheduleJobs<DelayedJob>(scheduler, 1, true, false, 1, TimeSpan.FromMilliseconds(1), 0);

        Thread.Sleep(150);

        Assert.That(scheduler.NumJobsExecuted, Is.EqualTo(4));

        Thread.Sleep(150);

        Assert.That(scheduler.NumJobsExecuted, Is.EqualTo(7));

        Thread.Sleep(200);

        Assert.That(scheduler.NumJobsExecuted, Is.EqualTo(7));

        scheduler.Shutdown(true).GetAwaiter().GetResult();
    }

    private static void ScheduleJobs<T>(QuartzScheduler scheduler,
        int jobCount,
        bool disableConcurrentExecution,
        bool persistJobDataAfterExecution,
        int triggersPerJob,
        TimeSpan repeatInterval,
        int repeatCount)
    {
        var triggersByJob = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>();

        for (var i = 0; i < jobCount; i++)
        {
            var job = CreateJobDetail(typeof(QuartzSchedulerTest).Name,
                typeof(T),
                disableConcurrentExecution,
                persistJobDataAfterExecution);

            var triggers = new ITrigger[triggersPerJob];
            for (var j = 0; j < triggersPerJob; j++)
            {
                triggers[j] = CreateTrigger(job, repeatInterval, repeatCount);
            }

            triggersByJob.Add(job, triggers);
        }

        scheduler.ScheduleJobs(triggersByJob, false).GetAwaiter().GetResult();
    }


    private static QuartzScheduler CreateQuartzScheduler(string name, string instanceId, int threadCount)
    {
        var threadPool = new DefaultThreadPool { MaxConcurrency = threadCount };
        threadPool.Initialize();

        QuartzSchedulerResources res = new QuartzSchedulerResources
        {
            Name = name,
            InstanceId = instanceId,
            ThreadPool = threadPool,
            JobRunShellFactory = new StdJobRunShellFactory(),
            JobStore = new RAMJobStore(),
            IdleWaitTime = TimeSpan.FromMilliseconds(10),
            MaxBatchSize = threadCount,
            BatchTimeWindow = TimeSpan.FromMilliseconds(10)
        };

        var scheduler = new QuartzScheduler(res);
        scheduler.JobFactory = new SimpleJobFactory();
        return scheduler;
    }

    private static ITrigger CreateTrigger(IJobDetail job, TimeSpan repeatInterval, int repeatCount)
    {
        return TriggerBuilder.Create()
            .ForJob(job)
            .WithSimpleSchedule(
                sb => sb.WithRepeatCount(repeatCount)
                    .WithInterval(repeatInterval)
                    .WithMisfireHandlingInstruction(MisfireInstruction.IgnoreMisfirePolicy))
            .Build();
    }

    private static IJobDetail CreateJobDetail(string group,
        Type jobType,
        bool disableConcurrentExecution,
        bool persistJobDataAfterExecution)
    {
        return JobBuilder.Create(jobType)
            .WithIdentity(Guid.NewGuid().ToString(), group)
            .DisallowConcurrentExecution(disableConcurrentExecution)
            .PersistJobDataAfterExecution(persistJobDataAfterExecution)
            .Build();
    }

    public class DelayedJob : IJob
    {
        private static readonly TimeSpan _delay = TimeSpan.FromMilliseconds(200);

        public async ValueTask Execute(IJobExecutionContext context)
        {
            await Task.Delay(_delay).ConfigureAwait(false);
        }
    }
}