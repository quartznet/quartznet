using System.Data.Common;
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

/// <summary>
/// The same idea written against <see cref="ITriggerPersistenceDelegate" /> directly, for a trigger
/// whose state does not fit the five SIMPROP columns and needs a table of its own.
/// </summary>
/// <remarks>
/// Not on the page — <see cref="BusinessDayTriggerPersistenceDelegate" /> is what the page teaches,
/// because inheriting the SIMPROP table is nearly always the answer. This exists because it is the only
/// shape that reaches the whole seam from outside Quartz: the context and its
/// <see cref="IDbAccessor" />, and the batching form that describes an update as a
/// <see cref="SqlStatement" /> rather than issuing it. If any of that stopped being public this file
/// would stop compiling, which is the point of it living in an assembly Quartz grants no
/// <c>InternalsVisibleTo</c>.
/// </remarks>
internal sealed class OwnTableTriggerPersistenceDelegate : ITriggerPersistenceDelegate
{
    private const string Table = "MY_BUSDAY_TRIGGERS";

    private const string KeyPredicate =
        "WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @triggerName AND TRIGGER_GROUP = @triggerGroup";

    private IDbAccessor accessor = null!;
    private string schedulerName = "";
    private string tablePrefix = "";

    public void Initialize(TriggerPersistenceDelegateContext context)
    {
        accessor = context.DbAccessor;
        schedulerName = context.SchedulerName;
        tablePrefix = context.TablePrefix;
    }

    public string GetHandledTriggerTypeDiscriminator() => "BUSDAY2";

    public bool CanHandleTriggerType(IOperableTrigger trigger) => trigger is BusinessDayTriggerImpl;

    public async ValueTask<int> InsertExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = accessor.PrepareCommand(
            conn,
            $"INSERT INTO {tablePrefix}{Table} (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, SKIP_COUNT) "
            + "VALUES (@schedulerName, @triggerName, @triggerGroup, @skipCount)");

        BindKey(cmd, trigger.Key);
        accessor.AddCommandParameter(cmd, "skipCount", ((BusinessDayTriggerImpl) trigger).SkipCount);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<int> UpdateExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = accessor.PrepareCommand(
            conn, $"UPDATE {tablePrefix}{Table} SET SKIP_COUNT = @skipCount " + KeyPredicate);

        BindKey(cmd, trigger.Key);
        accessor.AddCommandParameter(cmd, "skipCount", ((BusinessDayTriggerImpl) trigger).SkipCount);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The batching form. Describing the statement rather than issuing it lets the store send a
    /// trigger's rows in one round trip; <see langword="false" />, which is the interface's own default,
    /// means "issue it the ordinary way".
    /// </summary>
    public bool TryDescribeUpdateExtendedTriggerProperties(
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        ICollection<SqlStatement> statements)
    {
        statements.Add(new SqlStatement(
            $"UPDATE {tablePrefix}{Table} SET SKIP_COUNT = @skipCount " + KeyPredicate,
            [
                new SqlStatementParameter("skipCount", ((BusinessDayTriggerImpl) trigger).SkipCount),
                new SqlStatementParameter("schedulerName", schedulerName),
                new SqlStatementParameter("triggerName", trigger.Key.Name),
                new SqlStatementParameter("triggerGroup", trigger.Key.Group)
            ]));

        return true;
    }

    public async ValueTask<int> DeleteExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = accessor.PrepareCommand(
            conn, $"DELETE FROM {tablePrefix}{Table} " + KeyPredicate);

        BindKey(cmd, triggerKey);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<TriggerPropertyBundle> LoadExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = accessor.PrepareCommand(
            conn, $"SELECT SKIP_COUNT FROM {tablePrefix}{Table} " + KeyPredicate);

        BindKey(cmd, triggerKey);

        await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"No {Table} row for {triggerKey}.");
        }

        return ReadTriggerPropertyBundle(reader);
    }

    public async ValueTask<Dictionary<TriggerKey, TriggerPropertyBundle>> LoadExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        Dictionary<TriggerKey, TriggerPropertyBundle> bundles = [];
        foreach (TriggerKey triggerKey in triggerKeys)
        {
            bundles[triggerKey] = await LoadExtendedTriggerProperties(conn, triggerKey, cancellationToken);
        }

        return bundles;
    }

    public TriggerPropertyBundle ReadTriggerPropertyBundle(DbDataReader rs)
    {
        int skipCount = Convert.ToInt32(rs["SKIP_COUNT"]);

        return new TriggerPropertyBundle(BusinessDayScheduleBuilder.Create().SkippingDays(skipCount));
    }

    private void BindKey(DbCommand cmd, TriggerKey triggerKey)
    {
        accessor.AddCommandParameter(cmd, "schedulerName", schedulerName);
        accessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        accessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);
    }
}
