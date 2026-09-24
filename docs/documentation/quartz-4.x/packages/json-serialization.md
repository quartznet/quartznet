---

title: JSON Serialization
---

::: tip
JSON is the recommended format for job store data. Also consider `StoreJobDataAsStrings`, which restricts job
data to strings and keeps it out of the serializer.
:::

::: tip
System.Text.Json serialization is built into the `Quartz` package and is the default; see
[Serialization (System.Text.Json)](system-text-json).
:::

## JSON.NET

[Quartz.Serialization.Newtonsoft](https://www.nuget.org/packages/Quartz.Serialization.Newtonsoft) serializes job
store data with [Json.NET](https://www.newtonsoft.com/json).

### Installation

```shell
dotnet add package Quartz.Serialization.Newtonsoft
```

### Configuring

**Configuring the store**

<!-- snippet: sample_newtonsoft_registration -->
```csharp
builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);

    // it's generally recommended to stick with
    // string property keys and values when serializing
    store.ConfigureStore(options => options.StoreJobDataAsStrings = true);

    store.UseNewtonsoftJsonSerializer();
}));
```
<!-- endSnippet -->

Without a host, put the same calls inside `QuartzSchedulerBuilder.Create(q => …)`:

<!-- snippet: sample_newtonsoft_standalone -->
```csharp
await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder
    .Create(q => q.UsePersistentStore(store =>
    {
        store.UseGenericDatabase("MyProvider", "my connection string");
        store.ConfigureStore(options => options.StoreJobDataAsStrings = true);
        store.UseNewtonsoftJsonSerializer();
    }))
    .Build();
```
<!-- endSnippet -->

`Build()` returns a `StandaloneSchedulerFactory`, which owns the container it built. Disposing it (`await using`,
as above) shuts the scheduler down.

**Classic property-based configuration**

The 3.x flat keys still work, with the same meaning:

<!-- snippet: sample_newtonsoft_properties -->
```csharp
NameValueCollection properties = new()
{
    ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz",
    ["quartz.serializer.type"] = "newtonsoft"
};

await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
    .UseProperties(properties)
    .Build();
```
<!-- endSnippet -->

Use `UseGenericDatabase` only for a database Quartz has no specific support for; otherwise use `UseSqlServer`,
`UsePostgres` and the rest. If Quartz has no description of your ADO.NET driver either, describe it in the same
call; see [the configuration reference](../configuration/reference.md#describing-a-driver-quartz-does-not-know).

### What a job data map may hold

A job data value must be one of the types `JobDataMap` has an accessor for, or a `Dictionary<string, string>`:

- `string`, `bool`, `char`, the numeric types;
- `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, `DateOnly`, `TimeOnly`;
- an enum.

Anything else is refused when the job or trigger is stored, with a `Quartz.JsonSerializationException` naming
the entry and the type. It is not written as a blob that fails to load on the next fire.

- The System.Text.Json serializer accepts the same set, from the same declaration, so either reader can read
  what the other wrote.
- A `Dictionary<string, string>` is written as a plain JSON object. This serializer used to record the map's
  type name.
- A string map entry cannot be named `$type`. Json.NET writes a value's type there, so both readers treat it as
  metadata; a map with such an entry is refused.

To store a type of your own, declare it. You then guarantee that Json.NET can build it back, and must keep it
readable for as long as the value is in the database:

<!-- snippet: sample_newtonsoft_job_data_value_type -->
```csharp
builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseNewtonsoftJsonSerializer(json =>
    {
        // Without this, a ReportOptions in a JobDataMap is refused when the job is stored.
        json.AddJobDataValueType<ReportOptions>();
    });
}));
```
<!-- endSnippet -->

A `JobKey` or `TriggerKey` job data value needs the same declaration. `TimeZoneInfo` and a nested `JobDataMap`
cannot be declared, because Json.NET cannot read them back:

- store a zone's `Id` instead of the zone;
- serialize a nested structure in the job and store the result as a string.

Use a string too when the value must survive a change of serializer: a declared type is read back only by the
serializer that wrote it.

### Migrating from binary serialization

Quartz 4 no longer ships the `BinaryObjectSerializer`. `BinaryFormatter` is removed from modern .NET and throws
on .NET 9 and later, so binary data in the database must be migrated to JSON.

Migrate **while still on Quartz 3.x**, which still has `BinaryObjectSerializer`; the Quartz 3.x version of this
page has a ready-made hybrid serializer. Either let the system migrate gradually as it runs, or write a small
program that loads and writes back every serialized asset.

#### Which blobs rewrite themselves, and which never do

A gradual migration does not finish on its own: running reliably rewrites only one of the four blob
columns. On 3.x and 4.x alike:

| Column | Rewritten by running? |
|---|---|
| `QRTZ_BLOB_TRIGGERS.BLOB_DATA` | **Yes**, on every write to the trigger, firings included |
| `QRTZ_TRIGGERS.JOB_DATA` | **Only if the map changed** (`JobDataMap.Dirty`) |
| `QRTZ_JOB_DETAILS.JOB_DATA` | **Only if the job is stored again**, or fires with `[PersistJobDataAfterExecution]` *and* a modified map |
| `QRTZ_CALENDARS.CALENDAR` | **Never**; only `AddCalendar` writes it |

- The trigger `UPDATE` omits `JOB_DATA` when `JobDataMap.Dirty` is false, so a trigger whose data nobody
  touches fires forever without rewriting it.
- A calendar added once at deployment is never rewritten.
- Anything belonging to a **paused** trigger, or one whose next fire is months out, is untouched until it
  resumes or fires.

Only a program that loads and writes back every asset finishes the job. Set the
`SchedulerConstants.ForceJobDataMapDirty` key to make a loaded map count as modified, so a re-store writes it.

`BLOB_TRIGGERS.BLOB_DATA` rewrites itself as the scheduler runs, but must still be migrated on 3.x: 4.x cannot
read it at all ([below](#blob-triggers-cannot-be-migrated-from-4-x)).

#### Finding what is left

A `BinaryFormatter` payload begins `0x00 0x01 0x00 0x00 0x00`; a JSON one begins `{` (`0x7B`). The first byte
tells them apart. Count what is still binary per dialect:

**SQL Server**

```sql
SELECT 'QRTZ_JOB_DETAILS' AS SOURCE, COUNT(*) AS STILL_BINARY FROM QRTZ_JOB_DETAILS   WHERE SUBSTRING(JOB_DATA,  1, 1) = 0x00
UNION ALL SELECT 'QRTZ_TRIGGERS',      COUNT(*) FROM QRTZ_TRIGGERS      WHERE SUBSTRING(JOB_DATA,  1, 1) = 0x00
UNION ALL SELECT 'QRTZ_CALENDARS',     COUNT(*) FROM QRTZ_CALENDARS     WHERE SUBSTRING(CALENDAR,  1, 1) = 0x00
UNION ALL SELECT 'QRTZ_BLOB_TRIGGERS', COUNT(*) FROM QRTZ_BLOB_TRIGGERS WHERE SUBSTRING(BLOB_DATA, 1, 1) = 0x00;
```

**PostgreSQL**

```sql
SELECT 'QRTZ_JOB_DETAILS' AS SOURCE, COUNT(*) AS STILL_BINARY FROM QRTZ_JOB_DETAILS   WHERE GET_BYTE(JOB_DATA,  0) = 0
UNION ALL SELECT 'QRTZ_TRIGGERS',      COUNT(*) FROM QRTZ_TRIGGERS      WHERE GET_BYTE(JOB_DATA,  0) = 0
UNION ALL SELECT 'QRTZ_CALENDARS',     COUNT(*) FROM QRTZ_CALENDARS     WHERE GET_BYTE(CALENDAR,  0) = 0
UNION ALL SELECT 'QRTZ_BLOB_TRIGGERS', COUNT(*) FROM QRTZ_BLOB_TRIGGERS WHERE GET_BYTE(BLOB_DATA, 0) = 0;
```

**MySQL**

```sql
SELECT 'QRTZ_JOB_DETAILS' AS SOURCE, COUNT(*) AS STILL_BINARY FROM QRTZ_JOB_DETAILS   WHERE SUBSTRING(JOB_DATA,  1, 1) = 0x00
UNION ALL SELECT 'QRTZ_TRIGGERS',      COUNT(*) FROM QRTZ_TRIGGERS      WHERE SUBSTRING(JOB_DATA,  1, 1) = 0x00
UNION ALL SELECT 'QRTZ_CALENDARS',     COUNT(*) FROM QRTZ_CALENDARS     WHERE SUBSTRING(CALENDAR,  1, 1) = 0x00
UNION ALL SELECT 'QRTZ_BLOB_TRIGGERS', COUNT(*) FROM QRTZ_BLOB_TRIGGERS WHERE SUBSTRING(BLOB_DATA, 1, 1) = 0x00;
```

**Oracle**

```sql
SELECT 'QRTZ_JOB_DETAILS' AS SOURCE, COUNT(*) AS STILL_BINARY FROM QRTZ_JOB_DETAILS   WHERE DBMS_LOB.SUBSTR(JOB_DATA,  1, 1) = HEXTORAW('00')
UNION ALL SELECT 'QRTZ_TRIGGERS',      COUNT(*) FROM QRTZ_TRIGGERS      WHERE DBMS_LOB.SUBSTR(JOB_DATA,  1, 1) = HEXTORAW('00')
UNION ALL SELECT 'QRTZ_CALENDARS',     COUNT(*) FROM QRTZ_CALENDARS     WHERE DBMS_LOB.SUBSTR(CALENDAR,  1, 1) = HEXTORAW('00')
UNION ALL SELECT 'QRTZ_BLOB_TRIGGERS', COUNT(*) FROM QRTZ_BLOB_TRIGGERS WHERE DBMS_LOB.SUBSTR(BLOB_DATA, 1, 1) = HEXTORAW('00');
```

**SQLite**

```sql
SELECT 'QRTZ_JOB_DETAILS' AS SOURCE, COUNT(*) AS STILL_BINARY FROM QRTZ_JOB_DETAILS   WHERE HEX(SUBSTR(JOB_DATA,  1, 1)) = '00'
UNION ALL SELECT 'QRTZ_TRIGGERS',      COUNT(*) FROM QRTZ_TRIGGERS      WHERE HEX(SUBSTR(JOB_DATA,  1, 1)) = '00'
UNION ALL SELECT 'QRTZ_CALENDARS',     COUNT(*) FROM QRTZ_CALENDARS     WHERE HEX(SUBSTR(CALENDAR,  1, 1)) = '00'
UNION ALL SELECT 'QRTZ_BLOB_TRIGGERS', COUNT(*) FROM QRTZ_BLOB_TRIGGERS WHERE HEX(SUBSTR(BLOB_DATA, 1, 1)) = '00';
```

**Firebird**

```sql
SELECT 'QRTZ_JOB_DETAILS' AS SOURCE, COUNT(*) AS STILL_BINARY FROM QRTZ_JOB_DETAILS   WHERE CAST(SUBSTRING(JOB_DATA  FROM 1 FOR 1) AS VARCHAR(1) CHARACTER SET OCTETS) = x'00'
UNION ALL SELECT 'QRTZ_TRIGGERS',      COUNT(*) FROM QRTZ_TRIGGERS      WHERE CAST(SUBSTRING(JOB_DATA  FROM 1 FOR 1) AS VARCHAR(1) CHARACTER SET OCTETS) = x'00'
UNION ALL SELECT 'QRTZ_CALENDARS',     COUNT(*) FROM QRTZ_CALENDARS     WHERE CAST(SUBSTRING(CALENDAR  FROM 1 FOR 1) AS VARCHAR(1) CHARACTER SET OCTETS) = x'00'
UNION ALL SELECT 'QRTZ_BLOB_TRIGGERS', COUNT(*) FROM QRTZ_BLOB_TRIGGERS WHERE CAST(SUBSTRING(BLOB_DATA FROM 1 FOR 1) AS VARCHAR(1) CHARACTER SET OCTETS) = x'00';
```

- Replace `QRTZ_` with your table prefix.
- A null column is neither binary nor JSON and is not counted.
- Zero everywhere: the migration finished. Otherwise the result names the table to rewrite; in practice it is
  `QRTZ_CALENDARS`.

To read legacy binary data on Quartz 4 with .NET 9 or later, re-enable `BinaryFormatter` with Microsoft's
unsupported
[compatibility package](https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-migration-guide/compatibility-package).
It keeps `BinaryFormatter`'s type identity, so only your **application project** references it; Quartz does not:

```xml
<PropertyGroup>
  <EnableUnsafeBinaryFormatterSerialization>true</EnableUnsafeBinaryFormatterSerialization>
</PropertyGroup>
<ItemGroup>
  <!-- match the package's major version to your application's target framework -->
  <PackageReference Include="System.Runtime.Serialization.Formatters" Version="10.0.0" />
</ItemGroup>
```

- The package restores a working but still unsafe `BinaryFormatter`. Read Microsoft's guidance first and
  remove the package once the migration is complete.
- The Quartz types a blob can contain (job data maps, keys stored in them, calendars, trigger classes) keep
  their `[Serializable]` / `ISerializable` support, so the hybrid serializer below reads binary payloads and
  writes JSON. Other types lost those attributes in 4.0; see
  [the migration guide](../migration-guide.md#serializable-survives-only-where-a-database-blob-needs-it).
- If a blob's job data holds a key or an application class, declare that type with `AddJobDataValueType<T>()`
  on the registry of the migrator's inner serializer. Otherwise the value is read from the binary payload and
  then refused on write, by [the rule above](#what-a-job-data-map-may-hold).

#### Blob triggers cannot be migrated from 4.x

Migrate binary `BLOB_TRIGGERS.BLOB_DATA` while still on 3.x. It holds whole trigger objects, and
`BinaryFormatter` records private base-class fields under the base class's *name*. 4.0 renamed that class
(`AbstractTrigger` is `TriggerBase`) and extended its fields. On 4.x the hybrid serializer handles job data map,
key and calendar payloads only.

**Example hybrid serializer**

```csharp
using System.Runtime.Serialization.Formatters.Binary;

using Newtonsoft.Json;

using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz;

public sealed class MigratorSerializer : IObjectSerializer
{
    // you might need custom configuration, see sections about customizing in documentation
    private readonly NewtonsoftJsonObjectSerializer jsonSerializer = new();

    public T Deserialize<T>(byte[] data) where T : class
    {
        try
        {
            // Attempt to deserialize data as JSON
            return jsonSerializer.Deserialize<T>(data)!;
        }
        catch (JsonReaderException)
        {
            // The data was not JSON, so fall back to the legacy binary format. This branch needs
            // the System.Runtime.Serialization.Formatters compatibility package and
            // EnableUnsafeBinaryFormatterSerialization to be set in the application project.
            using var stream = new MemoryStream(data);
#pragma warning disable SYSLIB0011
            var binaryData = (T) new BinaryFormatter().Deserialize(stream);
#pragma warning restore SYSLIB0011
            if (binaryData is JobDataMap jobDataMap)
            {
                // make sure we mark the map as dirty so it will be serialized as JSON next time
                jobDataMap[SchedulerConstants.ForceJobDataMapDirty] = "true";
            }
            return binaryData;
        }
    }

    public byte[] Serialize<T>(T obj) where T : class => jsonSerializer.Serialize(obj);
}
```

### Customizing JSON.NET

Subclass the serializer and override `CreateSerializerSettings`:

<!-- snippet: sample_newtonsoft_custom_serializer -->
```csharp
class CustomJsonSerializer : NewtonsoftJsonObjectSerializer
{
    protected override JsonSerializerSettings CreateSerializerSettings()
    {
        var settings = base.CreateSerializerSettings();
        settings.Converters.Add(new MyCustomConverter());
        return settings;
    }
}
```
<!-- endSnippet -->

**And then configure it to use**

<!-- snippet: sample_newtonsoft_use_custom_serializer -->
```csharp
store.UseSerializer<CustomJsonSerializer>();
```
<!-- endSnippet -->

or, as a flat property key:

```text
quartz.serializer.type = MyProject.CustomJsonSerializer, MyProject
```

### Customizing calendar serialization

A custom calendar needs an `ICalendarSerializer`. The base class `CalendarSerializer` gives a strongly-typed one.

**Custom calendar and serializer**

<!-- snippet: sample_newtonsoft_custom_calendar -->
```csharp
[Serializable]
class CustomCalendar : BaseCalendar
{
    public CustomCalendar()
    {
    }

    // binary serialization support
    protected CustomCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        SomeCustomProperty = info?.GetBoolean("SomeCustomProperty") ?? true;
    }

    public bool SomeCustomProperty { get; set; } = true;

    // binary serialization support
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info?.AddValue("SomeCustomProperty", SomeCustomProperty);
    }
}

