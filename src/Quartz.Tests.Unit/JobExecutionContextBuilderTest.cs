using FakeItEasy;

using Microsoft.Extensions.Time.Testing;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="JobExecutionContextBuilder" /> builds the context a scheduler would hand a job, from what
/// it is given and from a firing's defaults for the rest (#3869).
/// </summary>
public sealed class JobExecutionContextBuilderTest
{
    private static readonly DateTimeOffset scheduledAt = new(2026, 3, 6, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void TheContextCarriesWhatItWasGiven()
    {
        RecordingJob job = new();
        IJobDetail detail = JobBuilder.Create<RecordingJob>().WithIdentity("import", "sync").Build();
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("import-trigger", "sync")
            .ForJob(detail)
            .StartAt(scheduledAt)
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
        IScheduler scheduler = A.Fake<IScheduler>();
        DateTimeOffset firedAt = scheduledAt.AddSeconds(3);

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(job)
            .WithJob(detail)
            .WithTrigger(trigger)
            .WithScheduler(scheduler)
            .FiredAt(firedAt, scheduledAt)
            .Build();

        context.JobInstance.Should().BeSameAs(job);
        context.JobDetail.Should().BeSameAs(detail, "a job detail is used as it is given");
        context.Trigger.Key.Should().Be(trigger.Key);
        context.Scheduler.Should().BeSameAs(scheduler);
        context.FireTimeUtc.Should().Be(firedAt);
        context.ScheduledFireTimeUtc.Should().Be(scheduledAt, "a late firing reports the time it was due as well as the time it ran");
        context.NextFireTimeUtc.Should().Be(scheduledAt.AddHours(1), "the next fire time is the schedule's next one after this firing, as a job store reports it");
        context.PreviousFireTimeUtc.Should().BeNull("a trigger that has never fired has no previous fire time");
        context.Recovering.Should().BeFalse();
        context.RefireCount.Should().Be(0);
        context.FireInstanceId.Should().NotBeNullOrEmpty("a job store names every firing, and a job may log or report under the name");
    }

    [Test]
    public void WhatIsLeftOutTakesAFiringsDefaults()
    {
        RecordingJob job = new();
        DateTimeOffset before = TimeProvider.System.GetUtcNow();

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(job).Build();

        DateTimeOffset after = TimeProvider.System.GetUtcNow();
        context.JobDetail.JobType.Type.Should().Be<RecordingJob>("the default job detail is for the job's own type");
        context.JobDetail.Key.Name.Should().Be(nameof(RecordingJob));
        context.Trigger.JobKey.Should().Be(context.JobDetail.Key, "the default trigger fires the default job detail");
        context.FireTimeUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after, "a context not told when it fired fired now");
        context.ScheduledFireTimeUtc.Should().Be(context.FireTimeUtc, "and on time");
        context.NextFireTimeUtc.Should().BeNull("the default trigger fires once");
        context.Scheduler.Should().BeNull("there is no scheduler unless one is given");
        context.MergedJobDataMap.Should().BeEmpty();
    }

    [Test]
    public void TheTriggerIsCopiedAndTheOnePassedIsLeftAsItWas()
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("shared").StartAt(scheduledAt).Build();
        JobExecutionContextBuilder builder = JobExecutionContextBuilder.For(new RecordingJob())
            .WithTrigger(trigger)
            .WithInput("payload");

        using JobExecutionContextImpl first = builder.Build();
        using JobExecutionContextImpl second = builder.Build();

