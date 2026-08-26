---

title: Serialization (System.Text.Json)
---

::: tip
JSON is the recommended persistent format to store data in a database for greenfield projects.
You should also strongly consider setting `StoreJobDataAsStrings` to true to restrict key-values to be strings.
:::

System.Text.Json serialization is built into the `Quartz` package - there is no separate
`Quartz.Serialization.SystemTextJson` package to reference any more, and it is the serializer a
persistent store gets when nothing else is configured.

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

Quartz 4 no longer ships the `BinaryObjectSerializer`. See
[JSON Serialization](json-serialization#migrating-from-binary-serialization) for the migration recipe;
it applies to System.Text.Json the same way.

## Customizing serialization options

If you need to customize serialization, inherit a custom implementation and override
`CreateSerializerOptions`.

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

The registry the serializer was built with is available to the subclass through the protected `Registry`
property.

## Customizing calendar serialization

If you have implemented a custom calendar, you need to implement an `ICalendarSerializer` for it.
There's a convenience base class `CalendarSerializer` that gives you a strongly-typed experience.

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

A custom trigger type works the same way, through `TriggerSerializer` from
`Quartz.Serialization.SystemTextJson.Triggers`. Without a serializer a custom trigger is persisted as a reflected
blob, which can be read back only by the exact same type.

## Registering custom serializers

Both kinds are registered through the `UseSystemTextJsonSerializer` callback:

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
`SystemTextJsonObjectSerializer.AddCalendarSerializer` and `AddTriggerSerializer` were static in 3.x, so
every scheduler in the process shared one set of custom serializers and registration order silently
decided which one won. They have been removed - use the callback above.
:::

**What the callback registers belongs to that scheduler alone.** This is the point of the change: two
schedulers in one container can now serialize different custom types.

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

Serializing a trigger is not something only the job store does: the [HTTP API](http-api),
the [dashboard](dashboard) and `Quartz.HttpClient` all serialize triggers too, and none of them belongs to
a single scheduler. They read the container-wide registry instead, so a serializer that only one
scheduler's callback knows about is invisible to them. Register it on the container to make it visible
everywhere:

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

`SystemTextJsonSerializerRegistry` lives in the `Quartz.Serialization.SystemTextJson` namespace. It always starts
out knowing every built-in trigger and calendar type, so registering a custom one adds to that set rather
than replacing it. Both `Add*` methods return the registry, so registrations chain.

A single scheduler can also be given its own registry directly, which is the same thing the callback does
under the hood:

<!-- snippet: sample_stj_keyed_registry -->
```csharp
services.AddKeyedSingleton("reporting", new SystemTextJsonSerializerRegistry()
    .AddTriggerSerializer<ReportTrigger>(new ReportTriggerSerializer()));
```
<!-- endSnippet -->

`Quartz.HttpClient` resolves the container's registry when the scheduler is registered with
`AddQuartzHttpClient`; when a `HttpScheduler` is constructed by hand, pass one to its `serializerRegistry`
parameter. A remote scheduler's own registrations cannot be discovered over HTTP, so custom types are only
readable if this process knows their serializers.

## Publishing trimmed or native AOT

`PublishTrimmed` and `PublishAot` set `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault` to
false, so a type nobody has written metadata for cannot be serialized at all. This serializer carries a
source-generated contract for everything Quartz writes — every trigger type, every calendar type,
`CronExpression`, `NameValueCollection`, and a `JobDataMap` holding any of the value types
`DataMapExtensions` declares an accessor for — and the registry answers for every custom trigger and
calendar type registered with it, because `AddTriggerSerializer<TTrigger>` and
`AddCalendarSerializer<TCalendar>` know the type statically.

What is left is a **job-data value of a type of your own**. Hand the registry the metadata for it, as a
generated `JsonSerializerContext`:

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

Resolvers are asked in the order they were added, behind Quartz's own contract and in front of
reflection, so `AddTypeInfoResolver` can be called more than once and is safe to configure whether or
not the application is published trimmed.

The `Quartz.Serialization.Newtonsoft` serializer has no equivalent: it is reflection by nature, so an
application that publishes trimmed uses this one.
