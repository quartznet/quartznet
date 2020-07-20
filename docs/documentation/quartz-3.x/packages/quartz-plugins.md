[Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) provides some useful ready-mady plugins for your convenience.

## Installation

You need to add NuGet package reference to your project which uses Quartz.

    Install-Package Quartz.Plugins

## Features

### LoggingJobHistoryPlugin

Logs a history of all job executions (and execution vetoes) and writes the entries to configured logging infrastructure.

### ShutdownHookPlugin

This plugin catches the event of the VM terminating (such as upon a CRTL-C) and tells the scheduler to Shutdown.

### XMLSchedulingDataProcessorPlugin

This plugin loads XML file(s) to add jobs and schedule them with triggers as the scheduler is initialized, and can optionally periodically scan thefile for changes.

::: warning
The periodically scanning of files for changes is not currently supported in a clustered environment.
:::