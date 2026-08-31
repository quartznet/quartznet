using System.Text;
using System.Text.Json;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Serialization.Newtonsoft;
using Quartz.Serialization.SystemTextJson;

using StjJsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// The built-in trigger serializers are public and unsealed on purpose, in both packages: a trigger
/// deriving from a built-in trigger pairs with a serializer deriving from that trigger's built-in
/// serializer, which calls the base for the built-in fields so their stored shape stays the built-in
/// one rather than a hand-copy that has to be kept in step with it.
/// </summary>
/// <remarks>
/// The ADO smoke tests exercise the same seam, but only where a database is running. This is its guard
/// at unit speed, so that making a built-in serializer internal — or sealing one — fails here first.
/// </remarks>
public class BuiltInTriggerSerializerDerivationTest
{
    [Test]
    public void ANewtonsoftSerializerDerivesFromTheBuiltInOneAndKeepsItsFields()
    {
        NewtonsoftJsonSerializerRegistry registry = new NewtonsoftJsonSerializerRegistry()
            .AddTriggerSerializer<ReportTrigger>(new ReportTriggerNewtonsoftSerializer());
        NewtonsoftJsonObjectSerializer serializer = new(registry);

        byte[] payload = serializer.Serialize(CreateTrigger());

        Encoding.UTF8.GetString(payload).Should().Contain("CronExpressionString",
            "the built-in serializer's base call is what writes the cron half of the payload");

        ReportTrigger restored = serializer.Deserialize<ReportTrigger>(payload);

        AssertRoundTripped(restored);
    }

    [Test]
    public void ASystemTextJsonSerializerDerivesFromTheBuiltInOneAndKeepsItsFields()
    {
        SystemTextJsonSerializerRegistry registry = new SystemTextJsonSerializerRegistry()
            .AddTriggerSerializer<ReportTrigger>(new ReportTriggerSystemTextJsonSerializer());
        SystemTextJsonObjectSerializer serializer = new(registry);

        byte[] payload = serializer.Serialize(CreateTrigger());

        Encoding.UTF8.GetString(payload).Should().Contain("CronExpressionString",
            "the built-in serializer's base call is what writes the cron half of the payload");

        ReportTrigger restored = serializer.Deserialize<ReportTrigger>(payload);

        AssertRoundTripped(restored);
    }

    private static ReportTrigger CreateTrigger()
    {
        return new ReportTrigger
        {
            Key = new TriggerKey("nightly", "reports"),
            JobKey = new JobKey("rollup", "reports"),
            CronExpressionString = "0 0 3 * * ?",
            TimeZone = TimeZoneInfo.Utc,
            Report = "daily-revenue"
        };
    }

    private static void AssertRoundTripped(ReportTrigger restored)
    {
        restored.Should().NotBeNull();
        restored.CronExpressionString.Should().Be("0 0 3 * * ?",
            "the built-in half of the payload is read back by the built-in serializer's own fields");
        restored.TimeZone.Should().Be(TimeZoneInfo.Utc);
        restored.Report.Should().Be("daily-revenue",
            "the derived half is the only part the subclass writes itself");
        restored.HasAdditionalProperties.Should().BeTrue(
            "a trigger that adds properties to a built-in one is what this pairing exists for");
    }

    /// <summary>
    /// A cron trigger with something added, which is what <c>HasAdditionalProperties</c> announces.
    /// </summary>
    private sealed class ReportTrigger : CronTriggerImpl
    {
        public override bool HasAdditionalProperties => true;

        public string Report { get; set; } = "";
    }

    private sealed class ReportTriggerNewtonsoftSerializer : Serialization.Newtonsoft.Triggers.CronTriggerSerializer
    {
        public override string TriggerTypeName => "ReportTrigger";

        public override IScheduleBuilder CreateScheduleBuilder(JObject source)
        {
            return new ReportTriggerScheduleBuilder(
                source.Value<string>("CronExpressionString")!,
                TimeZones.FindById(source.Value<string>("TimeZone")!));
        }

        protected override void SerializeFields(JsonWriter writer, ICronTrigger trigger)
        {
            base.SerializeFields(writer, trigger);
            writer.WritePropertyName("Report");
            writer.WriteValue(((ReportTrigger) trigger).Report);
        }

        protected override void DeserializeFields(ICronTrigger trigger, JObject source)
        {
            base.DeserializeFields(trigger, source);
            ((ReportTrigger) trigger).Report = source.Value<string>("Report")!;
        }
    }

    private sealed class ReportTriggerSystemTextJsonSerializer : Serialization.SystemTextJson.Triggers.CronTriggerSerializer
    {
        public override string TriggerTypeName => "ReportTrigger";

        public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, StjJsonSerializerOptions options)
        {
            return new ReportTriggerScheduleBuilder(
                jsonElement.GetProperty("CronExpressionString").GetString()!,
                TimeZones.FindById(jsonElement.GetProperty("TimeZone").GetString()!));
        }

        protected override void SerializeFields(Utf8JsonWriter writer, ICronTrigger trigger, StjJsonSerializerOptions options)
        {
            base.SerializeFields(writer, trigger, options);
            writer.WriteString("Report", ((ReportTrigger) trigger).Report);
        }

        protected override void DeserializeFields(ICronTrigger trigger, JsonElement jsonElement, StjJsonSerializerOptions options)
        {
            base.DeserializeFields(trigger, jsonElement, options);
            ((ReportTrigger) trigger).Report = jsonElement.GetProperty("Report").GetString()!;
        }
    }

    /// <summary>
    /// Builds the subclass rather than the built-in trigger, which is the one thing a derived serializer
    /// cannot inherit: the built-in schedule builder knows only the built-in type.
    /// </summary>
    private sealed class ReportTriggerScheduleBuilder(string cronExpression, TimeZoneInfo timeZone) : IScheduleBuilder
    {
        public IMutableTrigger Build()
        {
            return new ReportTrigger
            {
                CronExpressionString = cronExpression,
                TimeZone = timeZone
            };
        }
    }
}
