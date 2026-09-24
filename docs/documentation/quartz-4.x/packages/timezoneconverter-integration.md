---

title: TimeZoneConverter Integration
---

[Quartz.Plugins.TimeZoneConverter](https://www.nuget.org/packages/Quartz.Plugins.TimeZoneConverter)
plugs [TimeZoneConverter](https://github.com/mj1856/TimeZoneConverter) into Quartz's time zone lookup. Windows
ids (`Central America Standard Time`) and IANA ids (`America/Guatemala`) then resolve on either operating system.

## Why you would want it

`TimeZoneInfo.FindSystemTimeZoneById` knows Windows ids on Windows and IANA ids on Linux and macOS. Recent
.NET converts between them only where the operating system has the data. A schedule written on one and run on
the other throws `TimeZoneNotFoundException` when a trigger is built or read back. Schedules stored in a
database are the ones that move between hosts.

`UseTimeZoneConverter` registers a resolver with `Quartz.TimeZones`, which Quartz's own lookups go through.
Both spellings then resolve everywhere, and a stored trigger keeps firing after its scheduler moves host.

## Installation

```shell
dotnet add package Quartz.Plugins.TimeZoneConverter
```

## Using

<!-- snippet: sample_timezoneconverter_host -->
```csharp
builder.Services.AddQuartz(q => q.UseTimeZoneConverter());
```
<!-- endSnippet -->

`UseTimeZoneConverter` is on `IQuartzBuilder`, so the same call works without a host:

<!-- snippet: sample_timezoneconverter_standalone -->
```csharp
await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder
    .Create(q => q.UseTimeZoneConverter())
    .Build();
```
<!-- endSnippet -->

## There is no plugin, and no key

3.x shipped this as an `ISchedulerPlugin` named by `quartz.plugin.timeZoneConverter.type`. 4.0 removed both
the `TimeZoneConverterPlugin` type and the key; `UseTimeZoneConverter` makes the plugin's one
`TimeZones.AddResolver` call itself. A configuration file that still names the plugin type fails to load it:
delete the key and call `UseTimeZoneConverter` instead.

* **It takes effect during configuration, not at scheduler start.** Building a trigger, parsing a
  `CronExpression` and deserializing a trigger from a job store all resolve zones with no scheduler in scope.
  A trigger built before the host starts resolves its zone too.
* **Nothing removes it.** The registration outlives every scheduler in the process. Calling
  `UseTimeZoneConverter` for a second scheduler is a no-op.
