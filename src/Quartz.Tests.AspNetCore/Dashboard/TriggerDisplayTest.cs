using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The one place the dashboard names a trigger's kind.
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
    public void TypeNameNamesARecurrenceTrigger()
    {
        ITrigger recurrence = TriggerBuilder.Create()
            .WithIdentity("recurrence", "group")
            .WithRecurrenceSchedule("FREQ=DAILY")
            .Build();

        TriggerDisplay.TypeName(recurrence).Should().Be("Recurrence");
    }

    [Test]
    public void TypeNameFallsBackToTheTypeItselfForAKindItDoesNotKnow()
    {
        ITrigger own = FakeItEasy.A.Fake<ITrigger>();

        TriggerDisplay.TypeName(own).Should().Be(own.GetType().Name,
            "a kind with no display name of its own is better named by its type than by nothing");
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
