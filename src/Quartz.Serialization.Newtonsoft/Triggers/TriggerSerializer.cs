using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Triggers;

public interface ITriggerSerializer
{
    string TriggerTypeForJson { get; }

    IScheduleBuilder CreateScheduleBuilder(JObject source);

    void SerializeFields(JsonWriter writer, ITrigger trigger);

    void DeserializeFields(ITrigger trigger, JObject source);
}

public abstract class TriggerSerializer<TTrigger> : ITriggerSerializer where TTrigger : ITrigger
{
    public abstract string TriggerTypeForJson { get; }

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