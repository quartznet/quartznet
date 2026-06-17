# Quartz.Plugins.TimeZoneConverter

[Quartz.Plugins.TimeZoneConverter](https://www.nuget.org/packages/Quartz.Plugins.TimeZoneConverter) integrates [TimeZoneConverter](https://github.com/mj1856/TimeZoneConverter) so time zone ids resolve consistently across Windows and *nix.

## Installation

```shell
dotnet add package Quartz.Plugins.TimeZoneConverter
```

## Usage

Using the scheduler builder:

```csharp
var config = SchedulerBuilder.Create()
    .UseTimeZoneConverter();

ISchedulerFactory schedulerFactory = config.Build();
```

Or via configuration properties:

```text
quartz.plugin.timeZoneConverter.type = Quartz.Plugin.TimeZoneConverter.TimeZoneConverterPlugin, Quartz.Plugins.TimeZoneConverter
```

## Documentation

📖 Full documentation: <https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/timezoneconverter-integration.html>
