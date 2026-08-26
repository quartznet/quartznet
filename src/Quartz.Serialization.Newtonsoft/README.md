# Quartz.Serialization.Newtonsoft

[Quartz.Serialization.Newtonsoft](https://www.nuget.org/packages/Quartz.Serialization.Newtonsoft)
serializes what a Quartz.NET ADO.NET job store persists — job data maps, calendars and trigger state —
with [Json.NET](https://www.newtonsoft.com/json).

System.Text.Json serialization is built into the core
[Quartz](https://www.nuget.org/packages/Quartz) package and is the default, so reach for this one when
what is already in your database was written by Json.NET, or when your job data depends on how Json.NET
handles it. It is the successor to Quartz 3's `Quartz.Serialization.Json`.

## Installation

```shell
dotnet add package Quartz.Serialization.Newtonsoft
```

## Usage

<!-- snippet: sample_readme_newtonsoft -->
```csharp
builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);
    store.ConfigureStore(options => options.StoreJobDataAsStrings = true);
    store.UseNewtonsoftJsonSerializer();
}));
```
<!-- endSnippet -->

The same `UseNewtonsoftJsonSerializer` call configures a store built without a host, and the flat key
`quartz.serializer.type = newtonsoft` selects it from configuration.

`StoreJobDataAsStrings` is worth setting whichever serializer you use: it keeps job data out of the
serializer altogether, which is what avoids surprises when a persisted type later changes shape.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/json-serialization.html>
