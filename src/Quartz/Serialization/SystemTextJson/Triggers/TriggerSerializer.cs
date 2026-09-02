using System.Text.Json;

namespace Quartz.Serialization.SystemTextJson.Triggers;

/// <summary>
/// How one trigger type is written to and read from the store's JSON.
/// </summary>
/// <remarks>
/// Implemented by deriving from <see cref="TriggerSerializer{TTrigger}" /> rather than directly.
/// </remarks>
public interface ITriggerSerializer
{
    /// <summary>
    /// The discriminator written into the payload, and matched against when reading one.
    /// </summary>
    string TriggerTypeName { get; }

    /// <summary>
    /// Builds the schedule the payload describes, which the converter then hands to a trigger builder.
    /// </summary>
    IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options);

    /// <summary>
    /// Writes the trigger's own fields. Its key, job key, description, calendar, priority, fire times
    /// and data map are written around this by the converter.
    /// </summary>
    void SerializeFields(Utf8JsonWriter writer, ITrigger trigger, JsonSerializerOptions options);

    /// <summary>
    /// Reads the trigger's own fields back, for the values a schedule builder cannot carry.
    /// </summary>
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
    /// <inheritdoc />
    public abstract string TriggerTypeName { get; }

    /// <inheritdoc />
    public abstract IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options);

    void ITriggerSerializer.SerializeFields(Utf8JsonWriter writer, ITrigger trigger, JsonSerializerOptions options) => SerializeFields(writer, (TTrigger) trigger, options);

    /// <summary>
    /// Writes the trigger's own fields.
    /// </summary>
    protected abstract void SerializeFields(Utf8JsonWriter writer, TTrigger trigger, JsonSerializerOptions options);

    void ITriggerSerializer.DeserializeFields(ITrigger trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
        DeserializeFields((TTrigger) trigger, jsonElement, options);
    }

    /// <summary>
    /// Reads back the fields a schedule builder cannot carry. Does nothing unless a trigger type has
    /// any, which most do not.
    /// </summary>
    protected virtual void DeserializeFields(TTrigger trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
    }
}