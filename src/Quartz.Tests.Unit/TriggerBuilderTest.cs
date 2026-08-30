using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

[NonParallelizable]
public class TriggerBuilderTest
{
    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestStatefulJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestAnnotatedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    [SetUp]
    public void SetUp()
    {
    }

    [Test]
    public void TestTriggerBuilder()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(trigger.Key.Name, Is.Not.EqualTo(null), "Expected non-null trigger name ");
            Assert.That(trigger.Key.Group, Is.EqualTo(JobKey.DefaultGroup), "Unexpected trigger group: " + trigger.Key.Group);
            Assert.That(trigger.JobKey, Is.EqualTo(null), "Unexpected job key: " + trigger.JobKey);
            Assert.That(trigger.Description, Is.EqualTo(null), "Unexpected job description: " + trigger.Description);
            Assert.That(trigger.Priority, Is.EqualTo(TriggerConstants.DefaultPriority), "Unexpected trigger priority: " + trigger.Priority);
            Assert.That(trigger.StartTimeUtc.DateTime, Is.EqualTo(DateTimeOffset.UtcNow.DateTime).Within(TimeSpan.FromSeconds(1)), "Unexpected start-time: " + trigger.StartTimeUtc);
            Assert.That(trigger.EndTimeUtc, Is.EqualTo(null), "Unexpected end-time: " + trigger.EndTimeUtc);
        });

        DateTimeOffset stime = TestDates.EvenSecondDateAfterNow();

        trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithDescription("my description")
            .WithPriority(2)
            .EndAt(TestDates.FutureDate(10, IntervalUnit.Week))
            .StartAt(stime)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(trigger.Key.Name, Is.EqualTo("t1"), "Unexpected trigger name " + trigger.Key.Name);
            Assert.That(trigger.Key.Group, Is.EqualTo(JobKey.DefaultGroup), "Unexpected trigger group: " + trigger.Key.Group);
            Assert.That(trigger.JobKey, Is.EqualTo(null), "Unexpected job key: " + trigger.JobKey);
            Assert.That(trigger.Description, Is.EqualTo("my description"), "Unexpected job description: " + trigger.Description);
            Assert.That(trigger.Priority, Is.EqualTo(2), "Unexpected trigger priority: " + trigger);
            Assert.That(trigger.StartTimeUtc, Is.EqualTo(stime), "Unexpected start-time: " + trigger.StartTimeUtc);
            Assert.That(trigger.EndTimeUtc, Is.Not.EqualTo(null), "Unexpected end-time: " + trigger.EndTimeUtc);
        });
    }

    [Test]
    public void TestTriggerBuilderWithEndTimePriorCurrentTime()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("some trigger name", "some trigger group")
            .ForJob("some job name", "some job group")
            .StartAt(DateTime.Now - TimeSpan.FromMilliseconds(200000000))
            .EndAt(DateTime.Now - TimeSpan.FromMilliseconds(100000000))
            .WithCronSchedule("0 0 0 * * ?")
            .Build();
    }

    [Test(Description = "https://github.com/quartznet/quartznet/pull/212")]
    public void TestOverwriting()
    {
        var map = new JobDataMap();
        map["key"] = "overwritingvalue";
        var trigger = TriggerBuilder.Create()
            .UsingJobData("key", "originalvalue")
            .UsingJobData(map)
            .Build();

        Assert.That(trigger.JobDataMap["key"], Is.EqualTo("overwritingvalue"));
    }

    [Test]
    public void UsingJobData_StoresTheValueWithItsOwnType()
    {
        Guid guid = Guid.NewGuid();

        ITrigger trigger = TriggerBuilder.Create<TestJob>()
            .UsingJobData("string", "text")
            .UsingJobData("int", 1)
            .UsingJobData("long", 2L)
            .UsingJobData("float", 3.5f)
            .UsingJobData("double", 4.5d)
            .UsingJobData("decimal", 5.5m)
            .UsingJobData("bool", true)
            .UsingJobData("guid", guid)
            .UsingJobData("char", 'c')
            .UsingJobData("null", null)
            .Build();

        // Same contract as JobBuilder's: the one object?-typed overload has to store exactly what
        // the nine primitive overloads it replaced stored.
        trigger.JobDataMap["string"].Should().Be("text");
        trigger.JobDataMap["int"].Should().Be(1).And.BeOfType<int>();
        trigger.JobDataMap["long"].Should().Be(2L).And.BeOfType<long>();
        trigger.JobDataMap["float"].Should().Be(3.5f).And.BeOfType<float>();
        trigger.JobDataMap["double"].Should().Be(4.5d).And.BeOfType<double>();
        trigger.JobDataMap["decimal"].Should().Be(5.5m).And.BeOfType<decimal>();
        trigger.JobDataMap["bool"].Should().Be(true).And.BeOfType<bool>();
        trigger.JobDataMap["guid"].Should().Be(guid).And.BeOfType<Guid>();
        trigger.JobDataMap["char"].Should().Be('c').And.BeOfType<char>();
        trigger.JobDataMap["null"].Should().BeNull();
    }

    [Test]
    public void WithCalendarName_NamesTheCalendarTheTriggerObserves()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithCalendarName("holidays")
            .Build();

        trigger.CalendarName.Should().Be("holidays");

        TriggerBuilder.Create().WithCalendarName(null).Build().CalendarName.Should().BeNull();
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/3294")]
    public void WithCalendarName_TreatsABlankNameAsNoCalendar()
    {
        TriggerBuilder.Create().WithCalendarName("").Build()
            .CalendarName.Should().BeNull(
                "every job store gates its calendar lookup on a non-null name, so a blank one would be looked up, not found, and the trigger would silently stop firing");

        TriggerBuilder.Create().WithCalendarName("   ").Build()
            .CalendarName.Should().BeNull();

        TriggerBuilder.Create().WithCalendarName(" holidays ").Build()
            .CalendarName.Should().Be(" holidays ",
                "a calendar is looked up by the exact name it was stored under, so trimming would break a calendar genuinely registered with padding");
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/3294")]
    public void CalendarName_TreatsABlankNameAsNoCalendar_WhenSetDirectly()
    {
        // The builder is not the only writer: the JSON converters, the ADO store and plain user code
        // all assign the property, so the normalization has to live in the setter.
        IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create().WithCalendarName("holidays").Build();

        trigger.CalendarName = "";
        trigger.CalendarName.Should().BeNull();

        trigger.CalendarName = "   ";
        trigger.CalendarName.Should().BeNull();

        trigger.CalendarName = "holidays";
        trigger.CalendarName.Should().Be("holidays");
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/3294")]
    public void GetTriggerBuilder_DoesNotResurrectABlankCalendarName()
    {
        IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create().WithIdentity("t1").Build();
        trigger.CalendarName = "";

        trigger.GetTriggerBuilder().Build().CalendarName.Should().BeNull();
    }

    [Test]
    public void WithXSchedule_KeepsTheBuilderType()
    {
        // The extensions are generic in the receiver, so a chain that starts on TriggerBuilder<TJob>
        // stays on it and can go on naming the job's own properties afterwards.
        TriggerBuilder<TestJob> builder = TriggerBuilder.Create<TestJob>()
            .WithCronSchedule("0 0 12 * * ?")
            .WithIdentity("t1");

        builder.Build().Should().BeAssignableTo<ICronTrigger>();
    }

    [Test]
    public void WithXSchedule_KeepsTheConfiguratorType()
    {
        // ...and one that starts on the configurator the container hands out stays on that.
        ITriggerConfigurator<TestJob> configurator = TriggerBuilder.Create<TestJob>();

        ITriggerConfigurator<TestJob> configured = configurator
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(5)).RepeatForever())
            .WithDescription("every five minutes");

        ITrigger trigger = ((TriggerBuilder<TestJob>) configured).Build();

        trigger.Description.Should().Be("every five minutes");
        trigger.Should().BeAssignableTo<ISimpleTrigger>()
            .Which.RepeatInterval.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void WithSchedule_OnTheConfigurator_KeepsTheJobsType()
    {
        // WithSchedule is redeclared on the generic configurator for the same reason: losing TJob
        // here would lose the property-named job data that comes after it.
        ITriggerConfigurator<TestJob> configured = ((ITriggerConfigurator<TestJob>) TriggerBuilder.Create<TestJob>())
            .WithSchedule(CronScheduleBuilder.Create("0 0 12 * * ?"))
            .WithDescription("noon");

        ((TriggerBuilder<TestJob>) configured).Build().Description.Should().Be("noon");
    }

    [Test]
    public void WithXSchedule_TakesAPrebuiltBuilder()
    {
        // The hash-key cron overloads are gone; a hash key rides on the CronExpression instead.
        ITrigger trigger = TriggerBuilder.Create()
            .WithCronSchedule(CronScheduleBuilder.Create(new CronExpression("H H * * * ?", "custom-key")))
            .Build();

        trigger.Should().BeAssignableTo<ICronTrigger>();
    }

    /// <summary>
    /// Every schedule builder Quartz ships constructs its trigger with no clock at all - it has no
    /// reason to know about one - so the trigger builder is what puts its own clock on the trigger
    /// it hands back.
    /// </summary>
    [TestCaseSource(nameof(EveryShippedSchedule))]
    public void TheBuiltTriggerHoldsTheBuildersClock(string _, IScheduleBuilder schedule)
    {
        FakeTimeProvider clock = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 8, 0, 0, TimeSpan.Zero));

        ITrigger trigger = TriggerBuilder.Create(clock)
            .WithIdentity("clocked")
            .WithSchedule(schedule)
            .Build();

        trigger.Should().BeAssignableTo<TriggerBase>()
            .Which.TimeProvider.Should().BeSameAs(clock,
                "every 'now' the trigger reads afterwards - the past-due clamp and the whole of UpdateAfterMisfire - "
                + "has to be the clock the builder was created with, not the machine's");
    }

    /// <summary>
    /// And a builder created without one leaves the trigger on the system clock, which is what
    /// <c>TriggerBuilder.Create()</c> has always meant.
    /// </summary>
    [TestCaseSource(nameof(EveryShippedSchedule))]
    public void ABuilderWithNoClockLeavesTheTriggerOnTheSystemClock(string _, IScheduleBuilder schedule)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("unclocked")
            .WithSchedule(schedule)
            .Build();

        trigger.Should().BeAssignableTo<TriggerBase>()
            .Which.TimeProvider.Should().BeSameAs(TimeProvider.System,
                "passing no clock is how a caller asks for the machine's");
    }

    /// <summary>
    /// The clock survives a rebuild, so rescheduling a trigger read out of a store does not quietly
    /// put it back on the machine's clock.
    /// </summary>
    [Test]
    public void GetTriggerBuilder_CarriesTheTriggersClockIntoTheRebuiltTrigger()
    {
        FakeTimeProvider clock = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 8, 0, 0, TimeSpan.Zero));

        ITrigger original = TriggerBuilder.Create(clock)
            .WithIdentity("rebuilt")
            .WithCronSchedule("0 0 12 * * ?")
            .Build();

        ITrigger rebuilt = original.GetTriggerBuilder().Build();

        rebuilt.Should().BeAssignableTo<TriggerBase>()
            .Which.TimeProvider.Should().BeSameAs(clock,
                "GetTriggerBuilder() rebuilds this trigger, and a rebuild of it is still it");
    }

    /// <summary>
    /// The arithmetic, not just the field: a cron trigger whose first fire time is already past has it
    /// clamped forward to "now", and on a 2024 clock that lands in 2024.
    /// </summary>
    [Test]
    public void ACronTriggerScheduledOnAFakeClockComputesItsFirstFireTimeOnThatClock()
    {
        FakeTimeProvider clock = new FakeTimeProvider(new DateTimeOffset(2024, 6, 15, 8, 0, 0, TimeSpan.Zero));

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity("noon-daily")
            .StartAt(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero))
            .WithCronSchedule("0 0 12 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc))
            .Build();

        DateTimeOffset? first = trigger.ComputeFirstFireTimeUtc(null);

        first.Should().Be(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero),
            "the start time is months past, so the first fire is clamped forward to the first noon after the "
            + "builder's now - and the builder's now is 2024-06-15T08:00Z, not whenever this test happens to run");
    }

    /// <summary>
    /// The shorthand builds the same schedule the delegate form spells out, so the 124 call sites
    /// that say <c>x =&gt; x.WithInterval(i).RepeatForever()</c> can say <c>i</c>.
    /// </summary>
    [Test]
    public void WithSimpleSchedule_TakingAnInterval_RepeatsForever()
    {
        ISimpleTrigger shorthand = (ISimpleTrigger) TriggerBuilder.Create()
            .WithSimpleSchedule(TimeSpan.FromHours(1))
            .Build();

        ISimpleTrigger spelledOut = (ISimpleTrigger) TriggerBuilder.Create()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        shorthand.RepeatInterval.Should().Be(spelledOut.RepeatInterval);
        shorthand.RepeatCount.Should().Be(SimpleTriggerImpl.RepeatIndefinitely,
            "omitting the repeat count is how a caller asks for the forever schedule the delegate form spells out")
            .And.Be(spelledOut.RepeatCount);
    }

    /// <summary>
    /// The count is the trigger's own <see cref="ISimpleTrigger.RepeatCount" />, not a total number of
    /// firings — the "- 1" the 3.x <c>ForTotalCount</c> factories did is deliberately not repeated here.
    /// </summary>
    [Test]
    public void WithSimpleSchedule_TakingARepeatCount_PassesItThroughUnchanged()
    {
        ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
            .WithSimpleSchedule(TimeSpan.FromMinutes(5), repeatCount: 2)
            .Build();

        trigger.RepeatInterval.Should().Be(TimeSpan.FromMinutes(5));
        trigger.RepeatCount.Should().Be(2,
            "the argument is the repeat count the trigger carries, so this fires three times in all - "
            + "the shorthand does no arithmetic the trigger would then have to be read back through");
    }

    /// <summary>
    /// Zero is a repeat count, not "unset": it is how a caller says "fire once and stop", and it has to
    /// survive the <see langword="null" /> that means forever.
    /// </summary>
    [Test]
    public void WithSimpleSchedule_TakingARepeatCountOfZero_FiresOnce()
    {
        ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
            .WithSimpleSchedule(TimeSpan.FromMinutes(5), repeatCount: 0)
            .Build();

        trigger.RepeatCount.Should().Be(0, "zero repeats is one firing, and is not the forever schedule");
    }

    [Test]
    public void Key_IsNullUntilAnIdentityIsNamed()
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create();

        builder.Key.Should().BeNull("nothing has named the trigger yet, so there is no identity to report");
    }

    [Test]
    public void Key_IsTheIdentityTheCallerNamed()
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create().WithIdentity("nightly", "reports");

        builder.Key.Should().Be(new TriggerKey("nightly", "reports"),
            "code that has to agree with this trigger - a job, a registration - reads the key rather than "
            + "building the trigger to find out");
    }

    /// <summary>
    /// Unlike <see cref="JobBuilder{TJob}.Key" />, this one reports a generated key too: the trigger
    /// builder keeps what <c>Build</c> generated, so building twice produces the same trigger.
    /// </summary>
    [Test]
    public void Key_AfterBuild_IsTheGeneratedIdentityTheTriggerCarries()
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create();

        ITrigger trigger = builder.Build();

        builder.Key.Should().Be(trigger.Key,
            "Build keeps the identity it generated, so a caller can read back what it scheduled");
        builder.Build().Key.Should().Be(trigger.Key,
            "and a second Build is the same trigger rather than a second one");
    }

    /// <summary>
    /// The imperative twin of the container's <c>q.ScheduleJob&lt;TJob&gt;</c>: the job detail is built
    /// on the way through, and with nothing naming the job it takes the trigger's identity.
    /// </summary>
    [Test]
    public async Task ScheduleJob_WithNoConfigurator_GivesTheJobTheTriggersIdentity()
    {
        IScheduler scheduler = await NewScheduler("schedule-job-borrows-identity");

        try
        {
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("nightly", "reports")
                .StartAt(DateTimeOffset.UtcNow.AddHours(1))
                .Build();

            DateTimeOffset firstFire = await scheduler.ScheduleJob<TestJob>(trigger);

            firstFire.Should().Be(trigger.StartTimeUtc, "the trigger has not fired yet, so its start time is next");

            IJobDetail job = await scheduler.GetJobDetail(new JobKey("nightly", "reports"));
            job.Should().NotBeNull("the job was named after the trigger, exactly as the DI ScheduleJob<T> names it")
                .And.Match<IJobDetail>(x => x.JobType.Type == typeof(TestJob));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task ScheduleJob_WithAConfigurator_TakesTheIdentityItNames()
    {
        IScheduler scheduler = await NewScheduler("schedule-job-named-identity");

        try
        {
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("nightly", "reports")
                .StartAt(DateTimeOffset.UtcNow.AddHours(1))
                .Build();

            await scheduler.ScheduleJob<TestJob>(trigger, job => job.WithIdentity("compaction").WithDescription("nightly compaction"));

            IJobDetail job = await scheduler.GetJobDetail(new JobKey("compaction"));
            job.Should().NotBeNull("an identity the caller named beats the one borrowed from the trigger")
                .And.Match<IJobDetail>(x => x.Description == "nightly compaction");

            ITrigger stored = await scheduler.GetTrigger(new TriggerKey("nightly", "reports"));
            stored.JobKey.Should().Be(new JobKey("compaction"), "the trigger named no job, so it took this one");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// A trigger built with <c>ForJob</c> already says which job it is for, and that beats the trigger's
    /// own key — otherwise the pair could not agree and the scheduler would reject them.
    /// </summary>
    [Test]
    public async Task ScheduleJob_WithATriggerThatNamesItsJob_TakesThatJobsIdentity()
    {
        IScheduler scheduler = await NewScheduler("schedule-job-trigger-names-job");

        try
        {
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("nightly", "reports")
                .ForJob("compaction", "maintenance")
                .StartAt(DateTimeOffset.UtcNow.AddHours(1))
                .Build();

            await scheduler.ScheduleJob<TestJob>(trigger);

            IJobDetail job = await scheduler.GetJobDetail(new JobKey("compaction", "maintenance"));
            job.Should().NotBeNull(
                "the trigger already named the job it fires, so borrowing the trigger's own key instead "
                + "would have scheduled a pair the scheduler rejects as not referring to each other");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static ValueTask<IScheduler> NewScheduler(string name)
    {
        return QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = name)
            .UseInMemoryStore()
            .BuildScheduler();
    }

    /// <summary>
    /// The five shipped schedules, each of which builds a different trigger implementation.
    /// </summary>
    private static IEnumerable<TestCaseData> EveryShippedSchedule()
    {
        yield return new TestCaseData("cron", CronScheduleBuilder.Create("0 0 12 * * ?"));
        yield return new TestCaseData("simple", SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromHours(1)).RepeatForever());
        yield return new TestCaseData("daily time interval", DailyTimeIntervalScheduleBuilder.Create().WithInterval(15, IntervalUnit.Minute));
        yield return new TestCaseData("calendar interval", CalendarIntervalScheduleBuilder.Create().WithInterval(1, IntervalUnit.Day));
        yield return new TestCaseData("recurrence", RecurrenceScheduleBuilder.Create("FREQ=DAILY"));
    }
}