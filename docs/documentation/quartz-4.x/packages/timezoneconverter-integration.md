---

title: TimeZoneConverter Integration
---

[Quartz.Plugins.TimeZoneConverter](https://www.nuget.org/packages/Quartz.Plugins.TimeZoneConverter)
plugs [TimeZoneConverter](https://github.com/mj1856/TimeZoneConverter) into Quartz's time zone lookup, so that
both Windows ids (`Central America Standard Time`) and IANA ids (`America/Guatemala`) resolve on either
operating system.

## Why you would want it

Every trigger that names a time zone names it by id, and `TimeZoneInfo.FindSystemTimeZoneById` answers with
whatever ids the machine happens to know: Windows ids on Windows, IANA ids on Linux and macOS — with recent
.NET able to convert between them only where the operating system has the data to do it. A schedule written on
one and run on the other therefore throws `TimeZoneNotFoundException` at the point where a trigger is built or
read back, and a schedule stored in a database is exactly the schedule that gets moved between them.

Adding the plugin registers a resolver with `Quartz.TimeZones`, which is what Quartz's own lookups go through.
Both spellings then resolve everywhere, and a stored trigger keeps firing after its scheduler moves host.

## Installation

You need to add NuGet package reference to your project which uses Quartz.

```shell
dotnet add package Quartz.Plugins.TimeZoneConverter
```

## Using

```csharp
builder.Services.AddQuartz(q => q.UseTimeZoneConverter());
```

`UseTimeZoneConverter` hangs off `IQuartzBuilder`, so the same call configures a scheduler built without a
host:

```csharp
await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
    .UseTimeZoneConverter()
    .Build();
```

**Classic property-based configuration**

```csharp
NameValueCollection properties = new()
{
    ["quartz.plugin.timeZoneConverter.type"] = "Quartz.Plugins.TimeZoneConverter.TimeZoneConverterPlugin, Quartz.Plugins.TimeZoneConverter"
};

await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
    .UseProperties(properties)
    .Build();
```
