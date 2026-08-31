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
without a host.

The call registers a resolver with `Quartz.TimeZones`, which is what Quartz's own lookups go through:
both spellings then resolve everywhere, and a stored trigger keeps firing after its scheduler moves
host. The registration is process-wide and takes effect at once, so a trigger built before the
scheduler exists resolves its zone too. It is not a plugin — 4.0 retired the one this package used to
ship, along with the `quartz.plugin.timeZoneConverter.type` key that named it.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/timezoneconverter-integration.html>
