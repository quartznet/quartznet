---

title : Serialization (System.Text.Json)
---

::: tip
JSON is the recommended persistent format to store data in a database for greenfield projects.
You should also strongly consider setting `UseProperties` to true to restrict key-values to be strings.
:::

System.Text.Json serialization is built into the `Quartz` package - there is no separate
`Quartz.Serialization.SystemTextJson` package to reference any more, and it is the serializer a
persistent store gets when nothing else is configured.

## Configuring

**Code-first configuration**

```csharp
services.AddQuartz(q =>
{
    q.UsePersistentStore(store =>
    {
        store.UseSqlServer("my connection string");

        // it's generally recommended to stick with
        // string property keys and values when serializing
        store.Configure(options => options.UseProperties = true);

        store.UseSystemTextJsonSerializer();
    });
});
```

**Classic property-based configuration**

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

## Migrating from binary serialization

Quartz 4 no longer ships the `BinaryObjectSerializer`. See
[JSON Serialization](json-serialization#migrating-from-binary-serialization) for the migration recipe;
it applies to System.Text.Json the same way.

## Customizing serialization options

If you need to customize serialization, inherit a custom implementation and override
`CreateSerializerOptions`.

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

**And then configure it to use**

```csharp
store.UseSerializer<CustomJsonSerializer>();
// or
"quartz.serializer.type" = "MyProject.CustomJsonSerializer, MyProject"
```

The registry the serializer was built with is available to the subclass through the protected `Registry`
property.

## Customizing calendar serialization

If you have implemented a custom calendar, you need to implement an `ICalendarSerializer` for it.
There's a convenience base class `CalendarSerializer` that gives you a strongly-typed experience.

**Custom calendar and serializer**

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

## Customizing trigger serialization

A custom trigger type works the same way, through `TriggerSerializer` from
`Quartz.Serialization.SystemTextJson.Triggers`. Without a serializer a custom trigger is persisted as a reflected
blob, which can be read back only by the exact same type.

## Registering custom serializers

Both kinds are registered through the `UseSystemTextJsonSerializer` callback:

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

::: warning Changed in 4.0
`SystemTextJsonObjectSerializer.AddCalendarSerializer` and `AddTriggerSerializer` were static in 3.x, so
every scheduler in the process shared one set of custom serializers and registration order silently
decided which one won. They have been removed - use the callback above.
:::

**What the callback registers belongs to that scheduler alone.** This is the point of the change: two
schedulers in one container can now serialize different custom types.

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

### Making custom serializers visible outside the job store

Serializing a trigger is not something only the job store does: the [HTTP API](http-api),
the [dashboard](dashboard) and `Quartz.HttpClient` all serialize triggers too, and none of them belongs to
a single scheduler. They read the container-wide registry instead, so a serializer that only one
scheduler's callback knows about is invisible to them. Register it on the container to make it visible
everywhere:

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

`SystemTextJsonSerializerRegistry` lives in the `Quartz.Serialization.SystemTextJson` namespace. It always starts
out knowing every built-in trigger and calendar type, so registering a custom one adds to that set rather
than replacing it. Both `Add*` methods return the registry, so registrations chain.

A single scheduler can also be given its own registry directly, which is the same thing the callback does
under the hood:

```csharp
services.AddKeyedSingleton("reporting", new SystemTextJsonSerializerRegistry()
    .AddTriggerSerializer<ReportTrigger>(new ReportTriggerSerializer()));
```

`Quartz.HttpClient` resolves the container's registry when the scheduler is registered with
`AddQuartzHttpClient`; when a `HttpScheduler` is constructed by hand, pass one to its `serializerRegistry`
parameter. A remote scheduler's own registrations cannot be discovered over HTTP, so custom types are only
readable if this process knows their serializers.
