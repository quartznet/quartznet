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

`UseTimeZoneConverter` registers a resolver with `Quartz.TimeZones`, which is what Quartz's own lookups go
through. Both spellings then resolve everywhere, and a stored trigger keeps firing after its scheduler moves
host.

## Installation

You need to add NuGet package reference to your project which uses Quartz.

```shell
dotnet add package Quartz.Plugins.TimeZoneConverter
```

## Using

<!-- snippet: sample_timezoneconverter_host -->
```csharp
builder.Services.AddQuartz(q => q.UseTimeZoneConverter());
```
<!-- endSnippet -->

`UseTimeZoneConverter` hangs off `IQuartzBuilder`, so the same call configures a scheduler built without a
host:

<!-- snippet: sample_timezoneconverter_standalone -->
```csharp
QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();
builder.UseTimeZoneConverter();

await using StandaloneSchedulerFactory schedulerFactory = builder.Build();
```
<!-- endSnippet -->

## There is no plugin, and no key

3.x shipped this as an `ISchedulerPlugin`, named from configuration by
`quartz.plugin.timeZoneConverter.type`. In 4.0 both are gone: `TimeZoneConverterPlugin` was one
`TimeZones.AddResolver` call wearing a plugin's lifecycle — no per-scheduler state, no scheduler to
depend on — so `UseTimeZoneConverter` performs the registration itself. A configuration file still
naming the plugin type fails to load it; delete the key and call `UseTimeZoneConverter` instead.

Two things follow from that, and both are improvements:

* **It takes effect while you are configuring, not when the scheduler starts.** Time zone lookup is
  reached from places that have no scheduler in scope — building a trigger, parsing a `CronExpression`,
  deserializing a trigger out of a job store — so a trigger built before the host starts now resolves
  its zone as well.
* **Nothing removes it again.** The plugin disposed its registration when its scheduler shut down, and
  had to be careful not to disturb the other schedulers in the process while doing so. One registration
  that outlives every scheduler is the same guarantee with none of the bookkeeping. Calling
  `UseTimeZoneConverter` for a second scheduler is a no-op.
