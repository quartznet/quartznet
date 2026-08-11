using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Serialization.Newtonsoft.Triggers;

public interface ITriggerSerializer
{
    string TriggerTypeName { get; }

    IScheduleBuilder CreateScheduleBuilder(JObject source);

    void SerializeFields(JsonWriter writer, ITrigger trigger);

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
    public abstract string TriggerTypeName { get; }

    public abstract IScheduleBuilder CreateScheduleBuilder(JObject source);

    void ITriggerSerializer.SerializeFields(JsonWriter writer, ITrigger trigger) => SerializeFields(writer, (TTrigger) trigger);

    protected abstract void SerializeFields(JsonWriter writer, TTrigger trigger);

    void ITriggerSerializer.DeserializeFields(ITrigger trigger, JObject source)
    {
        DeserializeFields((TTrigger) trigger, source);
    }

    protected virtual void DeserializeFields(TTrigger trigger, JObject source)
    {
    }
}