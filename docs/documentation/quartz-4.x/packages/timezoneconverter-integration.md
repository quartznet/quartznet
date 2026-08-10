---

title: TimeZoneConverter Integration
---

[Quartz.Plugins.TimeZoneConverter](https://www.nuget.org/packages/Quartz.Plugins.TimeZoneConverter)
provides integration with [TimeZoneConverter](https://github.com/mj1856/TimeZoneConverter) which helps to bridge between
*nix and Windows differences.

## Installation

You need to add NuGet package reference to your project which uses Quartz.

```shell
Install-Package Quartz.Plugins.TimeZoneConverter
```

## Using

**Classic property-based configuration**

```csharp
var properties = new NameValueCollection
{
 ["quartz.plugin.timeZoneConverter.type"] = "Quartz.Plugins.TimeZoneConverter.TimeZoneConverterPlugin, Quartz.Plugins.TimeZoneConverter"
};
ISchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
    .UseProperties(properties)
    .Build();
```

**Configuring using scheduler builder**

```csharp
var builder = QuartzSchedulerBuilder.Create();
builder.UseTimeZoneConverter();

ISchedulerFactory schedulerFactory = builder.Build();
```
