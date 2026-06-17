# Quartz.Plugins

[Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) provides a set of ready-made plugins for common scheduling needs.

## Installation

```shell
dotnet add package Quartz.Plugins
```

## Included plugins

* **LoggingJobHistoryPlugin** / **LoggingTriggerHistoryPlugin** — log a history of job executions and trigger firings.
* **StructuredLoggingJobHistoryPlugin** / **StructuredLoggingTriggerHistoryPlugin** — structured-logging equivalents using named message-template parameters (Serilog, NLog, …); recommended when using structured logging sinks.
* **XMLSchedulingDataProcessorPlugin** / **JsonSchedulingDataProcessorPlugin** — load jobs and triggers from XML or JSON files, optionally re-scanning for changes.
* **ShutdownHookPlugin** — shuts the scheduler down when the process terminates.
* **JobInterruptMonitorPlugin** — interrupts jobs that run longer than a configured maximum (Quartz 3.3+).

## Usage

Plugins are enabled via DI extensions or configuration keys (`quartz.plugin.{name}.{property}`):

```csharp
services.AddQuartz(q =>
{
    q.UseStructuredJobLogging();
    q.UseStructuredTriggerLogging();
});
```

## Documentation

📖 Full documentation and per-plugin configuration: <https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/quartz-plugins.html>
