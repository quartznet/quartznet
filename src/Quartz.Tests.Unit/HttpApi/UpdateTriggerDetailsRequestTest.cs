using System.Text.Json;

using AwesomeAssertions.Execution;

using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// The trigger-details patch, on its own. It is the one body with a converter of its own, because it is
/// the one body where an absent member and a null member have to mean different things.
/// </summary>
public class UpdateTriggerDetailsRequestTest
{
    private static readonly JsonSerializerOptions wireOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

    [Test]
    public void ABodyWithNoMembersUpdatesNothing()
    {
        TriggerDetailsUpdate update = Read("{}").AsUpdate();

        using (new AssertionScope())
        {
            update.HasDescription.Should().BeFalse();
            update.HasPriority.Should().BeFalse();
            update.HasJobDataMap.Should().BeFalse();
            update.HasCalendarName.Should().BeFalse();
            update.HasMisfireInstruction.Should().BeFalse();
            update.HasPreferredNode.Should().BeFalse();
            update.HasExecutionGroup.Should().BeFalse();
            update.HasRetryPolicy.Should().BeFalse();
        }
    }

    /// <summary>
    /// The distinction the converter exists for: the same member, absent and null.
    /// </summary>
    [TestCase("""{"description":null}""", true, TestName = "A description sent as null clears it")]
    [TestCase("""{"priority":1}""", false, TestName = "A description never sent is left alone")]
    public void NullIsAValueAndAbsenceIsNot(string body, bool expectedHasDescription)
    {
        Read(body).AsUpdate().HasDescription.Should().Be(expectedHasDescription,
            "a body of nullable members could not tell these two apart, and they mean opposite things");
    }

    [Test]
    public void EveryMemberIsReadIntoTheUpdate()
    {
        const string body = """
            {
              "description": "nightly export",
              "priority": 8,
              "jobDataMap": { "region": "eu-west" },
              "calendarName": "HolidayCalendar",
              "misfireInstruction": 2,
              "misfireInstructionFamily": "Cron",
              "preferredNode": "node-a",
              "preferredNodeAuto": false,
              "executionGroup": "imports",
              "retryPolicy": "fixed;3;00:00:30"
            }
            """;

        TriggerDetailsUpdate update = Read(body).AsUpdate();

        using (new AssertionScope())
        {
            update.Description.Should().Be("nightly export");
            update.Priority.Should().Be(8);
            update.JobDataMap.Should().ContainKey("region").WhoseValue.Should().Be("eu-west");
            update.CalendarName.Should().Be("HolidayCalendar");
            update.MisfireInstructionCode.Should().Be(2);
            update.MisfireInstructionFamily.Should().Be(TriggerFamily.Cron);
            update.PreferredNode.Should().Be(PreferredNode.For("node-a"));
            update.ExecutionGroup.Should().Be("imports");
            update.RetryPolicy.Should().Be(RetryPolicy.Fixed(3, TimeSpan.FromSeconds(30)));
        }
    }

    /// <summary>
    /// A misfire instruction with no family named is the bare code, which is the only form that reaches
    /// a trigger implementation outside the five built-in families.
    /// </summary>
    [Test]
    public void AMisfireInstructionWithNoFamilyIsABareCode()
    {
        TriggerDetailsUpdate update = Read("""{"misfireInstruction":2}""").AsUpdate();

        update.HasMisfireInstruction.Should().BeTrue();
        update.MisfireInstructionFamily.Should().BeNull(
            "naming no family is what asks the store to skip the family check, not an omission to guess at");
    }

    /// <summary>
    /// Every family has a wire spelling, and it is not the internal enum's <c>ToString</c> — the
    /// spellings are written down so that renaming a member cannot quietly rename a wire value.
    /// </summary>
    [Test]
    public void EveryFamilyHasAWireSpelling()
    {
        Dictionary<string, TriggerFamily> spellings = new(StringComparer.Ordinal)
        {
            ["Simple"] = TriggerFamily.Simple,
            ["Cron"] = TriggerFamily.Cron,
            ["CalendarInterval"] = TriggerFamily.CalendarInterval,
            ["DailyTimeInterval"] = TriggerFamily.DailyTimeInterval,
            ["Recurrence"] = TriggerFamily.Recurrence
        };

        spellings.Keys.Should().BeEquivalentTo(Enum.GetNames<TriggerFamily>(),
            "a family with no wire spelling is a family an HTTP caller cannot state, and the check the store "
            + "would then skip is the one that stops the wrong policy being applied");

        using (new AssertionScope())
        {
            foreach ((string wireName, TriggerFamily expected) in spellings)
            {
                Read($$"""{"misfireInstruction":0,"misfireInstructionFamily":"{{wireName}}"}""")
                    .AsUpdate().MisfireInstructionFamily.Should().Be(expected);
            }
        }
    }

    [Test]
    public void AnUnknownFamilyIsAValidationFailureRatherThanADroppedField()
    {
        Read("""{"misfireInstruction":2,"misfireInstructionFamily":"Weekly"}""").Validate()
            .Should().ContainSingle().Which.Should().Contain("Weekly");
    }

