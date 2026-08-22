using Quartz.Dashboard.Components.Shared;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.AspNetCore.Dashboard;

public class TriggerPayloadBuilderTest
{
    /// <summary>
    /// A trigger as the API client hands the detail page: every field populated, including the ones
    /// the page never displays.
    /// </summary>
    private static ITrigger CronTrigger(string? description = null, string? calendarName = null)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .ForJob("job1", "group1")
            .WithDescription(description)
            .WithCalendarName(calendarName)
            .UsingJobData("colour", "green")
            .StartAt(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero))
            .WithPriority(5)
            .WithExecutionGroup("imports")
            .WithPreferredNode(PreferredNode.For("node-a"))
            .WithCronSchedule("0 0 1 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc))
            .Build();

        ((CronTriggerImpl) trigger).NextFireTimeUtc = new DateTimeOffset(2025, 1, 2, 1, 0, 0, TimeSpan.Zero);
        return trigger;
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/3294")]
    public void TryWithCronExpressionLeavesTextTheTriggerDoesNotHaveAsNull()
    {
        TriggerPayloadBuilder.TryWithCronExpression(CronTrigger(), "0 0 2 * * ?", out ITrigger? payload)
            .Should().BeTrue();

        payload!.CalendarName.Should().BeNull(
            "an empty calendar name names a calendar that cannot be found, and the trigger then never fires again");
        payload.Description.Should().BeNull();
    }

    [Test]
    public void TryWithCronExpressionCarriesEverythingElseThroughUntouched()
    {
        TriggerPayloadBuilder.TryWithCronExpression(
            CronTrigger(description: "nightly import", calendarName: "holidays"),
            "0 0 2 * * ?",
            out ITrigger? payload).Should().BeTrue();

        payload.Should().BeOfType<CronTriggerImpl>("the edit must not change what kind of trigger this is");

        CronTriggerImpl cron = (CronTriggerImpl) payload!;
        cron.CronExpressionString.Should().Be("0 0 2 * * ?",
            "the edited expression is the only thing a reschedule is meant to change");
        cron.TimeZone.Should().Be(TimeZoneInfo.Utc, "the expression is resolved in the zone it was written for");
        cron.CalendarName.Should().Be("holidays");
        cron.Description.Should().Be("nightly import");
        cron.ExecutionGroup.Should().Be("imports");
        cron.PreferredNode.Node.Should().Be("node-a",
            "the hand-written payload dropped the node pin, which silently unpinned the trigger");
        cron.PreferredNode.IsAutomatic.Should().BeFalse();
        cron.JobDataMap["colour"].Should().Be("green");
        cron.Key.Should().Be(new TriggerKey("trigger1", "group1"));
        cron.JobKey.Should().Be(new JobKey("job1", "group1"));
        cron.Priority.Should().Be(5);
        cron.StartTimeUtc.Should().Be(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public void TryWithCronExpressionClearsTheStoredNextFireTime()
    {
        TriggerPayloadBuilder.TryWithCronExpression(CronTrigger(), "0 0 2 * * ?", out ITrigger? payload)
            .Should().BeTrue();

        payload!.NextFireTimeUtc.Should().BeNull(
            "the stored time was computed from the old expression, and RescheduleJob honours a non-null one verbatim");
    }

    [Test]
    public void TryWithCronExpressionDoesNotTouchTheTriggerItWasGiven()
    {
        ITrigger original = CronTrigger();

        TriggerPayloadBuilder.TryWithCronExpression(original, "0 0 2 * * ?", out ITrigger? payload).Should().BeTrue();

        ((ICronTrigger) original).CronExpressionString.Should().Be("0 0 1 * * ?",
            "the page still shows the trigger it loaded until the reschedule comes back");
        original.NextFireTimeUtc.Should().NotBeNull();
        payload.Should().NotBeSameAs(original);
    }

    [Test]
    public void TryWithCronExpressionRefusesATriggerItCannotRebuild()
    {
        // A cron schedule this cannot set without knowing the type. Guessing means posting back some
        // other trigger than the one on screen, which is how a custom cron trigger used to be
        // rewritten as a plain one.
        ITrigger simple = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .Build();

        TriggerPayloadBuilder.TryWithCronExpression(simple, "0 0 2 * * ?", out ITrigger? payload).Should().BeFalse();
        payload.Should().BeNull();
    }
}
