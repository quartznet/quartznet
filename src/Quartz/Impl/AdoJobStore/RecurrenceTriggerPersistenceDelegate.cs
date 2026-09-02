
using Quartz.Impl.Triggers;
using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Persist a <see cref="RecurrenceTriggerImpl"/> by converting internal fields to and from
/// <see cref="SimplePropertiesTriggerProperties"/>.
/// </summary>
/// <see cref="RecurrenceScheduleBuilder"/>
/// <see cref="IRecurrenceTrigger"/>
public sealed class RecurrenceTriggerPersistenceDelegate : SimplePropertiesTriggerPersistenceDelegateBase
{
    /// <inheritdoc />
    public override bool CanHandleTriggerType(IOperableTrigger trigger)
    {
        return trigger is RecurrenceTriggerImpl;
    }

    /// <inheritdoc />
    public override string GetHandledTriggerTypeDiscriminator()
    {
        return AdoConstants.TriggerTypeRecurrence;
    }

    /// <inheritdoc />
    protected override SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger)
    {
        RecurrenceTriggerImpl recTrig = (RecurrenceTriggerImpl) trigger;

        // QRTZ_SIMPROP_TRIGGERS STR_PROP_1 column is VARCHAR(512)
        if (recTrig.RecurrenceRule.Length > 512)
        {
            throw new JobPersistenceException(
                "RecurrenceRule string exceeds maximum length of 512 characters for database persistence.");
        }

        return new SimplePropertiesTriggerProperties
        {
            String1 = recTrig.RecurrenceRule,
            Int1 = recTrig.TimesTriggered,
            TimeZoneId = recTrig.TimeZone.Id,
        };
    }

    /// <inheritdoc />
    protected override TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties props)
    {
        TimeZoneInfo? tz = null;
        string? tzId = props.TimeZoneId;
        if (!string.IsNullOrEmpty(tzId))
        {
            tz = TimeZones.FindById(tzId!);
        }

        RecurrenceScheduleBuilder sb = RecurrenceScheduleBuilder.Create(props.String1!)
            .InTimeZone(tz);

        int timesTriggered = props.Int1;

        return new TriggerPropertyBundle(sb, t => ((RecurrenceTriggerImpl) t).TimesTriggered = timesTriggered);
    }
}
