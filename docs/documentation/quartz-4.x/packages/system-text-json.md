---

title: Serialization (System.Text.Json)
---

::: tip
JSON is the recommended persistent format for new projects. Also consider setting `StoreJobDataAsStrings` to
true, which restricts job data to string keys and values.
:::

System.Text.Json serialization is built into the `Quartz` package; there is no separate
`Quartz.Serialization.SystemTextJson` package any more. It is the serializer a persistent store gets when
nothing else is configured.

## Configuring

**Code-first configuration**

<!-- snippet: sample_stj_registration -->
```csharp
services.AddQuartz(q =>
{
    q.UsePersistentStore(store =>
    {
        store.UseSqlServer("my connection string");

        // it's generally recommended to stick with
        // string property keys and values when serializing
        store.ConfigureStore(options => options.StoreJobDataAsStrings = true);

        store.UseSystemTextJsonSerializer();
    });
});
```
<!-- endSnippet -->

**Classic property-based configuration**

<!-- snippet: sample_stj_properties -->
```csharp
var properties = new NameValueCollection
{
 ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz",
 ["quartz.serializer.type"] = "stj"
};
ISchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
    .UseProperties(properties)
    .Build();
```
<!-- endSnippet -->

## Migrating from binary serialization

Quartz 4 no longer ships the `BinaryObjectSerializer`. The recipe in
[JSON Serialization](json-serialization#migrating-from-binary-serialization) applies to System.Text.Json too.

## Customizing serialization options

Subclass the serializer and override `CreateSerializerOptions`:

<!-- snippet: sample_stj_custom_serializer -->
```csharp
class CustomJsonSerializer : SystemTextJsonObjectSerializer
{
    // Declaring this constructor is what lets the container hand the serializer the registered custom
    // trigger and calendar serializers; without it only the built-in types are known.
    public CustomJsonSerializer(SystemTextJsonSerializerRegistry registry) : base(registry)
    {
    }

    protected override JsonSerializerOptions CreateSerializerOptions()
    {
        var options = base.CreateSerializerOptions();
        options.Converters.Add(new MyCustomConverter());
        return options;
    }
}
```
<!-- endSnippet -->

**And then configure it to use**

<!-- snippet: sample_stj_use_custom_serializer -->
```csharp
store.UseSerializer<CustomJsonSerializer>();
```
<!-- endSnippet -->

or, as a flat property key:

```text
quartz.serializer.type = MyProject.CustomJsonSerializer, MyProject
```

The subclass reads the registry it was built with from the protected `Registry` property.

## Customizing calendar serialization

A custom calendar needs an `ICalendarSerializer`. The base class `CalendarSerializer` gives a strongly-typed one.

**Custom calendar and serializer**

<!-- snippet: sample_stj_custom_calendar -->
```csharp
using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson.Calendars;

public sealed class CustomCalendar : BaseCalendar
{
    public bool SomeCustomProperty { get; set; } = true;
}

// JSON serialization support
public sealed class CustomCalendarSerializer : CalendarSerializer<CustomCalendar>
{
    public override string CalendarTypeName => "CustomCalendar";

    protected override CustomCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new CustomCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, CustomCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteBoolean("SomeCustomProperty", calendar.SomeCustomProperty);
    }

    protected override void DeserializeFields(CustomCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        calendar.SomeCustomProperty = jsonElement.GetProperty("SomeCustomProperty").GetBoolean();
    }
}
```
<!-- endSnippet -->

## Customizing trigger serialization

A custom trigger works the same way, through `TriggerSerializer` from
`Quartz.Serialization.SystemTextJson.Triggers`. Without a serializer, a custom trigger is persisted as a
reflected blob that only the exact same type can read back.

## Registering custom serializers

Register both kinds in the `UseSystemTextJsonSerializer` callback:

<!-- snippet: sample_stj_register_custom_serializers -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer("my connection string");
    store.UseSystemTextJsonSerializer(json =>
    {
        json.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
        json.AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer());
    });
}));
```
<!-- endSnippet -->

::: warning Changed in 4.0
`SystemTextJsonObjectSerializer.AddCalendarSerializer` and `AddTriggerSerializer` were static in 3.x: every
scheduler in the process shared one set, and registration order decided which won. They are removed; use the
callback above.
:::

What the callback registers belongs to that scheduler alone, so two schedulers in one container can serialize
different custom types:

<!-- snippet: sample_stj_per_scheduler_serializers -->
```csharp
services.AddQuartz("reporting", q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(reportingDb);
    store.UseSystemTextJsonSerializer(json => json.AddTriggerSerializer<ReportTrigger>(new ReportTriggerSerializer()));
}));

