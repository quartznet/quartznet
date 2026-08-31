---

title: JSON Serialization
---

::: tip
JSON is the recommended format for data a job store persists. Consider also setting
`StoreJobDataAsStrings`, which keeps job data out of the serializer altogether by restricting it to
strings.
:::

::: tip
System.Text.Json serialization is built into the `Quartz` package and is the default; see
[Serialization (System.Text.Json)](system-text-json).
:::

## JSON.NET

[Quartz.Serialization.Newtonsoft](https://www.nuget.org/packages/Quartz.Serialization.Newtonsoft) provides JSON serialization support for job stores using
[Json.NET](https://www.newtonsoft.com/json) to handle the actual serialization process.

### Installation

You need to add NuGet package reference to your project which uses Quartz.

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

Without a host, the same calls go inside `QuartzSchedulerBuilder.Create(q => …)`:

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

`Build()` returns a `StandaloneSchedulerFactory`, which owns the container it built: dispose it — with
`await using`, as above — and the scheduler shuts down with it.

**Classic property-based configuration**

The flat keys 3.x used still work, and mean the same thing:

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

`UseGenericDatabase` is the right method only for a database Quartz has no specific support for; use
`UseSqlServer`, `UsePostgres` and the rest otherwise. If Quartz ships no description of your ADO.NET
driver either, describe it in the same call — see
[the configuration reference](../configuration/reference.md#describing-a-driver-quartz-does-not-know).

### What a job data map may hold

A job data value has to be one of the types `JobDataMap` declares an accessor for — `string`, `bool`,
`char`, the numeric types, `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, `DateOnly`, `TimeOnly`, an
enum — or a `Dictionary<string, string>`; anything else is refused when the job or trigger is stored,
with a `Quartz.JsonSerializationException` naming the entry and the type, rather than written as a blob
that fails to load on the next fire. That is the same set the System.Text.Json serializer accepts, and
literally the same declaration, so a value one of them writes is a value the other's reader has an answer
for — down to the bytes: a `Dictionary<string, string>` is written as a plain JSON object here as well,
where this serializer used to name the type it had written the map under.

The one name a string map's own entries cannot use is `$type`. That is where Json.NET writes a value's
type, so both readers take it as metadata rather than data, and a map that stores an entry under it is
refused along with everything else neither reader could hand back.

To store a type of your own, declare it — which is your word that Json.NET can build it back, and a
versioning commitment for as long as the value sits in the database:

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

A `JobKey` or `TriggerKey` held as a job data value takes the same declaration. A `TimeZoneInfo` and a
nested `JobDataMap` are past declaring, because Json.NET cannot read either back out of what it writes —
store a zone's `Id`, and serialize a nested structure in the job and keep the result as a string. A
string is also the answer when the value has to survive a change of serializer, since a declared type is
read back by the serializer that wrote it and by no other.

### Migrating from binary serialization

Quartz 4 no longer ships the `BinaryObjectSerializer`: the underlying `BinaryFormatter`
has been removed from modern .NET and throws on .NET 9 and later. If you still have
binary-serialized data in your database you need to migrate it to JSON.

The recommended path is to perform the migration **while you are still on Quartz 3.x**,
which still includes `BinaryObjectSerializer` - see the Quartz 3.x version of this page
for a ready-made hybrid serializer. Either let the system migrate gradually as it runs,
or write a small program that loads and writes back every serialized asset in the
database.

If you must read legacy binary data after upgrading to Quartz 4 on .NET 9 or later, you
can re-enable `BinaryFormatter` with Microsoft's unsupported
[compatibility package](https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-migration-guide/compatibility-package).
Because the package does not change `BinaryFormatter`'s type identity, only your
**application project** needs it - Quartz itself does not reference it:

```xml
<PropertyGroup>
  <EnableUnsafeBinaryFormatterSerialization>true</EnableUnsafeBinaryFormatterSerialization>
</PropertyGroup>
<ItemGroup>
  <!-- match the package's major version to your application's target framework -->
  <PackageReference Include="System.Runtime.Serialization.Formatters" Version="10.0.0" />
</ItemGroup>
```

The package restores a working - but still unsafe - `BinaryFormatter`, so read the Microsoft
guidance before relying on it and remove it once the migration is complete. The Quartz types a
blob can be made of - the job data maps, the keys that can sit in them as values, the calendars
and the trigger classes - keep their `[Serializable]` / `ISerializable` support, so the hybrid
serializer below can read the old binary payloads and write everything back as JSON. Types that
could never be part of a blob lost those attributes in 4.0; see
[the migration guide](../migration-guide.md#serializable-survives-only-where-a-database-blob-needs-it)
for the full list.

A blob whose job data holds a key, or a class of the application's own, needs that type declared with
`AddJobDataValueType<T>()` on the registry the migrator's inner serializer is built from — otherwise the
value reads out of the binary payload and is refused on the way back in, which is
[the gate described above](#what-a-job-data-map-may-hold) doing its job at the one moment it is unwelcome.

One column is the exception: `BLOB_TRIGGERS.BLOB_DATA` holds whole trigger objects, and
`BinaryFormatter` records private base-class fields under the base class's *name* - which 4.0
renamed (`AbstractTrigger` is `TriggerBase`) and whose field set 4.0 extended. Migrate binary
blob triggers while still on 3.x; the hybrid serializer on 4.x is for the job data map, key and
calendar payloads.

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

If you need to customize JSON.NET settings, you need to inherit custom implementation and override `CreateSerializerSettings`.

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

If you have implemented a custom calendar, you need to implement a `ICalendarSerializer` for it.
There's a convenience base class `CalendarSerializer` that you can use the get strongly-typed experience.

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

A serializer can optionally override `CalendarTypeName` to give the calendar a serializer-neutral
name — the same discriminator the System.Text.Json package would use for it. The registry then finds
the serializer under that name as well as under the calendar's assembly-qualified type name, so a
payload written by either package resolves. Leave it unset and the serializer answers only to the
assembly-qualified name, which is what payloads written by 3.x carry.

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
`NewtonsoftJsonObjectSerializer.AddCalendarSerializer` and `AddTriggerSerializer` were static in 3.x, so
every scheduler in the process shared one set of custom serializers and registration order silently
decided which one won. They have been removed. Register through the `UseNewtonsoftJsonSerializer`
callback as above: what the callback registers belongs to that scheduler alone, so two schedulers in one
container can serialize different custom types.
:::

If you build a serializer yourself rather than through the store builder, hand it a
`NewtonsoftJsonSerializerRegistry`. A new registry already knows every built-in trigger and calendar type,
so registering a custom one adds to that set:

<!-- snippet: sample_newtonsoft_registry_directly -->
```csharp
NewtonsoftJsonSerializerRegistry registry = new NewtonsoftJsonSerializerRegistry()
    .AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer())
    .AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer());

NewtonsoftJsonObjectSerializer serializer = new(registry);
```
<!-- endSnippet -->
