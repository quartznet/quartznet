# Quartz.Plugins

[Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) provides ready-made
`ISchedulerPlugin` implementations: logging a history of job and trigger events, loading jobs and
triggers from a file at startup, interrupting jobs that overrun, and shutting the scheduler down
cleanly when the process exits.

| Plugin | Extension |
|---|---|
| `StructuredLoggingJobHistoryPlugin` / `StructuredLoggingTriggerHistoryPlugin` | `UseStructuredJobLogging(…)` / `UseStructuredTriggerLogging(…)` |
| `LoggingJobHistoryPlugin` / `LoggingTriggerHistoryPlugin` | `UseJobHistoryLogging(…)` / `UseTriggerHistoryLogging(…)` |
| `JsonSchedulingDataProcessorPlugin` / `XmlSchedulingDataProcessorPlugin` | `UseJsonSchedulingConfiguration(…)` / `UseXmlSchedulingConfiguration(…)` |
| `JobInterruptMonitorPlugin` | `UseJobAutoInterrupt(…)` |
| `ShutdownHookPlugin` | `UseShutdownHook(…)` |

## Installation

```shell
dotnet add package Quartz.Plugins
```

## Usage

The extensions hang off `IQuartzBuilder`, so the same calls configure a scheduler with or without a
host:

<!-- snippet: sample_readme_plugins -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UseStructuredJobLogging();
    q.UseStructuredTriggerLogging();
    q.UseJobAutoInterrupt(options => options.DefaultMaxRunTime = TimeSpan.FromMinutes(5));
});
```
<!-- endSnippet -->

The flat `quartz.plugin.{name}.{property}` keys Quartz 3 used still work and mean the same thing, and
`AddPlugin<TPlugin>()` registers a plugin of your own so the container constructs it.

These plugins live in the `Quartz.Plugins.*` namespaces. In Quartz 3 they were the singular
`Quartz.Plugin.*`; a `quartz.plugin.<name>.type` naming the old spelling still resolves, with a warning.

JSON is the maintained format for a schedule kept in a file. The XML format is frozen at
`job_scheduling_data_2_0.xsd` — simple, cron and calendar-interval triggers — and will not gain the
trigger kinds or trigger settings added since. Existing files keep working; write a new schedule as
JSON.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/quartz-plugins.html>