        first.Trigger.Should().NotBeSameAs(trigger, "a job store fires a copy of the trigger it holds");
        trigger.FireInstanceId.Should().BeNull("naming the firing is done to the copy");
        trigger.JobDataMap.ContainsKey(SchedulerConstants.JobInput).Should().BeFalse("the input is put on the copy");
        second.Trigger.Should().NotBeSameAs(first.Trigger, "each build fires its own copy");
        second.FireInstanceId.Should().NotBe(first.FireInstanceId, "two firings are two fire instances");
    }

    [Test]
    public void TheTriggersJobDataWinsOverTheJobsInTheMergedMap()
    {
        IJobDetail detail = JobBuilder.Create<RecordingJob>()
            .UsingJobData("source", "job")
            .UsingJobData("batch", "7")
            .Build();
        ITrigger trigger = TriggerBuilder.Create().ForJob(detail).UsingJobData("source", "trigger").Build();

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new RecordingJob())
            .WithJob(detail)
            .WithTrigger(trigger)
            .Build();

        context.MergedJobDataMap.GetString("source").Should().Be("trigger", "a trigger's job data overrides the job's, as it does when a scheduler fires it");
        context.MergedJobDataMap.GetString("batch").Should().Be("7");
    }

    [Test]
    public async Task ATypedJobIsHandedTheInput()
    {
        GreetingJob job = new();

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(job)
            .WithInput(new Greeting("Ada"))
            .Build();

        await ((IJob) job).Execute(context, CancellationToken.None);

        job.Greeted.Should().Be("Ada", "the input is read back without a serializer, because it was never serialized");
        context.GetInput<Greeting>().Should().Be(new Greeting("Ada"));
    }

    [Test]
    public void WithInputWinsOverAnInputOnTheJob()
    {
        IJobDetail detail = JobBuilder.Create<GreetingJob>().UsingInput(new Greeting("from the job")).Build();

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new GreetingJob())
            .WithJob(detail)
            .WithInput(new Greeting("from the firing"))
            .Build();

        context.GetInput<Greeting>().Should().Be(new Greeting("from the firing"),
            "the input goes on the trigger, and a trigger's input wins over its job's");
    }

    [Test]
    public void ATriggerThatCannotBeFiredIsRefusedByName()
    {
        ITrigger readModel = A.Fake<ITrigger>();
        JobExecutionContextBuilder builder = JobExecutionContextBuilder.For(new RecordingJob());

        Action act = () => builder.WithTrigger(readModel);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("trigger")
            .WithMessage("*IOperableTrigger*");
    }

    [Test]
    public void NullArgumentsAreRefused()
    {
        JobExecutionContextBuilder builder = JobExecutionContextBuilder.For(new RecordingJob());

        Action forNoJob = () => JobExecutionContextBuilder.For<RecordingJob>(null);
        Action withNoDetail = () => builder.WithJob(null);
        Action withNoTrigger = () => builder.WithTrigger(null);
        Action withNoScheduler = () => builder.WithScheduler(null);
        Action withNoCalendar = () => builder.WithCalendar(null);
        Action withNoClock = () => builder.WithTimeProvider(null);

        forNoJob.Should().Throw<ArgumentNullException>().WithParameterName("job");
        withNoDetail.Should().Throw<ArgumentNullException>().WithParameterName("jobDetail");
        withNoTrigger.Should().Throw<ArgumentNullException>().WithParameterName("trigger");
        withNoScheduler.Should().Throw<ArgumentNullException>().WithParameterName("scheduler");
        withNoCalendar.Should().Throw<ArgumentNullException>().WithParameterName("calendar");
        withNoClock.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    [Test]
    public void NegativeCountsAreRefused()
    {
        JobExecutionContextBuilder builder = JobExecutionContextBuilder.For(new RecordingJob());

        Action negativeRetry = () => builder.WithRetryAttempt(-1);
        Action negativeRefire = () => builder.WithRefireCount(-1);

        negativeRetry.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("retryAttempt",
            "an occurrence that has been retried a negative number of times is not one a scheduler can fire");
        negativeRefire.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("refireCount",
            "the refire loop counts up from zero");
    }

    [Test]
    public void ARecoveringContextNamesTheTriggerItStandsFor()
    {
        ITrigger trigger = TriggerBuilder.Create().WithIdentity("import-trigger", "sync").StartAt(scheduledAt).Build();
        DateTimeOffset firedAt = scheduledAt.AddSeconds(3);

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new RecordingJob())
            .WithTrigger(trigger)
            .FiredAt(firedAt, scheduledAt)
            .Recovering()
            .Build();

        context.Recovering.Should().BeTrue("Recovering() with no argument is a recovery, as RequestRecovery() with none requests one");
        context.RecoveringTriggerKey.Should().Be(trigger.Key,
            "a recovery trigger's job data names the trigger it stands for, and without it RecoveringTriggerKey has no key to give");
        context.MergedJobDataMap.GetString(SchedulerConstants.FailedJobOriginalTriggerFireTime)
            .Should().Be(Convert.ToString(firedAt, System.Globalization.CultureInfo.InvariantCulture), "written as a job store writes it");
        context.MergedJobDataMap.GetString(SchedulerConstants.FailedJobOriginalTriggerScheduledFireTime)
            .Should().Be(Convert.ToString(scheduledAt, System.Globalization.CultureInfo.InvariantCulture));
        trigger.JobDataMap.ContainsKey(SchedulerConstants.FailedJobOriginalTriggerName)
            .Should().BeFalse("the recovery data is written to the copy, as everything else the context is given is");
    }

    [Test]
    public void RecoveringFalseIsAnOrdinaryFiring()
    {
        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new RecordingJob())
            .Recovering()
            .Recovering(false)
            .Build();

        context.Recovering.Should().BeFalse("the last call decides, as with every other member");
        context.RecoveringTriggerKey.Should().BeNull();
        context.MergedJobDataMap.ContainsKey(SchedulerConstants.FailedJobOriginalTriggerName).Should().BeFalse();
    }

    [Test]
    public void ARecoveringTriggerThatAlreadyNamesItsOriginalKeepsIt()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("recover_node-a_1", SchedulerConstants.DefaultRecoveryGroup)
            .UsingJobData(SchedulerConstants.FailedJobOriginalTriggerName, "nightly")
            .UsingJobData(SchedulerConstants.FailedJobOriginalTriggerGroup, "reports")
            .Build();

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new RecordingJob())
            .WithTrigger(trigger)
            .Recovering()
            .Build();

        context.RecoveringTriggerKey.Should().Be(new TriggerKey("nightly", "reports"),
            "a test that spells out the recovery trigger a store would store gets that one");
    }

    [Test]
    public void WithRetryAttemptIsTheAttemptTheContextReports()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .StartAt(scheduledAt)
            .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(1)))
            .Build();

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new RecordingJob())
            .WithTrigger(trigger)
            .WithRetryAttempt(3)
            .Build();

        context.RetryAttempt.Should().Be(3);
        context.Trigger.RetryAttempt.Should().Be(3, "the attempt is written to the fired trigger, where a job store writes it");
        context.RetryPolicy!.MaxAttempts.Should().Be(context.RetryAttempt, "which makes this the occurrence's last attempt");
        trigger.RetryAttempt.Should().Be(0, "the trigger passed in is left as it was");
    }

    /// <summary>
    /// The testing tutorial's last-attempt sample: the policy comes from the job type, which the default job
    /// detail names, and the attempt from the builder.
    /// </summary>
    [TestCase(2, false)]
    [TestCase(3, true)]
    public void TheJobTypesPolicyAndTheAttemptSayWhetherThisIsTheLastAttempt(int attempt, bool last)
    {
        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new ThreeRetriesJob())
            .WithRetryAttempt(attempt)
            .Build();

        context.RetryPolicy.Should().NotBeNull("the default job detail is for the job's type, whose [RetryPolicy] applies to a built-in trigger");
        (context.RetryAttempt >= context.RetryPolicy!.MaxAttempts).Should().Be(last,
            "a policy of three retries has its last attempt on the third retry");
    }

    [Test]
    public void WithRefireCountIsTheRefireCountTheContextReports()
    {
        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new RecordingJob())
            .WithRefireCount(2)
            .Build();

        context.RefireCount.Should().Be(2);
        context.RetryAttempt.Should().Be(0, "a refire is not a retry, and setting one leaves the other alone");
    }

    [Test]
    public void WithCalendarIsTheCalendarAndTheNextFireTimeSkipsWhatItExcludes()
    {
        DateTimeOffset excluded = scheduledAt.AddHours(1);
        ICalendar calendar = A.Fake<ICalendar>();
        A.CallTo(() => calendar.IsTimeIncluded(A<DateTimeOffset>._)).Returns(true);
        A.CallTo(() => calendar.IsTimeIncluded(excluded)).Returns(false);
        ITrigger hourly = TriggerBuilder.Create()
            .StartAt(scheduledAt)
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new RecordingJob())
            .WithTrigger(hourly)
            .WithCalendar(calendar)
            .FiredAt(scheduledAt)
            .Build();

        context.Calendar.Should().BeSameAs(calendar);
        context.NextFireTimeUtc.Should().Be(scheduledAt.AddHours(2),
            "a job store reports the next time the calendar lets the trigger fire, and the calendar excludes the hour after");
    }

    [Test]
    public void ACalendarThatExcludesEveryTimeLeavesNoNextFireTime()
    {
        ICalendar excludesEverything = A.Fake<ICalendar>();
        ITrigger yearly = TriggerBuilder.Create()
            .StartAt(scheduledAt)
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromDays(365)).RepeatForever())
            .Build();

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new RecordingJob())
            .WithTrigger(yearly)
            .WithCalendar(excludesEverything)
            .FiredAt(scheduledAt)
            .Build();

        context.NextFireTimeUtc.Should().BeNull(
            "the walk past excluded times gives up at the year a trigger's own Triggered gives up at, rather than looping");
    }

    [Test]
    public async Task WithCancellationReachesTheContextsTokenAndTheJob()
    {
        using CancellationTokenSource test = new();
        CancellationObservingJob job = new();

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(job)
            .WithCancellation(test.Token)
            .Build();

        context.CancellationToken.IsCancellationRequested.Should().BeFalse("nothing has cancelled the firing yet");
        Task execution = job.Execute(context, context.CancellationToken).AsTask();
        await test.CancelAsync();

        Func<Task> run = () => execution;
        await run.Should().ThrowAsync<OperationCanceledException>("the job was handed the context's token, as the scheduler hands it");
        context.CancellationToken.IsCancellationRequested.Should().BeTrue("the context's token is linked to the test's");
    }

    [Test]
    public void AnInterruptCancelsTheContextsTokenAndLeavesTheTestsAlone()
    {
        using CancellationTokenSource test = new();

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new RecordingJob())
            .WithCancellation(test.Token)
            .Build();

        ((IInterruptableJobExecutionContext) context).Interrupt();

        context.CancellationToken.IsCancellationRequested.Should().BeTrue(
            "the context's token is its own source linked to the test's, so an interrupt can still cancel it");
        test.IsCancellationRequested.Should().BeFalse("an interrupt of the firing is not the test's to receive");
    }

    [Test]
    public void ATokenThatCannotBeCancelledLeavesTheContextItsOwn()
    {
        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new RecordingJob())
            .WithCancellation(CancellationToken.None)
            .Build();

        context.CancellationToken.CanBeCanceled.Should().BeTrue("the context still has a source of its own for an interrupt to cancel");

        ((IInterruptableJobExecutionContext) context).Interrupt();

        context.CancellationToken.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void WithTimeProviderIsTheClockTheContextIsBuiltOn()
    {
        FakeTimeProvider clock = new(scheduledAt);

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new RecordingJob())
            .WithTimeProvider(clock)
            .Build();

        context.FireTimeUtc.Should().Be(scheduledAt, "a context not told when it fired fired at its clock's now");
        context.Trigger.StartTimeUtc.Should().Be(scheduledAt, "and the default trigger starts then");
        context.JobRunTime.Should().Be(TimeSpan.Zero, "the run begins as the context is built, and the clock has not moved since");

        clock.Advance(TimeSpan.FromSeconds(5));

        context.JobRunTime.Should().Be(TimeSpan.FromSeconds(5),
            "a job that reads its run time mid-run gets exactly what the test advanced the clock by");
    }

    [Test]
    public void WithoutAClockTheRunCountsFromTheBuildOnTheSystemClock()
    {
        long before = TimeProvider.System.GetTimestamp();

        using JobExecutionContextImpl context = JobExecutionContextBuilder.For(new RecordingJob())
            .FiredAt(scheduledAt)
            .Build();

        TimeSpan runTime = context.JobRunTime;
        runTime.Should().BeLessThanOrEqualTo(TimeProvider.System.GetElapsedTime(before),
            "the run began when the context was built, and not at a fire time years before it");
    }

    public sealed record Greeting(string Name);

    public sealed class GreetingJob : IJob<Greeting>
    {
        public string Greeted { get; private set; }

        public ValueTask Execute(IJobExecutionContext context, Greeting input, CancellationToken cancellationToken = default)
        {
            Greeted = input.Name;
            return default;
        }
    }

    public sealed class RecordingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [RetryPolicy(3, "00:05:00")]
    public sealed class ThreeRetriesJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Waits for the token it is handed, as a job that forwards its token to its work does.
    /// </summary>
    public sealed class CancellationObservingJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
