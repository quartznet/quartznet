using System.Text.Json;

using Quartz.Dashboard.Components.Shared;

namespace Quartz.Tests.AspNetCore.Dashboard;

public class TriggerPayloadBuilderTest
{
    private static readonly JsonSerializerOptions serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// A quarter of a second into 12:00:00 on 1 June 2025, which is after the sample trigger's start.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2025, 6, 1, 12, 0, 0, 250, TimeSpan.Zero);

    /// <summary>
    /// The shape the Quartz converters write and the dashboard's API client hands the detail page.
    /// Every property is present; the unset ones are JSON null.
    /// </summary>
    private static JsonElement SerializedTrigger(
        object? description = null,
        object? calendarName = null,
        DateTimeOffset? startTimeUtc = null,
        DateTimeOffset? endTimeUtc = null)
    {
        return JsonSerializer.SerializeToElement(new
        {
            triggerType = "CronTrigger",
            key = new { name = "trigger1", group = "group1" },
            jobKey = new { name = "job1", group = "group1" },
            description,
            calendarName,
            jobDataMap = new Dictionary<string, object?> { ["colour"] = "green" },
            misfireInstruction = 0,
            startTimeUtc = startTimeUtc ?? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            endTimeUtc,
            priority = 5,
            nextFireTimeUtc = new DateTimeOffset(2025, 1, 2, 1, 0, 0, TimeSpan.Zero),
            previousFireTimeUtc = (DateTimeOffset?) null,
            executionGroup = "imports",
            preferredNode = "node-a",
            preferredNodeAuto = false,
            cronExpressionString = "0 0 1 * * ?",
            timeZone = "UTC"
        }, serializerOptions);
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/3294")]
    public void TryWithCronExpressionLeavesTextTheTriggerDoesNotHaveAsNull()
    {
        TriggerPayloadBuilder.TryWithCronExpression(SerializedTrigger(), "0 0 2 * * ?", Now, out JsonElement payload)
            .Should().BeTrue();

        payload.GetProperty("calendarName").ValueKind.Should().Be(JsonValueKind.Null,
            "an empty calendar name names a calendar that cannot be found, and the trigger then never fires again");
        payload.GetProperty("description").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public void TryWithCronExpressionCarriesEverythingElseThroughUntouched()
    {
        TriggerPayloadBuilder.TryWithCronExpression(
            SerializedTrigger(description: "nightly import", calendarName: "holidays"),
            "0 0 2 * * ?",
            Now,
            out JsonElement payload).Should().BeTrue();

        payload.GetProperty("cronExpressionString").GetString().Should().Be("0 0 2 * * ?",
            "the edited expression is the only thing a reschedule is meant to change");
        payload.GetProperty("calendarName").GetString().Should().Be("holidays");
        payload.GetProperty("description").GetString().Should().Be("nightly import");
        payload.GetProperty("executionGroup").GetString().Should().Be("imports");
        payload.GetProperty("preferredNode").GetString().Should().Be("node-a",
            "the hand-written payload dropped the node pin, which silently unpinned the trigger");
        payload.GetProperty("preferredNodeAuto").GetBoolean().Should().BeFalse();
        payload.GetProperty("jobDataMap").GetProperty("colour").GetString().Should().Be("green");
        payload.GetProperty("key").GetProperty("name").GetString().Should().Be("trigger1");
        payload.GetProperty("jobKey").GetProperty("group").GetString().Should().Be("group1");
        payload.GetProperty("priority").GetInt32().Should().Be(5);
        payload.GetProperty("timeZone").GetString().Should().Be("UTC");
    }

    [Test]
    public void TryWithCronExpressionClearsTheStoredNextFireTime()
    {
        TriggerPayloadBuilder.TryWithCronExpression(SerializedTrigger(), "0 0 2 * * ?", Now, out JsonElement payload)
            .Should().BeTrue();

        payload.GetProperty("nextFireTimeUtc").ValueKind.Should().Be(JsonValueKind.Null,
            "the stored time was computed from the old expression, and RescheduleJob honours a non-null one verbatim");
    }

    [Test]
    public void TryWithCronExpressionMatchesPropertyNamesWhateverTheirCasing()
    {
        JsonElement trigger = JsonSerializer.SerializeToElement(new
        {
            TriggerType = "CronTrigger",
            CalendarName = (string?) null,
            StartTimeUtc = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            NextFireTimeUtc = new DateTimeOffset(2025, 1, 2, 1, 0, 0, TimeSpan.Zero),
            CronExpressionString = "0 0 1 * * ?"
        });

        TriggerPayloadBuilder.TryWithCronExpression(trigger, "0 0 2 * * ?", Now, out JsonElement payload)
            .Should().BeTrue();

        payload.GetProperty("CronExpressionString").GetString().Should().Be("0 0 2 * * ?");
        payload.GetProperty("NextFireTimeUtc").ValueKind.Should().Be(JsonValueKind.Null);
        payload.GetProperty("StartTimeUtc").GetDateTimeOffset().Should().Be(new DateTimeOffset(2025, 6, 1, 12, 0, 1, TimeSpan.Zero));
    }

    /// <summary>
    /// https://github.com/quartznet/quartznet/issues/4187
    /// </summary>
    [Test]
    public void TryWithCronExpressionMovesAStartThatHasPassedToTheWholeSecondAfterNow()
    {
        TriggerPayloadBuilder.TryWithCronExpression(SerializedTrigger(), "0/15 * * * * ?", Now, out JsonElement payload)
            .Should().BeTrue();

        payload.GetProperty("startTimeUtc").GetDateTimeOffset().Should().Be(new DateTimeOffset(2025, 6, 1, 12, 0, 1, TimeSpan.Zero),
            "RescheduleJob fires a trigger first at its first match after its start, so a start long past gives a fire in the past, which the scheduler treats as a misfire");
    }

    [Test]
    public void TryWithCronExpressionMovesAStartThatHasPassedBeyondTheCurrentSecondWhenNowIsOnTheSecond()
    {
        DateTimeOffset onTheSecond = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

        TriggerPayloadBuilder.TryWithCronExpression(SerializedTrigger(), "0/15 * * * * ?", onTheSecond, out JsonElement payload)
            .Should().BeTrue();

        payload.GetProperty("startTimeUtc").GetDateTimeOffset().Should().Be(onTheSecond.AddSeconds(1),
            "the first fire is the first match after the start less a second, so a start of now is a fire in the current second, which the trigger may just have fired in");
    }

    [Test]
    public void TryWithCronExpressionKeepsAStartStillAhead()
    {
        DateTimeOffset start = new(2025, 7, 1, 0, 0, 0, TimeSpan.Zero);

        TriggerPayloadBuilder.TryWithCronExpression(SerializedTrigger(startTimeUtc: start), "0 0 2 * * ?", Now, out JsonElement payload)
            .Should().BeTrue();

        payload.GetProperty("startTimeUtc").GetDateTimeOffset().Should().Be(start, "a trigger does not fire before its start time");
    }

    [Test]
    public void TryWithCronExpressionKeepsTheStartOfATriggerThatHasEnded()
    {
        TriggerPayloadBuilder.TryWithCronExpression(
            SerializedTrigger(endTimeUtc: new DateTimeOffset(2025, 5, 1, 0, 0, 0, TimeSpan.Zero)),
            "0 0 2 * * ?",
            Now,
            out JsonElement payload).Should().BeTrue();

        payload.GetProperty("startTimeUtc").GetDateTimeOffset().Should().Be(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "a start after the end is refused, and a trigger that has ended has no fire for a later start to give it");
    }

    [Test]
    public void TryWithCronExpressionKeepsTheStartOfATriggerThatEndsBeforeTheNextWholeSecond()
    {
        TriggerPayloadBuilder.TryWithCronExpression(
            SerializedTrigger(endTimeUtc: Now.AddMilliseconds(500)),
            "0 0 2 * * ?",
            Now,
            out JsonElement payload).Should().BeTrue();

        payload.GetProperty("startTimeUtc").GetDateTimeOffset().Should().Be(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "the whole second after now is after the trigger's end, and a start after the end is refused");
    }

    [Test]
    public void TryWithCronExpressionRefusesATriggerItCannotIdentify()
    {
        // The API client falls back to reflection for trigger types the Quartz converters do not
        // know, and that output carries no discriminator. Posting it back would either fail to
        // deserialize or, as the hand-written payload did, reschedule it as some other type.
        JsonElement reflected = JsonSerializer.SerializeToElement(new
        {
            key = new { name = "trigger1", group = "group1" },
            cronExpressionString = "0 0 1 * * ?"
        }, serializerOptions);

        TriggerPayloadBuilder.TryWithCronExpression(reflected, "0 0 2 * * ?", Now, out JsonElement payload)
            .Should().BeFalse();
        payload.ValueKind.Should().Be(JsonValueKind.Undefined);
    }
}
