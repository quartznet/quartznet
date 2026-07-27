using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

/// <summary>
/// <c>GetNextFireTimeUtc()</c>, <c>GetPreviousFireTimeUtc()</c> and <c>GetMayFireAgain()</c> survive
/// only as forwarders to the properties that replaced them. They exist twice — as default
/// implementations on <see cref="ITrigger" /> and as concrete methods on
/// <see cref="AbstractTrigger" /> — so both copies have to answer exactly what the property answers,
/// or code that has not migrated yet quietly reads a different schedule.
/// </summary>
public class ObsoleteTriggerForwardersTest
{
    private static readonly DateTimeOffset StartTime = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void ConcreteForwardersAgreeWithThePropertiesAtEveryStageOfTheSchedule()
    {
        // An explicit start time: a builder-defaulted one would make the expectations below depend on
        // the clock rather than on the forwarders.
        SimpleTriggerImpl trigger = new SimpleTriggerImpl(
            "forwarders",
            "group",
            StartTime,
            endTimeUtc: null,
            repeatCount: 1,
            repeatInterval: TimeSpan.FromHours(1));

        // Nothing has been computed yet: no fire times, and no possibility of firing.
        AssertForwardersAgreeWithProperties(trigger);
        trigger.GetNextFireTimeUtc().Should().BeNull("no fire time has been computed yet");
        trigger.GetPreviousFireTimeUtc().Should().BeNull("the trigger has never fired");
        trigger.GetMayFireAgain().Should().BeFalse("a trigger with no next fire time cannot fire again");

        trigger.ComputeFirstFireTimeUtc(calendar: null).Should().Be(StartTime, "the first fire time is the start time");

        AssertForwardersAgreeWithProperties(trigger);
        trigger.GetNextFireTimeUtc().Should().Be(StartTime, "the computed first fire time is what the property reports");
        trigger.GetPreviousFireTimeUtc().Should().BeNull("computing a fire time is not firing");
        trigger.GetMayFireAgain().Should().BeTrue("there is a next fire time");

        trigger.Triggered(calendar: null);

        AssertForwardersAgreeWithProperties(trigger);
        trigger.GetPreviousFireTimeUtc().Should().Be(StartTime, "the first firing became the previous fire time");
        trigger.GetNextFireTimeUtc().Should().Be(StartTime.AddHours(1), "one repeat remains, an hour later");
        trigger.GetMayFireAgain().Should().BeTrue("the repeat has not happened yet");

        trigger.Triggered(calendar: null);

        AssertForwardersAgreeWithProperties(trigger);
        trigger.GetPreviousFireTimeUtc().Should().Be(StartTime.AddHours(1), "the repeat became the previous fire time");
        trigger.GetNextFireTimeUtc().Should().BeNull("the repeat count is exhausted");
        trigger.GetMayFireAgain().Should().BeFalse("an exhausted trigger cannot fire again");
    }

    [Test]
    public void DefaultInterfaceForwardersAgreeWithTheProperties()
    {
        // A trigger that only implements the properties reaches the forwarders through ITrigger's
        // default implementations, which is a different piece of code from AbstractTrigger's methods.
        typeof(MinimalTrigger).GetMethod(nameof(ITrigger.GetNextFireTimeUtc), Type.EmptyTypes).Should().BeNull(
            "this implementation must not redeclare the forwarders, or the default interface methods are never exercised");

        MinimalTrigger minimal = new MinimalTrigger
        {
            NextFireTimeUtc = StartTime.AddHours(2),
            PreviousFireTimeUtc = StartTime,
            MayFireAgain = true,
        };

        ITrigger trigger = minimal;

        AssertForwardersAgreeWithProperties(trigger);
        trigger.GetNextFireTimeUtc().Should().Be(StartTime.AddHours(2), "the default implementation reads the property");
        trigger.GetPreviousFireTimeUtc().Should().Be(StartTime, "the default implementation reads the property");
        trigger.GetMayFireAgain().Should().BeTrue("the default implementation reads the property");

        // The other end of the range: nulls and false have to travel through the forwarders unchanged.
        minimal.NextFireTimeUtc = null;
        minimal.PreviousFireTimeUtc = null;
        minimal.MayFireAgain = false;

        AssertForwardersAgreeWithProperties(trigger);
        trigger.GetNextFireTimeUtc().Should().BeNull("a null next fire time must not become anything else on the way out");
        trigger.GetPreviousFireTimeUtc().Should().BeNull("a null previous fire time must not become anything else on the way out");
        trigger.GetMayFireAgain().Should().BeFalse("the forwarder must not invert or default the answer");
    }

    private static void AssertForwardersAgreeWithProperties(ITrigger trigger)
    {
        trigger.GetNextFireTimeUtc().Should().Be(trigger.NextFireTimeUtc,
            "GetNextFireTimeUtc() only exists to forward to NextFireTimeUtc");
        trigger.GetPreviousFireTimeUtc().Should().Be(trigger.PreviousFireTimeUtc,
            "GetPreviousFireTimeUtc() only exists to forward to PreviousFireTimeUtc");
        trigger.GetMayFireAgain().Should().Be(trigger.MayFireAgain,
            "GetMayFireAgain() only exists to forward to MayFireAgain");
    }

    private static void AssertForwardersAgreeWithProperties(AbstractTrigger trigger)
    {
        // Through the concrete type, where the interface's default implementations are unreachable.
        trigger.GetNextFireTimeUtc().Should().Be(trigger.NextFireTimeUtc,
            "the concrete forwarder has to answer what the property answers");
        trigger.GetPreviousFireTimeUtc().Should().Be(trigger.PreviousFireTimeUtc,
            "the concrete forwarder has to answer what the property answers");
        trigger.GetMayFireAgain().Should().Be(trigger.MayFireAgain,
            "the concrete forwarder has to answer what the property answers");

        // And through the interface, which for this type dispatches to the same concrete methods.
        AssertForwardersAgreeWithProperties((ITrigger) trigger);
    }

    /// <summary>
    /// The smallest thing that is an <see cref="ITrigger" />. Only the three properties the
    /// forwarders read are meaningful; everything else exists to satisfy the compiler.
    /// </summary>
    private sealed class MinimalTrigger : ITrigger
    {
        public TriggerKey Key { get; } = new TriggerKey("minimal", "group");

        public JobKey JobKey { get; } = new JobKey("job", "group");

        public TriggerBuilder GetTriggerBuilder() => throw new NotSupportedException();

        public IScheduleBuilder GetScheduleBuilder() => throw new NotSupportedException();

        public string Description => null;

        public string ExecutionGroup => null;

        public string PreferredNode => null;

        public bool IsPreferredNodeAuto => false;

        public string CalendarName => null;

        public JobDataMap JobDataMap { get; } = new JobDataMap();

        public DateTimeOffset? FinalFireTimeUtc => null;

        public int MisfireInstruction => Quartz.MisfireInstruction.InstructionNotSet;

        public DateTimeOffset? EndTimeUtc => null;

        public DateTimeOffset StartTimeUtc => StartTime;

        public int Priority { get; set; } = TriggerConstants.DefaultPriority;

        public bool MayFireAgain { get; set; }

        public DateTimeOffset? NextFireTimeUtc { get; set; }

        public DateTimeOffset? PreviousFireTimeUtc { get; set; }

        public DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime) => null;

        public bool HasMillisecondPrecision => true;

        public ITrigger Clone() => (ITrigger) MemberwiseClone();
    }
}
