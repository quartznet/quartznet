---

title: 'Configuration, Resource Usage and SchedulerFactory'
---

# Configuration, Resource Usage and SchedulerFactory

Quartz is modular. Before it can work, these components must be configured and put together:

* ThreadPool
* JobStore
* DataSources (if necessary)
* The Scheduler itself

The default thread pool, `DefaultThreadPool`, runs jobs as tasks on the [CLR's managed thread pool](https://docs.microsoft.com/en-us/dotnet/standard/threading/the-managed-thread-pool). Its max concurrency limits how many tasks it schedules to the CLR thread pool at once. See the [configuration reference](../configuration/reference.md#threadpool) to configure it.

JobStores and DataSources are covered in [Job Stores](job-stores.md). All JobStores implement the `IJobStore` interface; if no bundled JobStore fits your needs, write your own.

The Scheduler itself needs a name and instances of a JobStore and ThreadPool.

## StdSchedulerFactory

`StdSchedulerFactory` implements `ISchedulerFactory`. It creates and initializes a Quartz Scheduler from a set of properties (`NameValueCollection`), usually loaded from a file but also accepted directly from your program. `GetScheduler()` creates the scheduler, initializes it (and its ThreadPool, JobStore and DataSources), and returns its public interface.

All properties are in the [Configuration Reference](../configuration/reference.md).

## DirectSchedulerFactory

`DirectSchedulerFactory` is another `ISchedulerFactory` implementation, for creating the Scheduler in code. Its use is generally discouraged:

* it requires a deeper understanding of what you are doing, and
* it allows no declarative configuration, so all of the scheduler's settings end up hard-coded.

## Logging

::: tip
As of Quartz.NET 3.1, you can configure [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/) to be used instead of LibLog.
:::

### LibLog

Quartz.NET logs through the [LibLog](https://github.com/damianh/LibLog) library. It logs little: some information during initialization, then only serious problems while jobs execute. LibLog delegates to a logging framework such as log4net or SeriLog, so tune the output (amount, destination) in that framework. See the [LibLog Wiki](https://github.com/damianh/LibLog/wiki).

### Microsoft.Extensions.Logging.Abstractions

Configure Microsoft.Extensions.Logging.Abstractions manually, or with the services in [Quartz.Extensions.DependencyInjection](https://www.nuget.org/packages/Quartz.Extensions.DependencyInjection).

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
