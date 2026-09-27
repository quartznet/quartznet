using FakeItEasy;

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

        forNoJob.Should().Throw<ArgumentNullException>().WithParameterName("job");
        withNoDetail.Should().Throw<ArgumentNullException>().WithParameterName("jobDetail");
        withNoTrigger.Should().Throw<ArgumentNullException>().WithParameterName("trigger");
        withNoScheduler.Should().Throw<ArgumentNullException>().WithParameterName("scheduler");
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
}
