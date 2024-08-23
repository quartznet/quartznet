---

title: 'Configuration, Resource Usage and SchedulerFactory'
---

# Configuration, Resource Usage and SchedulerFactory

Quartz is designed in modular way, and therefore to get it running, several components need to be "snapped" together.
Fortunately, some helpers exist for making this happen.

The major components that need to be configured before Quartz can do its work are:

* ThreadPool
* JobStore
* DataSources (if necessary)
* The Scheduler itself

Thread pooling has changed a lot since the Task-based jobs were introduced.
Now the default implementation, `DefaultThreadPool` uses [CLR's managed thread pool](https://docs.microsoft.com/en-us/dotnet/standard/threading/the-managed-thread-pool) to execute jobs as tasks.
You can configure the pool that have max concurrency, which effectively limits how many concurrent tasks can be scheduled to the CLR's thread pool.
See configuration reference for more details on how to configure the thread pool implementation.

JobStores and DataSources were discussed in Lesson 9 of this tutorial. Worth noting here, is the fact that all JobStores
implement the `IJobStore` interface - and that if one of the bundled JobStores does not fit your needs, then you can make your own.

Finally, you need to create your Scheduler instance. The Scheduler itself needs to be given a name and handed
instances of a JobStore and ThreadPool.

## StdSchedulerFactory

`StdSchedulerFactory` is an implementation of the `ISchedulerFactory` interface.
It uses a set of properties (`NameValueCollection`) to create and initialize a Quartz Scheduler.
The properties are generally stored in and loaded from a file, but can also be created by your program and handed directly to the factory.
Simply calling `GetScheduler()` on the factory will produce the scheduler, initialize it (and its ThreadPool, JobStore and DataSources),
and return a handle to its public interface.

You can find complete documentation in the "Configuration Reference" section of the Quartz documentation.

## DirectSchedulerFactory

`DirectSchedulerFactory` is another `ISchedulerFactory` implementation. It is useful to those wishing to create their Scheduler
instance in a more programmatic way. Its use is generally discouraged for the following reasons:

* It requires the user to have a greater understanding of what they're doing, and
* it does not allow for declarative configuration - or in other words, you end up hard-coding all of the scheduler's settings.

## Logging

::: tip
As of Quartz.NET 3.1, you can configure [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/) to be used instead of LibLog.
:::

### LibLog

Quartz.NET uses [LibLog](https://github.com/damianh/LibLog) library for all of its logging needs.
Quartz does not produce much logging information - generally just some information during initialization, and
then only messages about serious problems while Jobs are executing. In order to "tune" the logging settings
(such as the amount of output, and where the output goes), you need to actually configure your logging framework of choice as LibLog mostly delegates the work to
more full-fledged logging framework like log4net, SeriLog etc.

Please see [LibLog Wiki](https://github.com/damianh/LibLog/wiki) for more information.

### Microsoft.Extensions.Logging.Abstractions

You can configure Microsoft.Extensions.Logging.Abstractions either manually or using services found in [Quartz.Extensions.DependencyInjection](https://www.nuget.org/packages/Quartz.Extensions.DependencyInjection).

#### Manual configuration

```csharp
// obtain your logger factory, for example from IServiceProvider
ILoggerFactory loggerFactory = ...;

// Quartz 3.1
Quartz.LogContext.SetCurrentLogProvider(loggerFactory);

// Quartz 3.2 onwards
Quartz.Logging.LogContext.SetCurrentLogProvider(loggerFactory);
```

#### Configuration using Microsoft DI integration

```csharp
services.AddQuartz(q =>
{
    // this automatically registers the Microsoft Logging
});
```
