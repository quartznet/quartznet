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

## Building a scheduler without a container

An application with no host — a console application, or a test — builds a scheduler with
`QuartzSchedulerBuilder`. It takes the same configuration API as `AddQuartz`, creating a container of
its own and building from it, so what works in one works in the other:

```csharp
var builder = QuartzSchedulerBuilder.Create();
builder.ConfigureScheduler(options => options.InstanceName = "reporting")
    .UseDefaultThreadPool(maxConcurrency: 10)
    .UseInMemoryStore();

IScheduler scheduler = await builder.BuildScheduler();
```

The builder is kept in a variable rather than built in one expression: its configuration methods are the
same ones `AddQuartz` hands out, so they return that interface rather than the builder and cannot be
chained into `BuildScheduler()`. Use `Build()` instead of `BuildScheduler()` when you want the factory
rather than the scheduler it produces. It returns a `StandaloneSchedulerFactory`, which owns the
container it built — dispose it, preferably with `await using`, to shut the scheduler down.

## Configuring from properties

A scheduler can also be configured from a set of flat `quartz.*` properties (`NameValueCollection`)
instead of in code. The properties are generally stored in and loaded from a file, but can also be
created by your program and handed to the builder:

```csharp
await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
    .UseProperties(properties)
    .Build();
```

The keys are translated into the same options and registrations the code-based API produces, so a
scheduler configured this way is the same scheduler, and the two can be mixed — what is written in code
wins. Keys are checked against the ones Quartz reads, so a misspelling is reported rather than silently
leaving a setting at its default.

You can find complete documentation in the "Configuration Reference" section of the Quartz documentation.

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
