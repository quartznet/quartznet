# Quartz.Serialization.Json

[Quartz.Serialization.Json](https://www.nuget.org/packages/Quartz.Serialization.Json) provides JSON serialization for ADO.NET job stores using [Json.NET](https://www.newtonsoft.com/json).

> **Tip:** For new projects consider [Quartz.Serialization.SystemTextJson](https://www.nuget.org/packages/Quartz.Serialization.SystemTextJson), which uses `System.Text.Json`. JSON is the recommended persistent format; also set `UseProperties = true` to keep job data map values as strings.

## Installation

```shell
dotnet add package Quartz.Serialization.Json
```

## Usage

Using the scheduler builder:

```csharp
var config = SchedulerBuilder.Create();
config.UsePersistentStore(store =>
{
    store.UseProperties = true;
    store.UseGenericDatabase(dbProvider, db => db.ConnectionString = "my connection string");
    store.UseNewtonsoftJsonSerializer();
});
```

Or via properties (`newtonsoft` is the preferred alias from Quartz 3.10 onwards):

```text
quartz.serializer.type = newtonsoft
```

## Documentation

📖 Full documentation, including migrating from binary serialization and customizing serialization: <https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/json-serialization.html>
