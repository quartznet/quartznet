---

title : JSON Serialization
---

::: tip
JSON is recommended persistent format to store data in database for greenfield projects.
You should also strongly consider setting useProperties to true to restrict key-values to be strings.
:::

::: tip
You might want to consider using [System.Text.Json](../packages/system-text-json) for JSON serialization.
:::

## JSON.NET

[Quartz.Serialization.Newtonsoft](https://www.nuget.org/packages/Quartz.Serialization.Newtonsoft) provides JSON serialization support for job stores using
[Json.NET](https://www.newtonsoft.com/json) to handle the actual serialization process.

### Installation

You need to add NuGet package reference to your project which uses Quartz.

```shell
Install-Package Quartz.Serialization.Newtonsoft
```

### Configuring

**Classic property-based configuration**

```csharp
var properties = new NameValueCollection
{
 ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
 ["quartz.serializer.type"] = "json"
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

### Migrating from binary serialization

Quartz 4 no longer ships the `BinaryObjectSerializer`: the underlying `BinaryFormatter`
has been removed from modern .NET and throws on .NET 9 and later. If you still have
binary-serialized data in your database you need to migrate it to JSON.

The recommended path is to perform the migration **while you are still on Quartz 3.x**,
which still includes `BinaryObjectSerializer` - see the Quartz 3.x version of this page
for a ready-made hybrid serializer. Either let the system migrate gradually as it runs,
or write a small program that loads and writes back every serialized asset in the
database.

If you must read legacy binary data on Quartz 4, the Quartz types still keep their
`[Serializable]` / `ISerializable` support, so you can plug in your own `IObjectSerializer`
that decodes the old binary payloads and let the hybrid serializer below write everything
back as JSON.

**Example hybrid serializer**

```csharp
using Newtonsoft.Json;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz;

public sealed class MigratorSerializer : IObjectSerializer
{
    // Quartz 4 no longer provides a binary serializer, so supply your own reader for
    // the legacy format (for example one backed by BinaryFormatter on a runtime that
    // still supports it, or a hand-written System.Formats.Nrbf reader).
    private readonly IObjectSerializer legacyBinarySerializer;
    private readonly NewtonsoftJsonObjectSerializer jsonSerializer;

    public MigratorSerializer(IObjectSerializer legacyBinarySerializer)
    {
        this.legacyBinarySerializer = legacyBinarySerializer;
        // you might need custom configuration, see sections about customizing
        // in documentation
        jsonSerializer = new NewtonsoftJsonObjectSerializer();
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
            var binaryData = legacyBinarySerializer.DeSerialize<T>(data);
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
        legacyBinarySerializer.Initialize();
        jsonSerializer.Initialize();
    }

    public byte[] Serialize<T>(T obj) where T : class
    {
        return jsonSerializer.Serialize(obj);
    }
}
```

### Customizing JSON.NET

If you need to customize JSON.NET settings, you need to inherit custom implementation and override `CreateSerializerSettings`.

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

**And then configure it to use**

```csharp
store.UseSerializer<CustomJsonSerializer>();
// or
"quartz.serializer.type" = "MyProject.CustomJsonSerializer, MyProject"
```

### Customizing calendar serialization

If you have implemented a custom calendar, you need to implement a `ICalendarSerializer` for it.
There's a convenience base class `CalendarSerializer` that you can use the get strongly-typed experience.

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
    store.UseNewtonsoftJsonSerializer(json =>
    {
        json.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
    });
});

// or just globally which is what above code calls
NewtonsoftJsonObjectSerializer.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
```