services.AddQuartz("ingest", q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(ingestDb);
    store.UseSystemTextJsonSerializer(json => json.AddTriggerSerializer<IngestTrigger>(new IngestTriggerSerializer()));
}));
```
<!-- endSnippet -->

### Making custom serializers visible outside the job store

The [HTTP API](http-api), the [dashboard](dashboard) and `Quartz.HttpClient` serialize triggers too. They read
the container-wide registry, not a scheduler's callback. Register a serializer on the container to make it
visible everywhere:

<!-- snippet: sample_stj_container_registry -->
```csharp
services.AddSingleton(new SystemTextJsonSerializerRegistry()
    .AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer())
    .AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer()));

services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer("my connection string");
    // no callback: the store's serializer reads the container's registry, so the same custom
    // serializers apply to the job store, the HTTP API and the dashboard
    store.UseSystemTextJsonSerializer();
}));
```
<!-- endSnippet -->

- `SystemTextJsonSerializerRegistry` is in the `Quartz.Serialization.SystemTextJson` namespace.
- It always knows every built-in trigger and calendar type; a custom registration adds to that set.
- Both `Add*` methods return the registry, so registrations chain.

To give one scheduler its own registry directly (what the callback does internally):

<!-- snippet: sample_stj_keyed_registry -->
```csharp
services.AddKeyedSingleton("reporting", new SystemTextJsonSerializerRegistry()
    .AddTriggerSerializer<ReportTrigger>(new ReportTriggerSerializer()));
```
<!-- endSnippet -->

`Quartz.HttpClient` resolves the container's registry when registered with `AddQuartzHttpClient`. For a
`HttpScheduler` constructed by hand, pass one to its `serializerRegistry` parameter. A remote scheduler's
registrations cannot be discovered over HTTP: custom types are readable only if this process knows their
serializers.

## Publishing trimmed or native AOT

`PublishTrimmed` and `PublishAot` set `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault` to false,
so a type without metadata cannot be serialized. This serializer carries a source-generated contract for
everything Quartz writes:

- every trigger type and every calendar type;
- `CronExpression` and `NameValueCollection`;
- a `JobDataMap` holding any value type `DataMapExtensions` has an accessor for;
- every custom trigger and calendar registered with `AddTriggerSerializer<TTrigger>` and
  `AddCalendarSerializer<TCalendar>`, which know the type statically.

A **job-data value of your own type** needs metadata. Pass a generated `JsonSerializerContext` to the registry:

<!-- snippet: sample_stj_type_info_resolver -->
```csharp
// The metadata for this application's own job-data value types. Only a trimmed or native AOT
// publish needs it: with reflection on, the resolver chain still ends in reflection.
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer("my connection string");
    store.UseSystemTextJsonSerializer(json => json.AddTypeInfoResolver(JobDataContext.Default));
}));
```
<!-- endSnippet -->

Resolvers are asked in the order added, after Quartz's own contract and before reflection. `AddTypeInfoResolver`
can be called more than once and is safe whether or not the application is published trimmed.

The `Quartz.Serialization.Newtonsoft` serializer relies on reflection and has no metadata equivalent; a trimmed
application uses this one. `AddTypeInfoResolver` also declares a job-data value type the serializer will write
(both serializers refuse the same set). Newtonsoft's counterpart for that is
[`AddJobDataValueType<T>()`](json-serialization.md#what-a-job-data-map-may-hold).
