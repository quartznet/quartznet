using Quartz.Spi;

namespace Quartz.Tests.Unit;

public class TriggerUtilsTest
{
    [Test]
    public void ComputeFireTimesBetween_ShouldPreserveTriggerStartTime()
    {
        var startAt = DateTimeOffset.Parse("2026-01-01 08:00:00Z");
        var endAt = DateTimeOffset.Parse("2026-01-07 08:00:01Z");

        var trigger = (IOperableTrigger) TriggerBuilder.Create()
            .StartAt(startAt)
            .EndAt(endAt)
            .WithSimpleSchedule(x => x.WithIntervalInHours(24).RepeatForever())
            .Build();

        // Query with 'from' 10 minutes earlier than trigger's start
        var from = DateTimeOffset.Parse("2026-01-01 07:50:00Z");
        var to = DateTimeOffset.Parse("2026-01-07 08:00:01Z");
        var fireTimes = TriggerUtils.ComputeFireTimesBetween(trigger, null, from, to);

        // All fire times should be at 08:00, not at 07:50
        Assert.That(fireTimes.Count, Is.EqualTo(7));
        foreach (var fireTime in fireTimes)
        {
            Assert.That(fireTime.Hour, Is.EqualTo(8));
            Assert.That(fireTime.Minute, Is.EqualTo(0));
        }
    }

    [Test]
    public void ComputeFireTimesBetween_MatchingFromAndStart_WorksCorrectly()
    {
        var startAt = DateTimeOffset.Parse("2026-01-01 08:00:00Z");
        var endAt = DateTimeOffset.Parse("2026-01-07 08:00:01Z");

        var trigger = (IOperableTrigger) TriggerBuilder.Create()
            .StartAt(startAt)
            .EndAt(endAt)
            .WithSimpleSchedule(x => x.WithIntervalInHours(24).RepeatForever())
            .Build();

        var from = DateTimeOffset.Parse("2026-01-01 08:00:00Z");
        var to = DateTimeOffset.Parse("2026-01-07 08:00:01Z");
        var fireTimes = TriggerUtils.ComputeFireTimesBetween(trigger, null, from, to);

        Assert.That(fireTimes.Count, Is.EqualTo(7));
        Assert.That(fireTimes[0], Is.EqualTo(startAt));
    }
}
