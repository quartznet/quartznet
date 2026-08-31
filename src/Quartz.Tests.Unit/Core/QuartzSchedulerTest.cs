using Microsoft.Extensions.Logging.Abstractions;
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
using Quartz.Jobs;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Core;

/// <author>Marko Lahma (.NET)</author>
[NonParallelizable]
public class QuartzSchedulerTest
{
    /// <summary>
    /// How long a test is willing to wait for a signal before declaring the scheduler stuck. Long enough
    /// that a loaded build agent never trips it, and never used as a measurement.
    /// </summary>
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

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
        ISchedulerFactory sf = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        IScheduler scheduler = await sf.GetScheduler();

        DateTime runTime = DateTime.Now.AddMinutes(10);

        // define the job and tie it to our HelloJob class
        var job = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey("job1", "group1"))
            .Build();

        // Trigger the job to run on the next round minute
        IOperableTrigger trigger = new SimpleTriggerImpl { Key = new TriggerKey("trigger1", "group1"), StartTimeUtc = runTime };

        // set invalid calendar
        trigger.CalendarName = "FOOBAR";

        try
        {
            await scheduler.ScheduleJob(job, trigger);
            Assert.Fail("No error for non-existing calendar");
        }
        catch (SchedulerException ex)
        {
            Assert.That(ex.Message, Is.EqualTo(ExpectedError));
        }

        try
        {
            await scheduler.ScheduleJob(trigger);
            Assert.Fail("No error for non-existing calendar");
        }
        catch (SchedulerException ex)
        {
            Assert.That(ex.Message, Is.EqualTo(ExpectedError));
        }

        await scheduler.Shutdown(false);
    }

    [Test]
    public async Task TestStartDelayed()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        ISchedulerFactory sf = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();

        IScheduler scheduler = await sf.GetScheduler();
        await scheduler.StartDelayed(TimeSpan.FromMilliseconds(100));
        scheduler.Status.Should().Be(SchedulerStatus.Created);
        await Task.Delay(2000);
        scheduler.Status.Should().Be(SchedulerStatus.Running);
    }

    /// <summary>
    /// The delay is waited out on a task nobody observes, so one the timer refuses used to fault that
    /// task and be collected in silence — leaving a scheduler that was never going to start, with
    /// nothing thrown, nothing logged and nothing naming the delay.
    /// </summary>
    [Test]
    public async Task StartDelayedRefusesADelayNoTimerWillWaitOut()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        properties["quartz.scheduler.instanceName"] = "StartDelayedCeiling";
        ISchedulerFactory sf = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        IScheduler scheduler = await sf.GetScheduler();

        var act = async () => await scheduler.StartDelayed(TimeSpan.FromDays(60));

        (await act.Should().ThrowAsync<ArgumentOutOfRangeException>(
            "the caller has to hear about it; the wait itself has nobody to tell"))
            .Which.ParamName.Should().Be("delay");

        scheduler.Status.Should().Be(SchedulerStatus.Created, "a refused delay must not leave the scheduler half started");

        await scheduler.Shutdown();
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
        ISchedulerFactory sf = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        IScheduler scheduler = await sf.GetScheduler();
        DateTime startTimeUtc = DateTime.UtcNow.AddSeconds(2);
        var jobDetail = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey(JobName, JobGroup))
            .Build();
        SimpleTriggerImpl jobTrigger = new SimpleTriggerImpl(TriggerName, TriggerGroup, JobName, JobGroup, startTimeUtc, null, 1, TimeSpan.FromMilliseconds(1000));

        ISchedulerListener listener = A.Fake<ISchedulerListener>();
        // a fake does not run the interface's default Name implementation, and the manager identifies
        // scheduler listeners by name
        A.CallTo(() => listener.Name).Returns("rescheduleListener");

        await scheduler.ScheduleJob(jobDetail, jobTrigger);
        // add listener after scheduled
        scheduler.ListenerManager.AddSchedulerListener(listener);

        // act
        await scheduler.RescheduleJob(new TriggerKey(TriggerName, TriggerGroup), jobTrigger);

        // assert
        // expect unschedule and schedule
        // The scheduler argument is the very instance the caller holds, so it is matched by reference
        // rather than by any property of it.
        A.CallTo(() => listener.JobUnscheduled(scheduler, new TriggerKey(TriggerName, TriggerGroup), A<CancellationToken>._)).MustHaveHappened();
        A.CallTo(() => listener.JobScheduled(scheduler, jobTrigger, A<CancellationToken>._)).MustHaveHappened();
    }

    /// <summary>
    /// The listing is of firings that have begun and not yet ended, and it is exactly that at every
    /// point: nothing before the first one starts, one entry per firing while they are held open, and
    /// nothing once each has been let go.
    /// </summary>
    /// <remarks>
    /// Every step is driven by a signal a firing raises rather than by an elapsed interval — which is
    /// what this test used to do, and what made it flaky enough to switch off. A job that sleeps for
    /// 200ms is executing at 150ms only on a machine with a thread to spare at the instant the test
    /// wanted one.
    /// </remarks>
    [Test]
    public async Task GetCurrentlyExecutingJobsListsTheFiringsThatHaveStartedAndNotFinished()
    {
        QuartzScheduler scheduler = await CreateQuartzScheduler("currentlyExecuting", "instance", threadCount: 5);
        CompletionRecordingJobListener completions = new();
        scheduler.ListenerManager.AddJobListener(completions);

        try
        {
            scheduler.GetCurrentlyExecutingJobs().Should().BeEmpty("nothing has fired yet");

            await scheduler.Start();

            scheduler.GetCurrentlyExecutingJobs().Should().BeEmpty(
                "starting a scheduler with nothing scheduled cannot have begun a firing");

            List<ExecutionGate> gates = await ScheduleGatedJobs(scheduler, jobCount: 4);

            await ShouldObserve(
                Task.WhenAll(gates.Select(gate => gate.Started.Reaches(1))),
                "all four jobs have to be in flight before the listing means anything");

            scheduler.GetCurrentlyExecutingJobs().Should().HaveCount(4,
                "every firing that has begun and not ended is listed");

            gates[0].Open();
            await ShouldObserve(completions.Completed.Reaches(1), "the released job has to finish before it can be missing");

            scheduler.GetCurrentlyExecutingJobs().Should().HaveCount(3,
                "a firing leaves the listing as soon as it is over, and the other three are still held open");

            foreach (ExecutionGate gate in gates.Skip(1))
            {
                gate.Open();
            }

            await ShouldObserve(completions.Completed.Reaches(4), "every firing has to finish");

            scheduler.GetCurrentlyExecutingJobs().Should().BeEmpty(
                "with every firing over there is nothing left to list");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// The count is of firings that began, and it counts each of them once.
    /// </summary>
    /// <remarks>
    /// Seven firings are asked for and seven are waited for; the schedule then has nothing left, and the
    /// shutdown that follows is what makes "and no more than seven" a fact rather than the verdict of a
    /// sleep that happened to be long enough.
    /// </remarks>
    [Test]
    public async Task NumberOfJobsExecutedCountsEveryFiringOnce()
    {
        QuartzScheduler scheduler = await CreateQuartzScheduler("jobsExecuted", "instance", threadCount: 5);
        CompletionRecordingJobListener completions = new();
        scheduler.ListenerManager.AddJobListener(completions);

        try
        {
            scheduler.NumberOfJobsExecuted.Should().Be(0, "nothing has fired yet");

            await scheduler.Start();

            scheduler.NumberOfJobsExecuted.Should().Be(0,
                "starting a scheduler with nothing scheduled cannot have fired anything");

            // Three jobs whose trigger fires twice and one whose trigger fires once: seven firings, and
            // no schedule left over afterwards.
            await ScheduleJobs<CountedJob>(scheduler, jobCount: 3, repeatCount: 1);
            await ScheduleJobs<CountedJob>(scheduler, jobCount: 1, repeatCount: 0);

            await ShouldObserve(completions.Completed.Reaches(7), "every firing the schedule calls for has to happen");

            scheduler.NumberOfJobsExecuted.Should().Be(7, "the seven firings that ran are seven firings counted");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        scheduler.NumberOfJobsExecuted.Should().Be(7,
            "the schedule is exhausted and the scheduler is down, so nothing further can have been counted");
        scheduler.GetCurrentlyExecutingJobs().Should().BeEmpty("every firing ended before the shutdown returned");
    }

    private static async Task ShouldObserve(Task observation, string because)
    {
        Func<Task> act = () => observation;
        await act.Should().CompleteWithinAsync(observationDeadline, because);
    }

    /// <summary>
    /// Schedules <paramref name="jobCount" /> jobs that each hold their firing open until the gate
    /// handed back for them is opened, one trigger and one firing apiece.
    /// </summary>
    private static async Task<List<ExecutionGate>> ScheduleGatedJobs(QuartzScheduler scheduler, int jobCount)
    {
        List<ExecutionGate> gates = new(jobCount);
        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersByJob = new();

        for (int i = 0; i < jobCount; i++)
        {
            ExecutionGate gate = new();
            gates.Add(gate);

            IJobDetail job = CreateJobDetail<GatedJob>(new JobDataMap { [ExecutionGate.JobDataKey] = gate });
            triggersByJob.Add(job, [CreateTrigger(job, repeatCount: 0)]);
        }

        await scheduler.ScheduleJobs(triggersByJob);
        return gates;
    }

    private static async Task ScheduleJobs<TJob>(QuartzScheduler scheduler, int jobCount, int repeatCount)
        where TJob : IJob
    {
        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersByJob = new();

        for (int i = 0; i < jobCount; i++)
        {
            IJobDetail job = CreateJobDetail<TJob>(new JobDataMap());
            triggersByJob.Add(job, [CreateTrigger(job, repeatCount)]);
        }

        await scheduler.ScheduleJobs(triggersByJob);
    }

    private static async Task<QuartzScheduler> CreateQuartzScheduler(string name, string instanceId, int threadCount)
    {
        DefaultThreadPool threadPool = new() { MaxConcurrency = threadCount };
        await threadPool.Initialize();

        RAMJobStore jobStore = TestJobStores.Ram();
        await jobStore.Initialize(TestJobStores.Identity(name, instanceId));

        QuartzSchedulerResources res = new QuartzSchedulerResources
        {
            Name = name,
            InstanceId = instanceId,
            ThreadPool = threadPool,
            JobRunShellFactory = new StdJobRunShellFactory(NullLogger<JobRunShell>.Instance),
            JobStore = jobStore,
            IdleWaitTime = TimeSpan.FromMilliseconds(10),
            MaxBatchSize = threadCount,
            BatchTimeWindow = TimeSpan.FromMilliseconds(10)
        };

        QuartzScheduler scheduler = new QuartzScheduler(res);
        scheduler.JobFactory = new SimpleJobFactory();
        return scheduler;
    }

    private static ITrigger CreateTrigger(IJobDetail job, int repeatCount)
    {
        return TriggerBuilder.Create()
            .ForJob(job)
            .WithSimpleSchedule(
                sb => sb.WithRepeatCount(repeatCount)
                    .WithInterval(TimeSpan.FromMilliseconds(1))
                    .WithMisfireInstruction(SimpleTriggerMisfireInstruction.IgnoreMisfires))
            .Build();
    }

    private static IJobDetail CreateJobDetail<TJob>(JobDataMap jobDataMap) where TJob : IJob
    {
        return JobBuilder.Create<TJob>()
            .WithIdentity(Guid.NewGuid().ToString(), nameof(QuartzSchedulerTest))
            .DisallowConcurrentExecution()
            .UsingJobData(jobDataMap)
            .Build();
    }

    /// <summary>
    /// One firing's half of the conversation with the test: it says when it has started, and waits to be
    /// let go. Handed to the job through its data map, so nothing is static and two tests cannot see each
    /// other's firings.
    /// </summary>
    private sealed class ExecutionGate
    {
        public const string JobDataKey = "gate";

        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The firings of this job that have begun.</summary>
        public CallLog<JobKey> Started { get; } = new();

        /// <summary>Completes when <see cref="Open" /> is called, and never before.</summary>
        public Task Released => release.Task;

        public void Open() => release.TrySetResult();
    }

    /// <summary>
    /// A job that begins and then runs until the test lets it stop.
    /// </summary>
    public sealed class GatedJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            ExecutionGate gate = (ExecutionGate) context.MergedJobDataMap[ExecutionGate.JobDataKey];
            gate.Started.Record(context.JobDetail.Key);
            await gate.Released.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A job that does nothing, for the tests that count firings rather than watch them.
    /// </summary>
    public sealed class CountedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Records each firing as it ends.
    /// </summary>
    /// <remarks>
    /// The scheduler notifies its own <c>ExecutingJobsManager</c> before any listener the application
    /// registered, so by the time this records a firing the scheduler has already dropped it from
    /// <see cref="QuartzScheduler.GetCurrentlyExecutingJobs" /> — which is what makes it safe to assert
    /// on that listing the moment this signals.
    /// </remarks>
    private sealed class CompletionRecordingJobListener : IJobListener
    {
        public CallLog<JobKey> Completed { get; } = new();

        public ValueTask JobWasExecuted(
            IJobExecutionContext context,
            JobExecutionException jobException,
            CancellationToken cancellationToken = default)
        {
            Completed.Record(context.JobDetail.Key);
            return default;
        }
    }
}