#nullable enable

using System.Reflection;

using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;

using TimeZoneConverter;

namespace Quartz.Tests.Unit.Impl.Triggers;

/// <summary>
/// <c>RAMJobStore.TriggersFired</c> advances its own cron trigger and the caller's copy with one
/// computation where the two would compute the same thing, and every trigger with two otherwise.
/// </summary>
public sealed class TriggerCopyFiringTest
{
    private static readonly DateTimeOffset Start = new(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Stopped at <see cref="Start" />, so that a first fire time is computed from the start time rather
    /// than from whenever the test happens to run.
    /// </summary>
    private static TimeProvider Clock() => new FakeTimeProvider(Start);

    private static IEnumerable<TestCaseData> SharedComputationCases()
    {
        yield return Case("every five seconds", Cron("0/5 * * * * ?", TimeZoneInfo.Utc, firedAlready: 3));
        yield return Case("in a zone of its own", Cron("0 15 10 * * ?", TimeZoneInfo.CreateCustomTimeZone("plus-two", TimeSpan.FromHours(2), "plus-two", "plus-two"), firedAlready: 1));
        yield return Case("across a daylight-saving change", Cron("0 30 1 * * ?", TZConvert.GetTimeZoneInfo("Pacific Standard Time"), firedAlready: 2, start: new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero)));
        yield return Case("on its last firing", Cron("0 0/1 * * * ?", TimeZoneInfo.Utc, firedAlready: 2, end: Start.AddMinutes(3)));
        yield return Case("with no firing left", Cron("0 0/1 * * * ?", TimeZoneInfo.Utc, firedAlready: 2, end: Start.AddMinutes(2).AddSeconds(30)));
        yield return Case("with an offset on its start time", Cron("0 0/1 * * * ?", TimeZoneInfo.Utc, firedAlready: 1, start: Start.ToOffset(TimeSpan.FromHours(3))));

        static TestCaseData Case(string name, IOperableTrigger trigger) => new TestCaseData(trigger).SetArgDisplayNames(name);
    }

    [TestCaseSource(nameof(SharedComputationCases))]
    public void OneComputationLeavesBothInstancesAsTwoWould(IOperableTrigger template)
    {
        IOperableTrigger storedFiredItself = Clone(template);
        IOperableTrigger copyFiredItself = Clone(template);
        storedFiredItself.Triggered(null);
        copyFiredItself.Triggered(null);

        IOperableTrigger stored = Clone(template);
        IOperableTrigger copy = Clone(template);
        bool shared = TriggerCopyFiring.Triggered(stored, copy, calendar: null);

        shared.Should().BeTrue("a cron trigger and its unmodified copy compute the same next fire time");
        ShouldHaveTheSameFields(copy, copyFiredItself, "the copy has to be exactly what firing it would have made it");
        ShouldHaveTheSameFields(stored, storedFiredItself, "the stored trigger is fired as it always was");
    }

    [Test]
    public void ASimpleTriggerIsFiredOnBothInstances()
    {
        SimpleTriggerImpl template = new(Clock())
        {
            Key = new TriggerKey("simple"),
            StartTimeUtc = Start,
            RepeatCount = 5,
            RepeatInterval = TimeSpan.FromMinutes(1)
        };
        template.ComputeFirstFireTimeUtc(null);

        SimpleTriggerImpl stored = (SimpleTriggerImpl) template.Clone();
        SimpleTriggerImpl copy = (SimpleTriggerImpl) template.Clone();

        TriggerCopyFiring.Triggered(stored, copy, calendar: null).Should().BeFalse(
            "a simple trigger advances in the time comparing the two instances would take, so it is left as it was");
        copy.TimesTriggered.Should().Be(1);
        copy.NextFireTimeUtc.Should().Be(Start.AddMinutes(1)).And.Be(stored.NextFireTimeUtc);
    }

    [Test]
    public void ACalendarIsConsultedForEachInstance()
    {
        IOperableTrigger template = Cron("0 0/1 * * * ?", TimeZoneInfo.Utc, firedAlready: 1);
        IOperableTrigger stored = Clone(template);
        IOperableTrigger copy = Clone(template);
        CountingCalendar calendar = new();

        TriggerCopyFiring.Triggered(stored, copy, calendar).Should().BeFalse(
            "a calendar is the application's code, and it is asked about each instance as it always was");
        calendar.Consulted.Should().Be(2);
        copy.NextFireTimeUtc.Should().Be(stored.NextFireTimeUtc);
    }

    [Test]
    public void ADerivedTriggerIsFiredOnBothInstances()
    {
        CountingCronTrigger template = new(Clock())
        {
            Key = new TriggerKey("counting"),
            CronExpression = new CronExpression("0 0/1 * * * ?", TimeZoneInfo.Utc),
            StartTimeUtc = Start
        };
        template.ComputeFirstFireTimeUtc(null);

        CountingCronTrigger stored = (CountingCronTrigger) template.Clone();
        CountingCronTrigger copy = (CountingCronTrigger) template.Clone();

        TriggerCopyFiring.Triggered(stored, copy, calendar: null).Should().BeFalse();
        stored.TriggeredCalls.Should().Be(1);
        copy.TriggeredCalls.Should().Be(1, "a derived Triggered may do anything, so the copy is fired rather than written to");
    }