    [Test]
    public void AFamilyWithNoInstructionIsAValidationFailure()
    {
        Read("""{"misfireInstructionFamily":"Cron"}""").Validate()
            .Should().ContainSingle().Which.Should().Contain("without a misfire instruction");
    }

    [Test]
    public void AnUnparseableRetryPolicyIsAValidationFailure()
    {
        Read("""{"retryPolicy":"not-a-policy"}""").Validate()
            .Should().ContainSingle().Which.Should().Contain("not-a-policy",
                "an unreadable policy would otherwise arrive as 'stop retrying', which is the opposite of what was asked");
    }

    [Test]
    public void ClearingTheRetryPolicyIsNotAValidationFailure()
    {
        UpdateTriggerDetailsRequest request = Read("""{"retryPolicy":null}""");

        request.Validate().Should().BeEmpty();
        request.AsUpdate().HasRetryPolicy.Should().BeTrue();
        request.AsUpdate().RetryPolicy.Should().BeNull();
    }

    [TestCase("""{"description":42}""", TestName = "A member that should be text is refused")]
    [TestCase("""{"priority":"eight"}""", TestName = "A member that should be a number is refused")]
    [TestCase("""{"jobDataMap":"not-a-map"}""", TestName = "A member that should be an object is refused")]
    [TestCase("[]", TestName = "A body that is not an object is refused")]
    public void AMemberOfTheWrongKindIsRefusedAsBadJson(string body)
    {
        Action read = () => Read(body);

        read.Should().Throw<JsonException>(
            "a malformed body is a 400, and JsonElement's own InvalidOperationException would be a 500");
    }

    /// <summary>
    /// The whole round trip, which is what the client and the server each do one half of.
    /// </summary>
    [Test]
    public void AnUpdateSurvivesTheRoundTrip()
    {
        TriggerDetailsUpdate original = new TriggerDetailsUpdate()
            .WithDescription(null)
            .WithPriority(8)
            .WithJobDataMap(new JobDataMap { { "region", "eu-west" } })
            .WithCalendarName("HolidayCalendar")
            .WithMisfireInstruction(DailyTimeIntervalTriggerMisfireInstruction.DoNothing)
            .WithPreferredNode(PreferredNode.Auto)
            .WithExecutionGroup("imports")
            .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromSeconds(30)));

        string json = JsonSerializer.Serialize(UpdateTriggerDetailsRequest.Create(original), wireOptions);
        TriggerDetailsUpdate roundTripped = Read(json).AsUpdate();

        using (new AssertionScope())
        {
            roundTripped.HasDescription.Should().BeTrue();
            roundTripped.Description.Should().BeNull();
            roundTripped.Priority.Should().Be(8);
            roundTripped.JobDataMap.Should().ContainKey("region").WhoseValue.Should().Be("eu-west");
            roundTripped.CalendarName.Should().Be("HolidayCalendar");
            roundTripped.MisfireInstructionCode.Should().Be((int) DailyTimeIntervalTriggerMisfireInstruction.DoNothing);
            roundTripped.MisfireInstructionFamily.Should().Be(TriggerFamily.DailyTimeInterval);
            roundTripped.PreferredNode.Should().Be(PreferredNode.Auto);
            roundTripped.ExecutionGroup.Should().Be("imports");
            roundTripped.RetryPolicy.Should().Be(RetryPolicy.Fixed(3, TimeSpan.FromSeconds(30)));
        }
    }

    /// <summary>
    /// An empty job data map clears the trigger's, and has to survive as an empty object rather than
    /// being written away as nothing.
    /// </summary>
    [Test]
    public void AnEmptyJobDataMapIsCarried()
    {
        string json = JsonSerializer.Serialize(
            UpdateTriggerDetailsRequest.Create(new TriggerDetailsUpdate().WithJobDataMap(new JobDataMap())),
            wireOptions);

        json.Should().Be("""{"jobDataMap":{}}""");
        Read(json).AsUpdate().JobDataMap.Should().BeEmpty();
    }

    /// <summary>
    /// A body that clears the job data reads and writes back as itself, so the emptying survives a hop
    /// through a proxy that reads the contract and re-emits it.
    /// </summary>
    [Test]
    public void ClearingTheJobDataMapSurvivesBeingWrittenBack()
    {
        UpdateTriggerDetailsRequest request = Read("""{"jobDataMap":null}""");

        JsonSerializer.Serialize(request, wireOptions).Should().Be("""{"jobDataMap":null}""");
        request.AsUpdate().JobDataMap.Should().BeEmpty("clearing the map is setting it to an empty one");
    }

    [Test]
    public void AnUpdateThatSetsNothingIsAnEmptyBody()
    {
        JsonSerializer.Serialize(UpdateTriggerDetailsRequest.Create(new TriggerDetailsUpdate()), wireOptions)
            .Should().Be("{}", "the body says what changes, so an update that changes nothing says nothing");
    }

    private static UpdateTriggerDetailsRequest Read(string json)
        => JsonSerializer.Deserialize<UpdateTriggerDetailsRequest>(json, wireOptions)!;
}
