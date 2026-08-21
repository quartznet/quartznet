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
    /// The shape the Quartz converters write and the dashboard's API client hands the detail page.
    /// Every property is present; the unset ones are JSON null.
    /// </summary>
    private static JsonElement SerializedTrigger(object? description = null, object? calendarName = null)
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
            startTimeUtc = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            endTimeUtc = (DateTimeOffset?) null,
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
        TriggerPayloadBuilder.TryWithCronExpression(SerializedTrigger(), "0 0 2 * * ?", out JsonElement payload)
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
        TriggerPayloadBuilder.TryWithCronExpression(SerializedTrigger(), "0 0 2 * * ?", out JsonElement payload)
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
            NextFireTimeUtc = new DateTimeOffset(2025, 1, 2, 1, 0, 0, TimeSpan.Zero),
            CronExpressionString = "0 0 1 * * ?"
        });

        TriggerPayloadBuilder.TryWithCronExpression(trigger, "0 0 2 * * ?", out JsonElement payload)
            .Should().BeTrue();

        payload.GetProperty("CronExpressionString").GetString().Should().Be("0 0 2 * * ?");
        payload.GetProperty("NextFireTimeUtc").ValueKind.Should().Be(JsonValueKind.Null);
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

        TriggerPayloadBuilder.TryWithCronExpression(reflected, "0 0 2 * * ?", out JsonElement payload)
            .Should().BeFalse();
        payload.ValueKind.Should().Be(JsonValueKind.Undefined);
    }
}
