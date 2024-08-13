using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl.Triggers;

[TestFixture]
public class SimpleTriggerImplTest
{
    private readonly Random _random;

    public SimpleTriggerImplTest()
    {
        _random = new Random();
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsNull_StartTimeUtcIsMinValue_RepeatCountIsZero_TimesTriggeredEqualsRepeatCount_EndTimeUtcIsNull()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = null,
            RepeatCount = 0,
            StartTimeUtc = DateTimeOffset.MinValue,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        DateTimeOffset? afterTimeUtc = null;

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);
        Assert.That(actual, Is.Null);
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsNull_StartTimeUtcIsBeforeNow_RepeatCountIsZero_TimesTriggeredIsGreaterThanRepeatCount_EndTimeUtcIsNull()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));
        DateTimeOffset? endTimeUtc = null;
        DateTimeOffset? afterTimeUtc = null;

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = endTimeUtc,
            RepeatCount = 0,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 1
        };

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsGreaterThanStartTimeUtc_RepeatCountIsRepeatIndefinitely_EndTimeUtcIsNull()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));
        DateTimeOffset? endTimeUtc = null;
        var afterTimeUtc = startTimeUtc.AddDays(1);

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = endTimeUtc,
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Not.Null);
        Assert.That(actual, Is.EqualTo(startTimeUtc.AddDays(2)));
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsGreaterThanStartTimeUtc_RepeatCountIsRepeatIndefinitely_EndTimeUtcEqualsAfterTimeUtc()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));
        var endTimeUtc = startTimeUtc.AddDays(1);
        var afterTimeUtc = endTimeUtc;

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = endTimeUtc,
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };


        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsGreaterThanStartTimeUtc_RepeatCountIsRepeatIndefinitely_EndTimeUtcIsGreaterThanAfterTimeUtc()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));
        var afterTimeUtc = startTimeUtc.AddDays(5);
        var endTimeUtc = afterTimeUtc.AddDays(1);

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = endTimeUtc,
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsGreaterThanStartTimeUtc_RepeatCountIsRepeatIndefinitely_EndTimeUtcIsLessThanAfterTimeUtc()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));
        var endTimeUtc = startTimeUtc.AddDays(1);
        var afterTimeUtc = endTimeUtc.AddDays(5);

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = endTimeUtc,
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsGreaterThanStartTimeUtc_RepeatCountIsZero_TimesTriggeredEqualsRepeatCount_EndTimeUtcIsNull()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));
        DateTimeOffset? endTimeUtc = null;
        var afterTimeUtc = startTimeUtc.AddDays(5);

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = endTimeUtc,
            RepeatCount = 0,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void GetFireTimeAfter_EndTimeUtcIsLessThanAfterTimeUtc_RepeatCountIsGreaterThanZero_TimesTriggeredIsLessThanRepeatCount()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));
        var endTimeUtc = startTimeUtc.AddMinutes(5);
        var afterTimeUtc = endTimeUtc.AddDays(1);

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = startTimeUtc.AddMinutes(5),
            RepeatCount = 1,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsLessThanStartTimeUtc_RepeatCountIsGreaterThanZero_TimesTriggeredIsLessThanRepeatCount_EndTimeUtcIsNull()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = null,
            RepeatCount = 1,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        DateTimeOffset? afterTimeUtc = startTimeUtc.AddMinutes(-1);

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Not.Null);
        Assert.That(actual, Is.EqualTo(startTimeUtc));
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsLessThanStartTimeUtc_RepeatCountIsGreaterThanZero_TimesTriggeredEqualsRepeatCount_EndTimeUtcIsNull()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = null,
            RepeatCount = 1,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 1
        };

        DateTimeOffset? afterTimeUtc = startTimeUtc.AddMinutes(-1);

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Not.Null);
        Assert.That(actual, Is.EqualTo(startTimeUtc));
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsLessThanStartTimeUtc_RepeatCountIsGreaterThanZero_TimesTriggeredIsGreaterThanRepeatCount_EndTimeUtcIsNull()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = null,
            RepeatCount = 1,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 2
        };

        DateTimeOffset? afterTimeUtc = startTimeUtc.AddMinutes(-1);

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsGreaterThanStartTimeUtc_RepeatCountIsGreaterThanZero_TimesTriggeredIsLessThanRepeatCount_EndTimeUtcIsNull_CalculatedNumberOfTimesExecutedIsGreaterThanRepeatCount()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = null,
            RepeatCount = 2,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        DateTimeOffset? afterTimeUtc = startTimeUtc.AddDays(4);

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsGreaterThanStartTimeUtc_RepeatCountIsGreaterThanZero_TimesTriggeredIsLessThanRepeatCount_EndTimeUtcIsNull_CalculatedNumberOfTimesExecutedIsLessThanRepeatCount()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = null,
            RepeatCount = 2,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        DateTimeOffset? afterTimeUtc = startTimeUtc.AddMinutes(1);

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Not.Null);
        Assert.That(actual, Is.EqualTo(startTimeUtc.Add(TimeSpan.FromDays(1))));
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsGreaterThanStartTimeUtc_RepeatCountIsGreaterThanZero_TimesTriggeredIsLessThanRepeatCount_EndTimeUtcIsNull_CalculatedNumberOfTimesExecutedEqualsThanRepeatCount()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = null,
            RepeatCount = 2,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        DateTimeOffset? afterTimeUtc = startTimeUtc.AddDays(1);

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Not.Null);
        Assert.That(actual, Is.EqualTo(startTimeUtc.Add(TimeSpan.FromDays(2))));
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsGreaterThanStartTimeUtc_RepeatCountIsGreaterThanZero_TimesTriggeredIsLessThanRepeatCount_CalculatedNumberOfTimesExecutedEqualsThanRepeatCount_CalculatedTimeIsGreaterThanEndTimeUtc()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = startTimeUtc.AddDays(1).AddMinutes(5),
            RepeatCount = 2,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        DateTimeOffset? afterTimeUtc = startTimeUtc.AddDays(1);

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsGreaterThanStartTimeUtc_RepeatCountIsGreaterThanZero_TimesTriggeredIsLessThanRepeatCount_CalculatedNumberOfTimesExecutedEqualsThanRepeatCount_CalculatedTimeIsLessThanEndTimeUtc()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = startTimeUtc.AddDays(3),
            RepeatCount = 2,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        DateTimeOffset? afterTimeUtc = startTimeUtc.AddDays(1);

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Not.Null);
        Assert.That(actual, Is.EqualTo(startTimeUtc.AddDays(2)));
    }

    [Test]
    public void GetFireTimeAfter_AfterTimeUtcIsGreaterThanStartTimeUtc_RepeatCountIsGreaterThanZero_TimesTriggeredIsLessThanRepeatCount_CalculatedNumberOfTimesExecutedEqualsThanRepeatCount_CalculatedTimeEqualsEndTimeUtc()
    {
        var startTimeUtc = DateTimeOffset.MinValue.AddDays(_random.Next(1, 10));

        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            EndTimeUtc = startTimeUtc.AddDays(2),
            RepeatCount = 2,
            StartTimeUtc = startTimeUtc,
            RepeatInterval = TimeSpan.FromDays(1),
            TimesTriggered = 0
        };

        DateTimeOffset? afterTimeUtc = startTimeUtc.AddDays(1);

        var actual = trigger.GetFireTimeAfter(afterTimeUtc);

        Assert.That(actual, Is.Null);
    }
}