    [Test]
    public void ACopyWhoseNextFireTimeDiffersIsAdvancedFromItsOwn()
    {
        CronTriggerImpl template = Cron("0 0/1 * * * ?", TimeZoneInfo.Utc, firedAlready: 1);
        CronTriggerImpl stored = (CronTriggerImpl) template.Clone();
        CronTriggerImpl copy = (CronTriggerImpl) template.Clone();
        copy.NextFireTimeUtc = copy.NextFireTimeUtc!.Value.AddMinutes(5);

        TriggerCopyFiring.Triggered(stored, copy, calendar: null).Should().BeFalse(
            "a caller may hand the store a copy that no longer matches it, and that copy is advanced from what it says");
        copy.PreviousFireTimeUtc.Should().Be(Start.AddMinutes(6));
        copy.NextFireTimeUtc.Should().Be(Start.AddMinutes(7));
        stored.NextFireTimeUtc.Should().Be(Start.AddMinutes(2));
    }

    [Test]
    public void ACopyWhoseNextFireTimeHasAnotherOffsetIsAdvancedFromItsOwn()
    {
        CronTriggerImpl template = Cron("0 0/1 * * * ?", TimeZoneInfo.Utc, firedAlready: 1);
        CronTriggerImpl stored = (CronTriggerImpl) template.Clone();
        CronTriggerImpl copy = (CronTriggerImpl) template.Clone();
        copy.NextFireTimeUtc = copy.NextFireTimeUtc!.Value.ToOffset(TimeSpan.FromHours(3));

        TriggerCopyFiring.Triggered(stored, copy, calendar: null).Should().BeFalse(
            "the same instant at another offset is not the same state: the copy's previous fire time keeps its own offset");
        copy.PreviousFireTimeUtc!.Value.Offset.Should().Be(TimeSpan.FromHours(3));
    }

    [Test]
    public void ACopyWithAnotherStartOrEndTimeIsAdvancedFromItsOwn()
    {
        CronTriggerImpl template = Cron("0 0/1 * * * ?", TimeZoneInfo.Utc, firedAlready: 1);

        CronTriggerImpl laterStart = (CronTriggerImpl) template.Clone();
        laterStart.StartTimeUtc = Start.AddMinutes(10);
        TriggerCopyFiring.Triggered((CronTriggerImpl) template.Clone(), laterStart, calendar: null).Should().BeFalse();
        laterStart.NextFireTimeUtc.Should().Be(Start.AddMinutes(10), "the copy's own start time bounds its own next firing");

        CronTriggerImpl earlierEnd = (CronTriggerImpl) template.Clone();
        earlierEnd.EndTimeUtc = Start.AddMinutes(1);
        TriggerCopyFiring.Triggered((CronTriggerImpl) template.Clone(), earlierEnd, calendar: null).Should().BeFalse();
        earlierEnd.NextFireTimeUtc.Should().BeNull("the copy's own end time has been reached");
    }

    [Test]
    public void ACopyWithAnotherExpressionIsAdvancedFromItsOwn()
    {
        CronTriggerImpl template = Cron("0 0/1 * * * ?", TimeZoneInfo.Utc, firedAlready: 1);
        CronTriggerImpl stored = (CronTriggerImpl) template.Clone();
        CronTriggerImpl copy = (CronTriggerImpl) template.Clone();
        copy.CronExpression = new CronExpression("0 0/10 * * * ?", TimeZoneInfo.Utc);

        TriggerCopyFiring.Triggered(stored, copy, calendar: null).Should().BeFalse();
        stored.NextFireTimeUtc.Should().Be(Start.AddMinutes(2));
        copy.NextFireTimeUtc.Should().Be(Start.AddMinutes(10));
    }

    [Test]
    public void ATriggerWithNoNextFireTimeIsFiredOnBothInstances()
    {
        CronTriggerImpl template = Cron("0 0/1 * * * ?", TimeZoneInfo.Utc, firedAlready: 0);
        template.NextFireTimeUtc = null;

        TriggerCopyFiring.Triggered(Clone(template), Clone(template), calendar: null).Should().BeFalse(
            "with no next fire time the computation reads the clock, which each instance has to read for itself");
    }

    [Test]
    public void OneInstanceHandedInTwiceIsFiredTwice()
    {
        CronTriggerImpl trigger = Cron("0 0/1 * * * ?", TimeZoneInfo.Utc, firedAlready: 1);

        TriggerCopyFiring.Triggered(trigger, trigger, calendar: null).Should().BeFalse();
        trigger.NextFireTimeUtc.Should().Be(Start.AddMinutes(3),
            "the store has always fired both of what it was handed, however many objects that was");
    }

