---

title : Serialization (System.Text.Json)
---

::: tip
JSON is the recommended persistent format for greenfield projects.
Also strongly consider setting useProperties to true, to restrict key-values to strings.
:::

[Quartz.Serialization.SystemTextJson](https://www.nuget.org/packages/Quartz.Serialization.SystemTextJson) adds JSON serialization for job stores, using
System.Text.Json.

## Installation

```powershell
    Install-Package Quartz.Serialization.SystemTextJson
```

## Configuring

**Classic property-based configuration**

```csharp
var properties = new NameValueCollection
{
 ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
 ["quartz.serializer.type"] = "stj"
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

    store.UseSystemTextJsonSerializer();
});
ISchedulerFactory schedulerFactory = config.Build();
```

## Migrating from binary serialization

There is no official migration, because every setup has its quirks, but this recipe can work:

1. Configure a custom serializer, like `MigratorSerializer` below, that reads the binary format and writes JSON.
2. Let the system migrate gradually as it runs, or write a program that loads all relevant serialized assets and writes them back to the database.

**Example hybrid serializer**

```csharp
using System.Text.Json;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz;

public sealed class MigratorSerializer : IObjectSerializer
{
    private readonly BinaryObjectSerializer binarySerializer;
    private readonly SystemTextJsonObjectSerializer jsonSerializer;

    public MigratorSerializer()
    {
        binarySerializer = new BinaryObjectSerializer();
        // you might need custom configuration, see sections about customizing
        // in documentation
        jsonSerializer = new SystemTextJsonObjectSerializer();
    }

    public T DeSerialize<T>(byte[] data) where T : class
    {
        try
        {
            // Attempt to deserialize data as JSON
            return jsonSerializer.DeSerialize<T>(data)!;
        }
        catch (JsonException)
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

## Customizing serialization options

To customize serialization, subclass the serializer and override `CreateSerializerOptions`.

 ```csharp
class CustomJsonSerializer : SystemTextJsonObjectSerializer
{
    protected override JsonSerializerOptions CreateSerializerOptions()
    {
        var options = base.CreateSerializerOptions();
        options.Converters.Add(new MyCustomConverter());
        return options;
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
using System;
using System.Runtime.Serialization;
using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson;

[Serializable]
public sealed class CustomCalendar : BaseCalendar
{
    public CustomCalendar()
    {
    }

    // binary serialization support
    private CustomCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
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
public sealed class CustomCalendarSerializer : CalendarSerializer<CustomCalendar>
{
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
        calendar.SomeCustomProperty = jsonElement.GetProperty("CustomProperty").GetBoolean();
    }

    public override string CalendarTypeName => "CustomCalendar";
}
```

**Configuring custom calendar serializer**

```csharp
var config = SchedulerBuilder.Create();
config.UsePersistentStore(store =>
{
    store.UseSystemTextJsonSerializer(json =>
    {
        json.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
    });
});

// or just globally which is what above code calls
SystemTextJsonObjectSerializer.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
```
