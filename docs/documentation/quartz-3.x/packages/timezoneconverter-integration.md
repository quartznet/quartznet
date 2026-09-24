---

title: TimeZoneConverter Integration
---

[Quartz.Plugins.TimeZoneConverter](https://www.nuget.org/packages/Quartz.Plugins.TimeZoneConverter)
integrates Quartz with [TimeZoneConverter](https://github.com/mj1856/TimeZoneConverter), which bridges the
*nix and Windows time zone differences.

## Installation

```shell
Install-Package Quartz.Plugins.TimeZoneConverter
```

## Using

**Classic property-based configuration**

```csharp
var properties = new NameValueCollection
{
 ["quartz.plugin.timeZoneConverter.type"] = "Quartz.Plugin.TimeZoneConverter.TimeZoneConverterPlugin, Quartz.Plugins.TimeZoneConverter"
};
ISchedulerFactory schedulerFactory = new StdSchedulerFactory(properties);
```

**Configuring using scheduler builder**

```csharp
var config = SchedulerBuilder.Create()
    .UseTimeZoneConverter();
ISchedulerFactory schedulerFactory = config.Build();
```
