---

title : Serialization (Newtonsoft Json.NET)
---

::: tip
JSON is the recommended persistent format for greenfield projects.
Also strongly consider setting useProperties to true, to restrict key-values to strings.
:::

::: tip
Consider [Quartz.Serialization.SystemTextJson](https://www.nuget.org/packages/Quartz.Serialization.SystemTextJson), which uses System.Text.Json.
:::

[Quartz.Serialization.Json](https://www.nuget.org/packages/Quartz.Serialization.Json) adds JSON serialization for job stores, using
[Json.NET](https://www.newtonsoft.com/json).

## Installation

```shell
Install-Package Quartz.Serialization.Json
```

## Configuring

**Classic property-based configuration**

```csharp
var properties = new NameValueCollection
{
 ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
 // "newtonsoft" and "json" are aliases for "Quartz.Simpl.JsonObjectSerializer, Quartz.Serialization.Json"
 // you should prefer "newtonsoft" as it's more explicit from Quartz 3.10 onwards
 ["quartz.serializer.type"] = "newtonsoft"
};
ISchedulerFactory schedulerFactory = new StdSchedulerFactory(properties);
```

**Configuring using scheduler builder**

```csharp
var config = SchedulerBuilder.Create();
config.UsePersistentStore(store =>
{
    // it's generally recommended to stick with
    // string property keys and values when serializing
    store.UseProperties = true;
    store.UseGenericDatabase(dbProvider, db =>
        db.ConnectionString = "my connection string"
    );

    store.UseNewtonsoftJsonSerializer();
});
ISchedulerFactory schedulerFactory = config.Build();
```

## Migrating from binary serialization

There is no official migration, because every setup has its quirks, but this recipe can work:

1. Configure a custom serializer, like `MigratorSerializer` below, that reads the binary format and writes JSON.
2. Let the system migrate gradually as it runs, or write a program that loads all relevant serialized assets and writes them back to the database.

**Example hybrid serializer**

```csharp
using Newtonsoft.Json;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz;

public sealed class MigratorSerializer : IObjectSerializer
{
    private readonly BinaryObjectSerializer binarySerializer;
    private readonly JsonObjectSerializer jsonSerializer;

    public MigratorSerializer()
    {
        binarySerializer = new BinaryObjectSerializer();
        // you might need custom configuration, see sections about customizing
        // in documentation
        jsonSerializer = new JsonObjectSerializer();
    }

    public T DeSerialize<T>(byte[] data) where T : class
    {
        try
        {
            // Attempt to deserialize data as JSON
            return jsonSerializer.DeSerialize<T>(data)!;
        }
        catch (JsonReaderException)
        {
            // Presumably, the data was not JSON, we instead use the binary serializer
            var binaryData = binarySerializer.DeSerialize<T>(data);
            if (binaryData is JobDataMap jobDataMap)
            {
                // make sure we mark the map as dirty so it will be serialized as JSON next time
                jobDataMap[SchedulerConstants.ForceJobDataMapDirty] = "true";
            }
            return binaryData!;
        }
    }

    public void Initialize()
    {
        binarySerializer.Initialize();
        jsonSerializer.Initialize();
    }

    public byte[] Serialize<T>(T obj) where T : class
    {
        return jsonSerializer.Serialize(obj);
    }
}
```

## Customizing JSON.NET

To customize JSON.NET settings, subclass the serializer and override `CreateSerializerSettings`.

```csharp
class CustomJsonSerializer : JsonObjectSerializer
{
    protected override JsonSerializerSettings CreateSerializerSettings()
    {
        var settings = base.CreateSerializerSettings();
        settings.Converters.Add(new MyCustomConverter());
        return settings;
    }
}
```

**Then configure it**

```csharp
store.UseSerializer<CustomJsonSerializer>();
// or
"quartz.serializer.type" = "MyProject.CustomJsonSerializer, MyProject"
```

## Customizing calendar serialization

A custom calendar needs an `ICalendarSerializer`. The base class `CalendarSerializer` makes it strongly typed.

**Custom calendar and serializer**

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

**Configuring custom calendar serializer**

```csharp
var config = SchedulerBuilder.Create();
config.UsePersistentStore(store =>
{
    store.UseJsonSerializer(json =>
    {
        json.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
    });
});

// or just globally which is what above code calls
JsonObjectSerializer.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
```
