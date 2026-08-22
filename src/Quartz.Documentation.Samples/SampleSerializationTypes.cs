using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Impl.Triggers;
using Quartz.Serialization.SystemTextJson.Triggers;

namespace Quartz.Documentation.Samples;

/// <summary>
/// The custom trigger types and converters the serialization pages name.
/// </summary>
/// <remarks>
/// A page showing how to register a serializer is showing the registration, not the serializer, so these
/// are the smallest thing that satisfies the registration's type constraints. Each derives from an
/// existing trigger implementation rather than from <c>AbstractTrigger</c>, because a full custom trigger
/// would be a page of its own.
/// </remarks>
public sealed class MyCustomConverter : JsonConverter<Uri>
{
    public override Uri Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, Uri value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public class CustomTrigger : SimpleTriggerImpl;

public sealed class ReportTrigger : CustomTrigger;

public sealed class IngestTrigger : CustomTrigger;

public sealed class MyTrigger : CustomTrigger;

public class CustomTriggerSerializer : TriggerSerializer<CustomTrigger>
{
    public override string TriggerTypeName => "CustomTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options) =>
        SimpleScheduleBuilder.Create();

    protected override void SerializeFields(Utf8JsonWriter writer, CustomTrigger trigger, JsonSerializerOptions options)
    {
    }
}

public sealed class ReportTriggerSerializer : CustomTriggerSerializer;

public sealed class IngestTriggerSerializer : CustomTriggerSerializer;

public sealed class MyTriggerSerializer : CustomTriggerSerializer;
