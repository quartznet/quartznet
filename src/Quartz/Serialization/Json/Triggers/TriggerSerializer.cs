using System.Text.Json;

namespace Quartz.Serialization.Json.Triggers;

public interface ITriggerSerializer
{
    string TriggerTypeForJson { get; }

    IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options);

    void SerializeFields(Utf8JsonWriter writer, ITrigger trigger, JsonSerializerOptions options);
}

internal abstract class TriggerSerializer<TTrigger> : ITriggerSerializer where TTrigger : ITrigger
{
    public abstract string TriggerTypeForJson { get; }

    public abstract IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options);

    void ITriggerSerializer.SerializeFields(Utf8JsonWriter writer, ITrigger trigger, JsonSerializerOptions options) => SerializeFields(writer, (TTrigger) trigger, options);

    protected abstract void SerializeFields(Utf8JsonWriter writer, TTrigger trigger, JsonSerializerOptions options);
}