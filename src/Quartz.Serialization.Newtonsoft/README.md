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

A job data value has to be one of the types `JobDataMap` declares an accessor for, or a
`Dictionary<string, string>` — the same set the System.Text.Json serializer accepts, so a blob written
here is one the other reader has an answer for. Anything else is refused when the job is stored, naming
the entry and the type; declare a type of your own with
`NewtonsoftJsonSerializerRegistry.AddJobDataValueType<T>()`.

## Trimming

This package is deliberately **not** trimmable, and does not declare `IsTrimmable`. Json.NET decides what
a type looks like by reflecting over it — a contract resolver reads the members of whatever it is handed
and constructs it — and there is no source-generated form of that to move to. Marking the package
trimmable would tell the trimmer it may remove members that a job data map is about to be deserialized
into, and the failure would arrive at run time when a job fires.

Publish trimmed or native AOT with the System.Text.Json serializer instead, which is built into the
`Quartz` package and is the default. It carries a source-generated contract for everything a job store
writes, and `SystemTextJsonSerializerRegistry.AddTypeInfoResolver` is where an application's own job data
value types are declared.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/json-serialization.html>
