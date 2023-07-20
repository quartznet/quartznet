using System.Text.Json;

namespace Quartz.Triggers;

public interface ITriggerSerializer
{
    string TriggerTypeForJson { get; }

    IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement);

    void SerializeFields(Utf8JsonWriter writer, ITrigger trigger);
}

internal abstract class TriggerSerializer<TTrigger> : ITriggerSerializer where TTrigger : ITrigger
{
    public abstract string TriggerTypeForJson { get; }

    public abstract IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement);

    void ITriggerSerializer.SerializeFields(Utf8JsonWriter writer, ITrigger trigger) => SerializeFields(writer, (TTrigger) trigger);

    protected abstract void SerializeFields(Utf8JsonWriter writer, TTrigger trigger);
}