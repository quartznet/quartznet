---
title: 'Persisting a Custom Trigger Type'
---

# Persisting a Custom Trigger Type

The ADO job store knows how to store the five shipped trigger families. A trigger type of your own —
or one deriving from a shipped one with extra properties — needs an `ITriggerPersistenceDelegate` to
say how its schedule is written and read.

Without one, the store falls back to serializing the whole trigger into `QRTZ_BLOB_TRIGGERS`. That
works, and it is a blob: unqueryable, and coupled to your type's shape forever.

## The easy path: SIMPROP_TRIGGERS

`QRTZ_SIMPROP_TRIGGERS` is a generic side table with two strings, two ints, two longs, two decimals,
two booleans, a third string and a time zone id. If your schedule fits in those, derive from
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

Everything else — the four SQL statements, parameter binding, the reader — is done for you. Note what
is *not* virtual: `Initialize(TriggerPersistenceDelegateContext)` is a plain `public void` on the base
that sets three protected properties (`TablePrefix`, `SchedulerName`, `DbAccessor`). Read those rather
than overriding it. The statements are `private const` for the same reason: they name every column the
base class writes, so a subclass replacing one would either write the same statement again or write one
the base's parameter binding does not match.

### The columns

| Property | Column |
|---|---|
| `String1`, `String2`, `String3` | `STR_PROP_1..3` |
| `Int1`, `Int2` | `INT_PROP_1..2` |
| `Long1`, `Long2` | `LONG_PROP_1..2` |
| `Decimal1`, `Decimal2` | `DEC_PROP_1..2` |
| `Boolean1`, `Boolean2` | `BOOL_PROP_1..2` (through the dialect's boolean conversion) |
| `TimeZoneId` | `TIME_ZONE_ID` |

They are deliberately anonymous: the schema is fixed, and a family that needs a fourth string is out of
luck rather than adding a column.

::: tip
`TIME_ZONE_ID` got a column of its own in 2.6. A delegate reading a row written before that finds the
id in `String2` instead — `CalendarIntervalTriggerPersistenceDelegate` implements exactly that
fallback, and is worth copying if your table has old rows.
:::

### The discriminator

`GetHandledTriggerTypeDiscriminator()` returns the value written into `QRTZ_TRIGGERS.TRIGGER_TYPE`, and
read back to find the delegate again. The shipped values are `SIMPLE`, `CRON`, `CAL_INT`, `DAILY_I`,
`RECUR` and `BLOB`. The column is `VARCHAR(8)`, so keep yours short — and do not collide with those
six.

### TriggerPropertyBundle and applyState

A trigger is rebuilt through `TriggerBuilder`, which carries a schedule but not runtime counters. That
is what the second constructor parameter is for:

<!-- snippet: sample_trigger_persistence_delegate_apply_state -->
```csharp
new TriggerPropertyBundle(scheduleBuilder, t => ((MyTriggerImpl) t).TimesTriggered = timesTriggered);
```
<!-- endSnippet -->

Pass `null` — or use the one-argument constructor — when your delegate carries no state beyond the
schedule; the cron delegate does exactly that. The store applies the fire state, then your applier,
then the routing state, in that order.

## The full path: your own table

`ITriggerPersistenceDelegate` directly, when the schedule does not fit the generic columns:

| Member | |
|---|---|
| `void Initialize(TriggerPersistenceDelegateContext context)` | **no default implementation** — a delegate that does not read the context has no accessor to prepare commands with, and would fail at its first statement rather than at startup |
| `bool CanHandleTriggerType(IOperableTrigger trigger)` | |
| `string GetHandledTriggerTypeDiscriminator()` | |
| `ValueTask<int> InsertExtendedTriggerProperties(conn, trigger, state, jobDetail, ct)` | |
| `ValueTask<int> UpdateExtendedTriggerProperties(conn, trigger, state, jobDetail, ct)` | |
| `ValueTask<int> DeleteExtendedTriggerProperties(conn, triggerKey, ct)` | |
| `ValueTask<TriggerPropertyBundle> LoadExtendedTriggerProperties(conn, triggerKey, ct)` | |
| `TriggerPropertyBundle ReadTriggerPropertyBundle(DbDataReader rs)` | |

There is one default interface method: the batch
`LoadExtendedTriggerProperties(conn, IReadOnlyCollection<TriggerKey>, ct)`, which loops the single-key
overload. Override it when your table can answer a whole page in one statement.

`TriggerPersistenceDelegateContext` carries three things: `SchedulerName`, `TablePrefix`, and
`DbAccessor` — command preparation and parameter binding for the type table this delegate owns, which
is the driver delegate itself. Bind `SCHED_NAME` in every statement, and substitute the table prefix.

You will also need DDL for the table, in every dialect you support, and a migration script — see
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

There is a factory overload too, `UseTriggerPersistenceDelegate(Func<IServiceProvider, ITriggerPersistenceDelegate>)`,
for a delegate whose constructor takes values rather than services. Either way the delegate is
constructed with `ActivatorUtilities`, so constructor dependencies work and no parameterless
constructor is required.

Registration is an *enumerable* — the five built-ins are always present, and yours is added to them.
Registering the same type twice collapses to one.

::: warning Ordering
The five built-in delegates are consulted **first**, and matching is first-wins. A delegate for a type
deriving from a shipped trigger will therefore never be reached unless the built-in one declines it —
which is what `HasAdditionalProperties` is for. See below.
:::

`Initialize` is called by the *driver delegate*, not the store, once at scheduler startup: the store
hands the registered delegates to `StdAdoDelegate.Initialize`, which builds a
`TriggerPersistenceDelegateContext` for each and calls it before adding it to the list.

## What else a custom trigger type needs

A persistence delegate is one of four pieces:

**1. An `IOperableTrigger`.** In practice derive from `TriggerBase`, which is public and abstract, and
implement `GetScheduleBuilder()`.

::: tip
All five shipped trigger implementations are subclassable, so deriving from `SimpleTriggerImpl` or
`CronTriggerImpl` — or from `CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` or
`RecurrenceTriggerImpl` — is the shortest route to a trigger that is one of those with something added.
Pair it with a serializer deriving from that trigger's built-in serializer, as below.
:::

**2. An `IScheduleBuilder`.** The store rebuilds a trigger as
`TriggerBuilder.Create()…WithSchedule(bundle.ScheduleBuilder)`, so the schedule has to be reproducible
from a builder.

**3. `HasAdditionalProperties`, if you derive from a built-in trigger.** `TriggerBase` declares
`public virtual bool HasAdditionalProperties => false`. Override it to return `true` and the built-in
delegate for the base type declines to handle your trigger, which is what lets yours be reached — and
what makes the store fall back to a BLOB if you never write one.

**4. A serializer**, for the BLOB path and for job-data round-tripping:

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

The built-in serializers are public and unsealed on purpose: a trigger deriving from a built-in one
pairs with a serializer deriving from the built-in one, overriding `SerializeFields` /
`DeserializeFields` and calling the base so the built-in fields keep their stored shape.

::: warning
`UseSystemTextJsonSerializer(configure)` with a callback captures a **per-scheduler** registry that is
not published to the container. Called with no callback, the serializer reads the container-wide
registry instead. Pick one: registering custom serializers in the callback and then expecting the HTTP
client to know about them will not work.
:::

::: tip
If you also use `RAMJobStore`, note that its trigger-type discriminator is a hard-coded switch that a
custom type falls off the end of into the blob branch. That is harmless in memory, but it means a
custom trigger behaves differently in the two stores — worth knowing when a test passes in memory and
fails against a database.
:::

## See also

- [A Driver Delegate for a New Database](dialect-delegate.md) — the other delegate seam
- [A Job Store of Your Own](custom-job-store.md) — when the storage model itself is different
- [JSON Serialization](../packages/system-text-json.md) — the serializer registry in full
