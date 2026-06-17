# Quartz.Serialization.SystemTextJson

[Quartz.Serialization.SystemTextJson](https://www.nuget.org/packages/Quartz.Serialization.SystemTextJson) provides JSON serialization for ADO.NET job stores using `System.Text.Json`.

> **Tip:** JSON is the recommended persistent format for new projects. Also set `UseProperties = true` to keep job data map values as strings.

## Installation

```shell
dotnet add package Quartz.Serialization.SystemTextJson
```

## Usage

Using the scheduler builder:

```csharp
var config = SchedulerBuilder.Create();
config.UsePersistentStore(store =>
{
    store.UseProperties = true;
    store.UseGenericDatabase(dbProvider, db => db.ConnectionString = "my connection string");
    store.UseSystemTextJsonSerializer();
});
```

Or via properties:

```text
quartz.serializer.type = stj
```

## Documentation

📖 Full documentation, including migrating from binary serialization and customizing serialization: <https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/system-text-json.html>
