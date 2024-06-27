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

There's now official solution for migration as there can be quirks in every setup, but there's a recipe that can work for you.

* Configure custom serializer like `MigratorSerializer` below that can read binary serialization format and writes JSON format
* Either let system gradually migrate as it's running or create a program which loads and writes back to DB all relevant serialized assets

**Example hybrid serializer**

```csharp
public class MigratorSerializer : IObjectSerializer
{
    private BinaryObjectSerializer binarySerializer;
    private JsonObjectSerializer jsonSerializer;

    public MigratorSerializer()
    {
        this.binarySerializer = new BinaryObjectSerializer();
        // you might need custom configuration, see sections about customizing
        // in documentation
        this.jsonSerializer = new JsonObjectSerializer();
    }

    public T DeSerialize<T>(byte[] data) where T : class
    {
        try
        {
            // Attempt to deserialize data as JSON
            var result = this.jsonSerializer.DeSerialize<T>(data);
            return result;
        }
        catch (JsonReaderException)
        {
            // Presumably, the data was not JSON, we instead use the binary serializer
            return this.binarySerializer.DeSerialize<T>(data);
        }
    }

    public void Initialize()
    {
        this.binarySerializer.Initialize();
        this.jsonSerializer.Initialize();
    }

    public byte[] Serialize<T>(T obj) where T : class
    {
        return this.jsonSerializer.Serialize<T>(obj);
    }
}
```

### Customizing JSON.NET

If you need to customize JSON.NET settings, you need to inherit custom implementation and override `CreateSerializerSettings`.

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
JsonObjectSerializer.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
```
