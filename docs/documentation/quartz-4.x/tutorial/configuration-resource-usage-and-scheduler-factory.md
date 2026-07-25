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

## Building a scheduler without a container

An application with no host — a console application, or a test — builds a scheduler with
`QuartzSchedulerBuilder`. It takes the same configuration API as `AddQuartz`, creating a container of
its own and building from it, so what works in one works in the other:

```csharp
IScheduler scheduler = await QuartzSchedulerBuilder.Create()
    .Configure(q =>
    {
        q.ConfigureScheduler(options => options.InstanceName = "reporting");
        q.UseDefaultThreadPool(maxConcurrency: 10);
        q.UseInMemoryStore();
    })
    .BuildScheduler();
```

## Logging

Quartz logs through `Microsoft.Extensions.Logging`. Under a host it uses whatever logging the
application has configured, with no extra setup. Quartz does not log much: some information while
starting, and then only serious problems while jobs run.

### Microsoft.Extensions.Logging

You can configure Microsoft.Extensions.Logging.Abstractions either manually or using services found in [Quartz](https://www.nuget.org/packages/Quartz).

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
