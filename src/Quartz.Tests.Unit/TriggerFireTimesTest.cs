using Quartz.Extensibility;

namespace Quartz.Tests.Unit;

public class TriggerFireTimesTest
{
    [Test]
    public void ComputeBetween_ShouldPreserveTriggerStartTime()
    {
        var startAt = DateTimeOffset.Parse("2026-01-01 08:00:00Z");
        var endAt = DateTimeOffset.Parse("2026-01-07 08:00:01Z");

        var trigger = (IOperableTrigger) TriggerBuilder.Create()
            .StartAt(startAt)
            .EndAt(endAt)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(24)).RepeatForever())
            .Build();

        // Query with 'from' 10 minutes earlier than trigger's start
        var from = DateTimeOffset.Parse("2026-01-01 07:50:00Z");
        var to = DateTimeOffset.Parse("2026-01-07 08:00:01Z");
        var fireTimes = TriggerFireTimes.ComputeBetween(trigger, null, from, to);

        // All fire times should be at 08:00, not at 07:50
        Assert.That(fireTimes.Count, Is.EqualTo(7));
        foreach (var fireTime in fireTimes)
        {
            Assert.That(fireTime.Hour, Is.EqualTo(8));
            Assert.That(fireTime.Minute, Is.EqualTo(0));
        }
    }

    [Test]
    public void ComputeBetween_MatchingFromAndStart_WorksCorrectly()
    {
        var startAt = DateTimeOffset.Parse("2026-01-01 08:00:00Z");
        var endAt = DateTimeOffset.Parse("2026-01-07 08:00:01Z");

        var trigger = (IOperableTrigger) TriggerBuilder.Create()
            .StartAt(startAt)
            .EndAt(endAt)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(24)).RepeatForever())
            .Build();

        var from = DateTimeOffset.Parse("2026-01-01 08:00:00Z");
        var to = DateTimeOffset.Parse("2026-01-07 08:00:01Z");
        var fireTimes = TriggerFireTimes.ComputeBetween(trigger, null, from, to);

        Assert.That(fireTimes.Count, Is.EqualTo(7));
        Assert.That(fireTimes[0], Is.EqualTo(startAt));
    }

    /// <summary>
    /// The three <see cref="ITrigger" /> overloads answer what their <see cref="IOperableTrigger" />
    /// twins answer, so a test can stop opening with a cast to a <c>Quartz.Extensibility</c> type.
    /// </summary>
    [Test]
    public void TheITriggerOverloadsAgreeWithTheOnesThatTakeAnOperableTrigger()
    {
        DateTimeOffset startAt = DateTimeOffset.Parse("2026-01-01 08:00:00Z");
        DateTimeOffset to = DateTimeOffset.Parse("2026-01-04 08:00:01Z");

        ITrigger trigger = TriggerBuilder.Create()
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(24)).RepeatForever())
            .Build();

        IOperableTrigger operable = (IOperableTrigger) trigger;

        TriggerFireTimes.Compute(trigger, calendar: null, numberOfTimes: 3)
            .Should().Equal(TriggerFireTimes.Compute(operable, calendar: null, numberOfTimes: 3));

        TriggerFireTimes.ComputeBetween(trigger, calendar: null, startAt, to)
            .Should().Equal(TriggerFireTimes.ComputeBetween(operable, calendar: null, startAt, to));

        TriggerFireTimes.ComputeEndTimeForCount(trigger, calendar: null, numberOfTimes: 3)
            .Should().Be(TriggerFireTimes.ComputeEndTimeForCount(operable, calendar: null, numberOfTimes: 3));
    }

    /// <summary>
    /// A trigger that is only an <see cref="ITrigger" /> cannot be advanced, and is told so by name.
    /// </summary>
    /// <remarks>
    /// Answering means walking a clone through its schedule and applying the calendar at each step,
    /// which is exactly what <see cref="IOperableTrigger" /> adds. Without this the cast inside would
    /// surface as an <see cref="InvalidCastException" /> from a member the caller never named.
    /// </remarks>
    [Test]
    public void ATriggerThatCannotBeAdvancedIsRefusedByName()
    {
        ITrigger trigger = new OpaqueTrigger();

        Action compute = () => TriggerFireTimes.Compute(trigger, calendar: null, numberOfTimes: 1);

        compute.Should().Throw<ArgumentException>()
            .WithMessage("*IOperableTrigger*", "the message has to name the contract that is missing")
            .WithMessage($"*{nameof(OpaqueTrigger)}*", "and the type that does not implement it")
            .And.ParamName.Should().Be("trigger");
    }

#nullable enable

    /// <summary>
    /// An <see cref="ITrigger" /> of somebody else's, implemented only far enough to be one.
    /// </summary>
    private sealed class OpaqueTrigger : ITrigger
    {
        public TriggerKey Key { get; } = new("opaque");
        public JobKey JobKey { get; } = new("job");
        public string? Description => null;
        public string? ExecutionGroup => null;
        public PreferredNode PreferredNode => default;
        public RetryPolicy? RetryPolicy => null;
        public int RetryAttempt => 0;
        public string? CalendarName => null;
        public JobDataMap JobDataMap { get; } = [];
        public DateTimeOffset? FinalFireTimeUtc => null;
        public int MisfireInstructionCode => 0;
        public DateTimeOffset? EndTimeUtc => null;
        public DateTimeOffset StartTimeUtc => DateTimeOffset.UnixEpoch;
        public int Priority => 5;
        public bool MayFireAgain => false;
        public DateTimeOffset? NextFireTimeUtc => null;
        public DateTimeOffset? PreviousFireTimeUtc => null;
        public DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime) => null;
        public ITrigger Clone() => this;
        public IScheduleBuilder GetScheduleBuilder() => throw new NotSupportedException();
        public TriggerBuilder<IJob> GetTriggerBuilder() => throw new NotSupportedException();
        public int CompareTo(ITrigger? other) => 0;
    }

#nullable restore
}
