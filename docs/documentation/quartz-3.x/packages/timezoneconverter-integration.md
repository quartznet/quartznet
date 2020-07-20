[Quartz.Plugins.TimeZoneConverter](https://www.nuget.org/packages/Quartz.Plugins.TimeZoneConverter)
provides integration with [TimeZoneConverter](https://github.com/mj1856/TimeZoneConverter) which helps to bridge between
*nix and Windows differences.

## Installation

You need to add NuGet package reference to your project which uses Quartz.

    Install-Package Quartz.Plugins.TimeZoneConverter

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
