using System.Text.Json;

namespace Quartz.Serialization.SystemTextJson.Triggers;

public interface ITriggerSerializer
{
    string TriggerTypeName { get; }

    IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options);

    void SerializeFields(Utf8JsonWriter writer, ITrigger trigger, JsonSerializerOptions options);

    void DeserializeFields(ITrigger trigger, JsonElement jsonElement, JsonSerializerOptions options);
}

/// <summary>
/// Base class for a custom trigger type's JSON serializer.
/// </summary>
/// <remarks>
/// Derive from this for a trigger type of your own. The built-in serializers
/// (<see cref="SimpleTriggerSerializer"/> and its siblings) are deliberately public and unsealed:
/// a trigger deriving from a built-in trigger — <c>HasAdditionalProperties</c> returning
/// <see langword="true" /> — pairs with a serializer deriving from the built-in one, overriding
/// <see cref="SerializeFields"/> / <see cref="DeserializeFields"/> and calling the base so the
/// built-in fields keep their stored shape.
/// </remarks>
public abstract class TriggerSerializer<TTrigger> : ITriggerSerializer where TTrigger : ITrigger
{
    public abstract string TriggerTypeName { get; }

    public abstract IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options);

    void ITriggerSerializer.SerializeFields(Utf8JsonWriter writer, ITrigger trigger, JsonSerializerOptions options) => SerializeFields(writer, (TTrigger) trigger, options);

    protected abstract void SerializeFields(Utf8JsonWriter writer, TTrigger trigger, JsonSerializerOptions options);

    void ITriggerSerializer.DeserializeFields(ITrigger trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
        DeserializeFields((TTrigger) trigger, jsonElement, options);
    }

    protected virtual void DeserializeFields(TTrigger trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
    }
}