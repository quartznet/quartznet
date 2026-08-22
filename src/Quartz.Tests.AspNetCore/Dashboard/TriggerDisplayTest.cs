using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The one place the dashboard names a trigger's kind and summarises its schedule.
/// </summary>
/// <remarks>
/// Both API clients read it, which is the point: they used to describe the same trigger differently,
/// the HTTP-backed one echoing the wire's <c>CronTrigger</c> discriminator where the in-process one
/// said <c>Cron</c>.
/// </remarks>
public class TriggerDisplayTest
{
    [Test]
    public void TypeNameNamesEveryShippedKind()
    {
        TriggerDisplay.TypeName(Cron()).Should().Be("Cron");
        TriggerDisplay.TypeName(Simple(TimeSpan.FromSeconds(30), repeatCount: 2)).Should().Be("Simple");
        TriggerDisplay.TypeName(CalendarInterval()).Should().Be("Calendar interval");
        TriggerDisplay.TypeName(DailyTimeInterval()).Should().Be("Daily time interval");
    }

    [Test]
    public void TypeNameFallsBackToTheTypeItselfForAKindItDoesNotKnow()
    {
        ITrigger recurrence = TriggerBuilder.Create()
            .WithIdentity("recurrence", "group")
            .WithRecurrenceSchedule("FREQ=DAILY")
            .Build();

        TriggerDisplay.TypeName(recurrence).Should().Be(recurrence.GetType().Name,
            "a kind with no display name of its own is better named by its type than by nothing");
    }

    [Test]
    public void ScheduleSummaryForACronTriggerIsItsExpression()
    {
        TriggerDisplay.ScheduleSummary(Cron()).Should().Be("0 0 1 * * ?");
    }

    [Test]
    public void ScheduleSummaryForASimpleTriggerCountsItsRepeats()
    {
        TriggerDisplay.ScheduleSummary(Simple(TimeSpan.FromSeconds(30), repeatCount: 2))
            .Should().Be("Every 00:00:30, 2 time(s)");

        TriggerDisplay.ScheduleSummary(Simple(TimeSpan.FromMinutes(5), repeatCount: -1))
            .Should().Be("Every 00:05:00, repeat forever",
                "a negative repeat count is Quartz's spelling of 'forever', not a count to show");
    }

    [Test]
    public void ScheduleSummaryIsNullForAKindItCannotSummarise()
    {
        TriggerDisplay.ScheduleSummary(CalendarInterval()).Should().BeNull(
            "the listing shows a dash rather than a half-true summary");
        TriggerDisplay.ScheduleSummary(DailyTimeInterval()).Should().BeNull();
    }

    private static ITrigger Cron() => TriggerBuilder.Create()
        .WithIdentity("cron", "group")
        .WithCronSchedule("0 0 1 * * ?")
        .Build();

    private static ITrigger Simple(TimeSpan interval, int repeatCount) => TriggerBuilder.Create()
        .WithIdentity("simple", "group")
        .WithSimpleSchedule(x => x.WithInterval(interval).WithRepeatCount(repeatCount))
        .Build();

    private static ITrigger CalendarInterval() => TriggerBuilder.Create()
        .WithIdentity("calendar-interval", "group")
        .WithCalendarIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Day))
        .Build();

    private static ITrigger DailyTimeInterval() => TriggerBuilder.Create()
        .WithIdentity("daily-time-interval", "group")
        .WithDailyTimeIntervalSchedule(x => x.WithInterval(2, IntervalUnit.Hour))
        .Build();
}
