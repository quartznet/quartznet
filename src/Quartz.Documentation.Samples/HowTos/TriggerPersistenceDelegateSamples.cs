using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Triggers;
using Quartz.Serialization.SystemTextJson.Triggers;

namespace Quartz.Documentation.Samples.HowTos;

#region sample_trigger_persistence_delegate

public sealed class BusinessDayTriggerPersistenceDelegate : SimplePropertiesTriggerPersistenceDelegateBase
{
    public override string GetHandledTriggerTypeDiscriminator() => "BUSDAY";

    public override bool CanHandleTriggerType(IOperableTrigger trigger)
        => trigger is BusinessDayTriggerImpl impl && !impl.HasAdditionalProperties;

    protected override SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger)
    {
        BusinessDayTriggerImpl t = (BusinessDayTriggerImpl) trigger;
        return new SimplePropertiesTriggerProperties
        {
            Int1 = t.SkipCount,
            Long1 = t.TimesTriggered,
            String1 = t.CalendarSystem,
            TimeZoneId = t.TimeZone.Id,
        };
    }

    protected override TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties props)
    {
        BusinessDayScheduleBuilder schedule = BusinessDayScheduleBuilder.Create()
            .SkippingDays(props.Int1)
            .InCalendarSystem(props.String1!)
            .InTimeZone(TimeZones.FindById(props.TimeZoneId!));

        long timesTriggered = props.Long1;
        return new TriggerPropertyBundle(
            schedule,
            t => ((BusinessDayTriggerImpl) t).TimesTriggered = timesTriggered);
    }
}

#endregion

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/trigger-persistence-delegate.md.
/// </summary>
public static class TriggerPersistenceDelegateSamples
{
    public static void ApplyState(IScheduleBuilder scheduleBuilder, long timesTriggered)
    {
        #region sample_trigger_persistence_delegate_apply_state

        new TriggerPropertyBundle(scheduleBuilder, t => ((MyTriggerImpl) t).TimesTriggered = timesTriggered);

        #endregion
    }

    public static void Registration(IHostApplicationBuilder builder, string connectionString)
    {
        #region sample_trigger_persistence_delegate_registration

        builder.Services.AddQuartz(q =>
        {
            q.UsePersistentStore(s =>
            {
                s.UseSqlServer(connectionString);
                s.UseTriggerPersistenceDelegate<BusinessDayTriggerPersistenceDelegate>();
            });
        });

        #endregion
    }

    public static void RegisteringTheSerializer(IPersistentStoreBuilder s)
    {
        #region sample_trigger_persistence_delegate_serializer_registration

        s.UseSystemTextJsonSerializer(registry =>
            registry.AddTriggerSerializer<BusinessDayTriggerImpl>(new BusinessDayTriggerSerializer()));

        #endregion
    }
}

/// <summary>
/// The trigger type the page invents, made real enough to compile the delegate that stores it.
/// Everything below is scaffolding; none of it appears on the page.
/// </summary>
public class BusinessDayTriggerImpl : TriggerBase
{
    public int SkipCount { get; set; }

    public long TimesTriggered { get; set; }

    public string? CalendarSystem { get; set; }

    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    public override DateTimeOffset? FinalFireTimeUtc => null;

    public override DateTimeOffset? PreviousFireTimeUtc { get; set; }

    public override DateTimeOffset? NextFireTimeUtc { get; set; }

    public override bool MayFireAgain => false;

    protected override bool HasMillisecondPrecision => false;

    public override IScheduleBuilder GetScheduleBuilder() => BusinessDayScheduleBuilder.Create();

    public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar? calendar) => null;

    public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime) => null;

    public override void Triggered(ICalendar? calendar)
    {
    }

    public override void UpdateAfterMisfire(ICalendar? calendar)
    {
    }

    public override void UpdateWithNewCalendar(ICalendar calendar, TimeSpan misfireThreshold)
    {
    }

    protected override bool ValidateMisfireInstruction(int misfireInstruction) => true;
}

/// <summary>The trigger the <c>applyState</c> sample casts to; scaffolding, like the one above.</summary>
public sealed class MyTriggerImpl : BusinessDayTriggerImpl;

public sealed class BusinessDayScheduleBuilder : IScheduleBuilder
{
    private BusinessDayScheduleBuilder()
    {
    }

    public static BusinessDayScheduleBuilder Create() => new();

    public BusinessDayScheduleBuilder SkippingDays(int days) => this;

    public BusinessDayScheduleBuilder InCalendarSystem(string calendarSystem) => this;

    public BusinessDayScheduleBuilder InTimeZone(TimeZoneInfo timeZone) => this;

    public IMutableTrigger Build() => new BusinessDayTriggerImpl();
}

public sealed class BusinessDayTriggerSerializer : TriggerSerializer<BusinessDayTriggerImpl>
{
    public override string TriggerTypeName => "BusinessDayTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
        => BusinessDayScheduleBuilder.Create();

    protected override void SerializeFields(Utf8JsonWriter writer, BusinessDayTriggerImpl trigger, JsonSerializerOptions options)
    {
        writer.WriteNumber("SkipCount", trigger.SkipCount);
    }
}
