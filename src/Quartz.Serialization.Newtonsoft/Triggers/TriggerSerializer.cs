using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Serialization.Newtonsoft.Triggers;

/// <summary>
/// Reads and writes one kind of trigger's own fields, beside the ones every trigger has.
/// </summary>
/// <remarks>
/// The registry keys serializers by <see cref="TriggerTypeName" />, and that name is written into the
/// stored JSON as its discriminator — so it is the name a blob is read back by, and changing it makes
/// every trigger already stored under the old one unreadable. Register an implementation with
/// <c>NewtonsoftJsonSerializerRegistry.AddTriggerSerializer</c>, or derive from
/// <see cref="TriggerSerializer{TTrigger}" /> to be handed the trigger already typed.
/// </remarks>
public interface ITriggerSerializer
{
    /// <summary>
    /// The discriminator this kind of trigger is stored under, shared with the System.Text.Json
    /// package so a blob either wrote is readable by both.
    /// </summary>
    string TriggerTypeName { get; }

    /// <summary>
    /// Rebuilds the schedule from the stored fields, as the builder the trigger is reconstructed from.
    /// </summary>
    /// <param name="source">The stored JSON for one trigger.</param>
    IScheduleBuilder CreateScheduleBuilder(JObject source);

    /// <summary>
    /// Writes this kind of trigger's own fields. The fields every trigger has are written around this
    /// call rather than by it.
    /// </summary>
    /// <param name="writer">The writer positioned inside the trigger's JSON object.</param>
    /// <param name="trigger">The trigger being stored.</param>
    void SerializeFields(JsonWriter writer, ITrigger trigger);

    /// <summary>
    /// Reads back whatever <see cref="SerializeFields" /> wrote that the schedule builder did not
    /// already restore.
    /// </summary>
    /// <param name="trigger">The trigger being rebuilt.</param>
    /// <param name="source">The stored JSON for that trigger.</param>
    void DeserializeFields(ITrigger trigger, JObject source);
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
    public abstract IScheduleBuilder CreateScheduleBuilder(JObject source);

    void ITriggerSerializer.SerializeFields(JsonWriter writer, ITrigger trigger) => SerializeFields(writer, (TTrigger) trigger);

    /// <summary>
    /// Writes this kind of trigger's own fields, with the trigger already typed.
    /// </summary>
    /// <param name="writer">The writer positioned inside the trigger's JSON object.</param>
    /// <param name="trigger">The trigger being stored.</param>
    protected abstract void SerializeFields(JsonWriter writer, TTrigger trigger);

    void ITriggerSerializer.DeserializeFields(ITrigger trigger, JObject source)
    {
        DeserializeFields((TTrigger) trigger, source);
    }

    /// <summary>
    /// Reads back whatever the schedule builder did not already restore. Does nothing unless
    /// overridden, which is right for a trigger whose every field is part of its schedule.
    /// </summary>
    /// <param name="trigger">The trigger being rebuilt.</param>
    /// <param name="source">The stored JSON for that trigger.</param>
    protected virtual void DeserializeFields(TTrigger trigger, JObject source)
    {
    }
}