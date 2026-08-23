# Quartz.Plugins.TimeZoneConverter

[Quartz.Plugins.TimeZoneConverter](https://www.nuget.org/packages/Quartz.Plugins.TimeZoneConverter)
plugs [TimeZoneConverter](https://github.com/mj1856/TimeZoneConverter) into Quartz.NET's time zone
lookup, so that both Windows ids (`Central America Standard Time`) and IANA ids
(`America/Guatemala`) resolve on either operating system.

Every trigger that names a time zone names it by id, and `TimeZoneInfo.FindSystemTimeZoneById` answers
with whatever ids the machine happens to know. A schedule written on Windows and run on Linux therefore
throws `TimeZoneNotFoundException` where the trigger is built or read back — and a schedule kept in a
database is exactly the one that gets moved between them.

## Installation

```shell
dotnet add package Quartz.Plugins.TimeZoneConverter
```

## Usage

<!-- snippet: sample_readme_timezoneconverter -->
```csharp
builder.Services.AddQuartz(q => q.UseTimeZoneConverter());
```
<!-- endSnippet -->

`UseTimeZoneConverter` hangs off `IQuartzBuilder`, so the same call configures a scheduler built
without a host. The flat key `quartz.plugin.timeZoneConverter.type` does the same from configuration.

Adding the plugin registers a resolver with `Quartz.TimeZones`, which is what Quartz's own lookups go
through: both spellings then resolve everywhere, and a stored trigger keeps firing after its scheduler
moves host.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/timezoneconverter-integration.html>
