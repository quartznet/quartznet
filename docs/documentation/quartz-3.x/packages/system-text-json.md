---

title : Serialization (System.Text.Json)
---

::: tip
JSON is recommended persistent format to store data in database for greenfield projects.
You should also strongly consider setting useProperties to true to restrict key-values to be strings.
:::

[Quartz.Serialization.SystemTextJson](https://www.nuget.org/packages/Quartz.Serialization.SystemTextJson) provides JSON serialization support for job stores using
System.Text.Json facilities to handle the actual serialization process.

## Installation

You need to add NuGet package reference to your project which uses Quartz.

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

There's now official solution for migration as there can be quirks in every setup, but there's a recipe that can work for you.

* Configure custom serializer like `MigratorSerializer` below that can read binary serialization format and writes JSON format
* Either let system gradually migrate as it's running or create a program which loads and writes back to DB all relevant serialized assets

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

 If you need to customize serialization, you need to inherit custom implementation and override `CreateSerializerOptions`.

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

**And then configure it to use**

```csharp
store.UseSerializer<CustomJsonSerializer>();
// or
"quartz.serializer.type" = "MyProject.CustomJsonSerializer, MyProject"
```

## Customizing calendar serialization

If you have implemented a custom calendar, you need to implement a `ICalendarSerializer` for it.
There's a convenience base class `CalendarSerializer` that you can use the get strongly-typed experience.

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
