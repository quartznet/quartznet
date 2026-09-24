---
title: 'Persisting a Custom Trigger Type'
---

# Persisting a Custom Trigger Type

The ADO job store stores the five shipped trigger families. Your own trigger type, or a shipped one with
extra properties, needs an `ITriggerPersistenceDelegate` to write and read its schedule. Without one, the
whole trigger is serialized into `QRTZ_BLOB_TRIGGERS`: unqueryable, and tied to your type's shape.

## The easy path: SIMPROP_TRIGGERS

`QRTZ_SIMPROP_TRIGGERS` is a generic side table: two strings, two ints, two longs, two decimals, two
booleans, a third string and a time zone id. If your schedule fits, derive from
`SimplePropertiesTriggerPersistenceDelegateBase` and write four members:

<!-- snippet: sample_trigger_persistence_delegate -->
```csharp
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
```
<!-- endSnippet -->

The base supplies the four SQL statements, parameter binding and the reader.

- `Initialize(TriggerPersistenceDelegateContext)` is a plain, non-virtual `public void` that sets the
  protected `TablePrefix`, `SchedulerName` and `DbAccessor`. Read those; do not override it.
- The statements are `private const`, because they name every column the base binds.

### The columns

| Property | Column |
|---|---|
| `String1`, `String2`, `String3` | `STR_PROP_1..3` |
| `Int1`, `Int2` | `INT_PROP_1..2` |
| `Long1`, `Long2` | `LONG_PROP_1..2` |
| `Decimal1`, `Decimal2` | `DEC_PROP_1..2` |
| `Boolean1`, `Boolean2` | `BOOL_PROP_1..2` (through the dialect's boolean conversion) |
| `TimeZoneId` | `TIME_ZONE_ID` |

The schema is fixed; a family needing a fourth string cannot add a column.

::: tip
`TIME_ZONE_ID` got its own column in 2.6; older rows keep the id in `String2`.
`CalendarIntervalTriggerPersistenceDelegate` implements that fallback; copy it if you have old rows.
:::

### The discriminator

`GetHandledTriggerTypeDiscriminator()` returns the value written to `QRTZ_TRIGGERS.TRIGGER_TYPE` and used to
find the delegate on read. The column is `VARCHAR(8)`; keep it short and avoid `SIMPLE`, `CRON`, `CAL_INT`,
`DAILY_I`, `RECUR` and `BLOB`.

### TriggerPropertyBundle and applyState

A trigger is rebuilt through `TriggerBuilder`, which has no runtime counters. The second constructor
parameter restores them:

<!-- snippet: sample_trigger_persistence_delegate_apply_state -->
```csharp
new TriggerPropertyBundle(scheduleBuilder, t => ((MyTriggerImpl) t).TimesTriggered = timesTriggered);
```
<!-- endSnippet -->

With no state beyond the schedule, pass `null` or use the one-argument constructor (as the cron delegate
does). The store applies the fire state, then your applier, then the routing state.

## The full path: your own table

Implement `ITriggerPersistenceDelegate` when the generic columns do not fit:

- `void Initialize(TriggerPersistenceDelegateContext context)`
- `bool CanHandleTriggerType(IOperableTrigger trigger)`
- `string GetHandledTriggerTypeDiscriminator()`
- `ValueTask<int> InsertExtendedTriggerProperties(conn, trigger, state, jobDetail, ct)`
- `ValueTask<int> UpdateExtendedTriggerProperties(conn, trigger, state, jobDetail, ct)`
- `ValueTask<int> DeleteExtendedTriggerProperties(conn, triggerKey, ct)`
- `ValueTask<TriggerPropertyBundle> LoadExtendedTriggerProperties(conn, triggerKey, ct)`
- `TriggerPropertyBundle ReadTriggerPropertyBundle(DbDataReader rs)`

Then:

- `Initialize` has **no default implementation**, so a delegate cannot skip the context and fail at its
  first statement instead of at startup.
- The one default interface method, the batch
  `LoadExtendedTriggerProperties(conn, IReadOnlyCollection<TriggerKey>, ct)`, loops the single-key overload.
  Override it if your table can return a page in one statement.
- `TriggerPersistenceDelegateContext` carries `SchedulerName`, `TablePrefix` and `DbAccessor` (the driver
  delegate itself: command preparation and parameter binding). Bind `SCHED_NAME` in every statement and
  substitute the table prefix.
- Ship DDL for every dialect you support, and a migration script; see
  [database/README.md](https://github.com/quartznet/quartznet/blob/main/database/README.md).

## Registering it

<!-- snippet: sample_trigger_persistence_delegate_registration -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(s =>
    {
        s.UseSqlServer(connectionString);
        s.UseTriggerPersistenceDelegate<BusinessDayTriggerPersistenceDelegate>();
    });
});
```
<!-- endSnippet -->

- `UseTriggerPersistenceDelegate(Func<IServiceProvider, ITriggerPersistenceDelegate>)` is for a delegate
  whose constructor takes values rather than services.
- Delegates are built with `ActivatorUtilities`: constructor dependencies work, and no parameterless
  constructor is needed.
- Registration is *enumerable*: yours is added to the five built-ins. Registering a type twice collapses to
  one.
- The *driver delegate* calls `Initialize`, once at startup: the store passes the registered delegates to
  `StdAdoDelegate.Initialize`, which builds a `TriggerPersistenceDelegateContext` for each and calls it before
  adding the delegate to its list.

::: warning Ordering
The five built-in delegates are consulted **first**, and the first match wins. A delegate for a type deriving
from a shipped trigger is reached only if the built-in one declines it, which is what
`HasAdditionalProperties` is for.
:::

## What else a custom trigger type needs

1. **An `IOperableTrigger`**: derive from the public abstract `TriggerBase` and implement
   `GetScheduleBuilder()`.
2. **An `IScheduleBuilder`**: the store rebuilds a trigger as
   `TriggerBuilder.Create()…WithSchedule(bundle.ScheduleBuilder)`.
3. **`HasAdditionalProperties`, when deriving from a built-in trigger.** `TriggerBase` declares
   `public virtual bool HasAdditionalProperties => false`. Return `true` so the built-in delegate declines
   your trigger; with no delegate of your own, the store falls back to a BLOB.
4. **A serializer**, for the BLOB path and job-data round-tripping.

::: tip
All five shipped implementations are subclassable: `SimpleTriggerImpl`, `CronTriggerImpl`,
`CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` and `RecurrenceTriggerImpl`. Deriving from one
is the shortest route to one of those with something added; pair it with a serializer derived from that
trigger's built-in serializer.
:::

<!-- The three remaining members are elided, so this one is written out here rather than
     compiled; a class with them left out does not compile. -->

```csharp
public sealed class BusinessDayTriggerSerializer : TriggerSerializer<BusinessDayTriggerImpl>
{
    public override string TriggerTypeName => "BusinessDayTrigger";
    // CreateScheduleBuilder / SerializeFields / DeserializeFields
}
```

<!-- snippet: sample_trigger_persistence_delegate_serializer_registration -->
```csharp
s.UseSystemTextJsonSerializer(registry =>
    registry.AddTriggerSerializer<BusinessDayTriggerImpl>(new BusinessDayTriggerSerializer()));