    [Test]
    public async Task TheStoreHandsBackACopyAdvancedAsTheStoredTriggerIs()
    {
        TimeProvider clock = Clock();
        RAMJobStore store = TestJobStores.Ram(timeProvider: clock);
        await store.Initialize(TestJobStores.Identity());

        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job").StoreDurably().Build();
        await store.AddJob(job);

        CronTriggerImpl trigger = new(clock)
        {
            Key = new TriggerKey("trigger"),
            JobKey = job.Key,
            CronExpression = new CronExpression("0 0/1 * * * ?", TimeZoneInfo.Utc),
            StartTimeUtc = Start
        };
        trigger.ComputeFirstFireTimeUtc(null);
        await store.AddTrigger(trigger);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(
            new TriggerAcquisitionRequest { NoLaterThan = Start.AddMinutes(1), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);

        TriggerFiredBundle bundle = fired.Should().ContainSingle().Which.TriggerFiredBundle!;
        CronTriggerImpl stored = (CronTriggerImpl) (await store.GetTrigger(trigger.Key))!;
        CronTriggerImpl copy = bundle.Trigger.Should().BeOfType<CronTriggerImpl>().Subject;

        copy.Should().BeSameAs(acquired[0], "the bundle carries the caller's own copy");
        copy.PreviousFireTimeUtc.Should().Be(Start).And.Be(stored.PreviousFireTimeUtc);
        copy.NextFireTimeUtc.Should().Be(Start.AddMinutes(1)).And.Be(stored.NextFireTimeUtc);
        bundle.ScheduledFireTimeUtc.Should().Be(Start);
        bundle.NextFireTimeUtc.Should().Be(Start.AddMinutes(1));
    }

    [Test]
    public async Task TheStoreFiresBothInstancesOfADerivedTrigger()
    {
        TimeProvider clock = Clock();
        RAMJobStore store = TestJobStores.Ram(timeProvider: clock);
        await store.Initialize(TestJobStores.Identity());

        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job").StoreDurably().Build();
        await store.AddJob(job);

        CountingCronTrigger trigger = new(clock)
        {
            Key = new TriggerKey("trigger"),
            JobKey = job.Key,
            CronExpression = new CronExpression("0 0/1 * * * ?", TimeZoneInfo.Utc),
            StartTimeUtc = Start
        };
        trigger.ComputeFirstFireTimeUtc(null);
        await store.AddTrigger(trigger);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(
            new TriggerAcquisitionRequest { NoLaterThan = Start.AddMinutes(1), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);

        CountingCronTrigger copy = (CountingCronTrigger) fired.Should().ContainSingle().Which.TriggerFiredBundle!.Trigger;
        CountingCronTrigger stored = (CountingCronTrigger) (await store.GetTrigger(trigger.Key))!;

        copy.TriggeredCalls.Should().Be(1, "a trigger type of the application's own sees Triggered on the copy it is handed");
        stored.TriggeredCalls.Should().Be(1, "and on the instance the store keeps, whose state the next acquisition clones");
    }

    private static CronTriggerImpl Cron(string expression, TimeZoneInfo timeZone, int firedAlready, DateTimeOffset? end = null, DateTimeOffset? start = null)
    {
        CronTriggerImpl trigger = new(Clock())
        {
            Key = new TriggerKey("cron"),
            JobKey = new JobKey("job"),
            CronExpression = new CronExpression(expression, timeZone),
            StartTimeUtc = start ?? Start
        };

        trigger.EndTimeUtc = end;
        trigger.ComputeFirstFireTimeUtc(null);

        for (int i = 0; i < firedAlready && trigger.NextFireTimeUtc is not null; i++)
        {
            trigger.Triggered(null);
        }

        return trigger;
    }

    private static IOperableTrigger Clone(IOperableTrigger trigger) => (IOperableTrigger) trigger.Clone();

    /// <summary>
    /// Every instance field, the base types' private ones included, so a field that firing starts writing
    /// tomorrow fails here rather than going uncopied.
    /// </summary>
    private static void ShouldHaveTheSameFields(object actual, object expected, string because)
    {
        actual.GetType().Should().Be(expected.GetType());

        for (Type? type = actual.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                object? actualValue = field.GetValue(actual);
                object? expectedValue = field.GetValue(expected);

                bool same = actualValue is DateTimeOffset actualTime && expectedValue is DateTimeOffset expectedTime
                    ? actualTime.EqualsExact(expectedTime)
                    : Equals(actualValue, expectedValue);

                same.Should().BeTrue($"{type.Name}.{field.Name} is {actualValue ?? "null"} where {expectedValue ?? "null"} was expected, and {because}");
            }
        }
    }

    private sealed class CountingCalendar : BaseCalendar
    {
        public int Consulted { get; private set; }

        public override bool IsTimeIncluded(DateTimeOffset timeStampUtc)
        {
            Consulted++;
            return true;
        }

        public override ICalendar Clone() => this;
    }

    public sealed class CountingCronTrigger : CronTriggerImpl
    {
        public CountingCronTrigger(TimeProvider timeProvider) : base(timeProvider)
        {
        }

        public int TriggeredCalls { get; private set; }

        public override void Triggered(ICalendar? calendar)
        {
            TriggeredCalls++;
            base.Triggered(calendar);
        }
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
