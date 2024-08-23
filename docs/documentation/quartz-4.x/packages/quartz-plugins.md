---

title : Plugins
---

[Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) provides some useful ready-made plugins for your convenience.

## Installation

You need to add NuGet package reference to your project which uses Quartz.

```shell
Install-Package Quartz.Plugins
```

## Configuration

Plugins are configured by using either DI configuration extensions or adding required configuration keys.

Configuration key in in format `quartz.plugin.{name-to-refer-with}.{property}`.

[See configuration reference](../configuration/reference.html#plug-ins) on how to configure each plugin

## Features

### LoggingJobHistoryPlugin

Logs a history of all job executions (and execution vetoes) and writes the entries to configured logging infrastructure.

### ShutdownHookPlugin

This plugin catches the event of the VM terminating (such as upon a CRTL-C) and tells the scheduler to Shutdown.

### XMLSchedulingDataProcessorPlugin

This plugin loads XML file(s) to add jobs and schedule them with triggers as the scheduler is initialized, and can optionally periodically scan the file for changes.

::: warning
The periodically scanning of files for changes is not currently supported in a clustered environment.
:::

### JobInterruptMonitorPlugin

This plugin catches the event of job running for a long time (more than the configured max time) and tells the scheduler to "try" interrupting it if enabled.

::: tip
Quartz 3.3 or later required.
:::

Each job configuration needs to have `JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable` key's value set to true in order for plugin to monitor the execution timeout.
Jobs can also define custom timeout value instead of global default by using key `JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime`.

```csharp
var job = JobBuilder.Create<SlowJob>()
    .WithIdentity("slowJob")
    .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, true)
    // allow only five seconds for this job, overriding default configuration
    .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, TimeSpan.FromSeconds(5).TotalMilliseconds.ToString()));
    .Build();
```