```
<!-- endSnippet -->

- The built-in serializers are public and unsealed: derive, override `SerializeFields` / `DeserializeFields`,
  and call the base so the built-in fields keep their stored shape.
- `Quartz.Serialization.Newtonsoft` uses the same names (`NewtonsoftJsonSerializerRegistry.AddTriggerSerializer<T>(…)`
  through `UseNewtonsoftJsonSerializer(…)`), so a custom trigger ports by changing the registration.
- Job data differs: declare your own value type inside a `JobDataMap` with `AddJobDataValueType<T>()` for
  Newtonsoft and `AddTypeInfoResolver(…)` for System.Text.Json. Both refuse an undeclared type at *write*
  time, naming the method.

::: warning
`UseSystemTextJsonSerializer(configure)` with a callback uses a **per-scheduler** registry not published to
the container; with no callback it reads the container-wide registry. Serializers registered in the callback
are unknown to the HTTP client.
:::

::: tip
`RAMJobStore`'s trigger-type discriminator is a hard-coded switch; custom types fall to its blob branch. That
is harmless in memory, but means a test can pass in memory and fail against a database.
:::

## See also

- [A Driver Delegate for a New Database](dialect-delegate.md) — the other delegate seam
- [A Job Store of Your Own](custom-job-store.md) — when the storage model itself is different
- [JSON Serialization](../packages/system-text-json.md) — the serializer registry in full
