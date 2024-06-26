::: tip
JSON is recommended persistent format to store data in database for greenfield projects.
You should also strongly consider setting useProperties to true to restrict key-values to be strings.
:::

[Quartz.Serialization.SystemTextJson](https://www.nuget.org/packages/Quartz.Serialization.SystemTextJson) provides JSON serialization support for job stores using 
System.Text.Json facilities to handle the actual serialization process.

## Installation

You need to add NuGet package reference to your project which uses Quartz.

    Install-Package Quartz.Serialization.SystemTextJson

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
public class MigratorSerializer : IObjectSerializer
{
    private BinaryObjectSerializer binarySerializer;
    private SystemTextJsonObjectSerializer jsonSerializer;

    public MigratorSerializer()
    {
        this.binarySerializer = new BinaryObjectSerializer();
        // you might need custom configuration, see sections about customizing
        // in documentation
        this.jsonSerializer = new SystemTextJsonObjectSerializer();
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

## Customizing JSON.NET
 
 If you need to customize JSON.NET settings, you need to inherit custom implementation and override `CreateSerializerSettings`.
 
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