// JSON serialization support
class CustomCalendarSerializer : CalendarSerializer<CustomCalendar>
{
    protected override CustomCalendar Create(JObject source)
    {
        return new CustomCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, CustomCalendar calendar)
    {
        writer.WritePropertyName("SomeCustomProperty");
        writer.WriteValue(calendar.SomeCustomProperty);
    }

    protected override void DeserializeFields(CustomCalendar calendar, JObject source)
    {
        calendar.SomeCustomProperty = source["SomeCustomProperty"]!.Value<bool>();
    }
}
```
<!-- endSnippet -->

Optionally override `CalendarTypeName` to give the calendar a serializer-neutral name, the same discriminator
the System.Text.Json serializer uses. The registry then finds the serializer by that name and by the
assembly-qualified type name, so payloads from either serializer resolve. Unset, only the assembly-qualified
name (what 3.x payloads carry) matches.

**Configuring custom calendar serializer**

<!-- snippet: sample_newtonsoft_register_calendar_serializer -->
```csharp
builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseNewtonsoftJsonSerializer(json =>
    {
        json.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
    });
}));
```
<!-- endSnippet -->

::: warning Changed in 4.0
`NewtonsoftJsonObjectSerializer.AddCalendarSerializer` and `AddTriggerSerializer` were static in 3.x: every
scheduler in the process shared one set, and registration order decided which won. They are removed. Register
through the `UseNewtonsoftJsonSerializer` callback as above; what it registers belongs to that scheduler alone.
:::

To build a serializer yourself, pass it a `NewtonsoftJsonSerializerRegistry`. A new registry knows every
built-in trigger and calendar type; custom registrations add to that set:

<!-- snippet: sample_newtonsoft_registry_directly -->
```csharp
NewtonsoftJsonSerializerRegistry registry = new NewtonsoftJsonSerializerRegistry()
    .AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer())
    .AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer());

NewtonsoftJsonObjectSerializer serializer = new(registry);
```
<!-- endSnippet -->
