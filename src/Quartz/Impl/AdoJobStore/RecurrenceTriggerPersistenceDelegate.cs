
using Quartz.Impl.Triggers;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Persist a <see cref="RecurrenceTriggerImpl"/> by converting internal fields to and from
/// <see cref="SimplePropertiesTriggerProperties"/>.
/// </summary>
/// <see cref="RecurrenceScheduleBuilder"/>
/// <see cref="IRecurrenceTrigger"/>
public sealed class RecurrenceTriggerPersistenceDelegate : SimplePropertiesTriggerPersistenceDelegateSupport
{
    public override bool CanHandleTriggerType(IOperableTrigger trigger)
    {
        return trigger is RecurrenceTriggerImpl;
    }

    public override string GetHandledTriggerTypeDiscriminator()
    {
        return AdoConstants.TriggerTypeRecurrence;
    }

    protected override SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger)
    {
        RecurrenceTriggerImpl recTrig = (RecurrenceTriggerImpl) trigger;

        // QRTZ_SIMPROP_TRIGGERS STR_PROP_1 column is VARCHAR(512)
        if (recTrig.RecurrenceRule.Length > 512)
        {
            throw new JobPersistenceException(
                "RecurrenceRule string exceeds maximum length of 512 characters for database persistence.");
        }

        SimplePropertiesTriggerProperties props = new SimplePropertiesTriggerProperties();

        props.String1 = recTrig.RecurrenceRule;
        props.Int1 = recTrig.TimesTriggered;
        props.TimeZoneId = recTrig.TimeZone.Id;

        return props;
    }

    protected override TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties props)
    {
        TimeZoneInfo? tz = null;
        string? tzId = props.TimeZoneId;
        if (!string.IsNullOrEmpty(tzId))
        {
            tz = TimeZoneUtil.FindTimeZoneById(tzId!);
        }

        RecurrenceScheduleBuilder sb = RecurrenceScheduleBuilder.Create(props.String1!)
            .InTimeZone(tz);

        int timesTriggered = props.Int1;

        string[] statePropertyNames = { "timesTriggered" };
        object[] statePropertyValues = { timesTriggered };

        return new TriggerPropertyBundle(sb, statePropertyNames, statePropertyValues);
    }
}
