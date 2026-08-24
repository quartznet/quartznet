---

title: 'Configuration, Resource Usage and Building a Scheduler'
---

# Configuration, Resource Usage and Building a Scheduler

Quartz is designed in a modular way: a scheduler is assembled from a thread pool, a job store, whatever data
sources that store needs, and the settings of the scheduler itself. The service container puts those pieces
together, and `AddQuartz` is where you say which ones you want.

The major components that can be configured are:

* **The thread pool.** `DefaultThreadPool` runs jobs as tasks on
  [the CLR's managed thread pool](https://learn.microsoft.com/dotnet/standard/threading/the-managed-thread-pool);
  its one setting, `MaxConcurrency`, limits how many jobs a node runs at once. `q.UseDefaultThreadPool(20)`, or
  `q.UseThreadPool<T>()` for one of your own.
* **The job store**, discussed in [Lesson 10](job-stores.md): `q.UseInMemoryStore()` or
  `q.UsePersistentStore(…)`.
* **Data sources**, when the store is a persistent one — part of the same `UsePersistentStore` call.
* **The scheduler itself**: `q.ConfigureScheduler(options => …)` for its name, id, idle wait time and batching.

Every option of every one of those, in both its typed and its flat spelling, is tabulated in the
[configuration reference](../configuration/reference.md).

## Building a scheduler without a container

An application with no host — a console application, or a test — builds a scheduler with
`QuartzSchedulerBuilder`. It takes the same configuration API as `AddQuartz`, creating a container of
its own and building from it, so what works in one works in the other:

<!-- snippet: sample_configuration_building_a_scheduler -->
```csharp
IScheduler scheduler = await QuartzSchedulerBuilder.Create()
    .ConfigureScheduler(options => options.InstanceName = "reporting")
    .UseDefaultThreadPool(maxConcurrency: 10)
    .UseInMemoryStore()
    .BuildScheduler();
```
<!-- endSnippet -->

Every configuration method returns the builder itself, so the whole thing is one expression. Nothing
starts on its own: without a hosted service, starting the scheduler is your call, and so is shutting it
down.

[Building a Scheduler Without a Host](standalone-scheduler.md) is the full lesson — `Build()` versus
`BuildScheduler()`, what owning the container means for disposal, persistent and clustered standalone
schedulers, and what the container-first path adds that this one lacks.

## Configuring from properties

A scheduler can also be configured from a set of flat `quartz.*` properties (`NameValueCollection`)
instead of in code. The properties are generally stored in and loaded from a file, but can also be
created by your program and handed to the builder:

<!-- snippet: sample_configuration_from_properties -->
```csharp
await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
    .UseProperties(properties)
    .Build();
```
<!-- endSnippet -->

The keys are translated into the same options and registrations the code-based API produces, so a
scheduler configured this way is the same scheduler, and the two can be mixed — what is written in code
wins. Keys are checked against the ones Quartz reads, so a misspelling is reported rather than silently
leaving a setting at its default.

Every key, and the option it maps to, is listed under
[Legacy property keys](../configuration/reference.md#legacy-property-keys).

## Logging

Quartz logs through `Microsoft.Extensions.Logging`. Under a host, or anywhere else the scheduler is built from
a container, it uses whatever logging the application has configured and there is nothing to set up. Quartz
does not log much: some information while starting, and then only serious problems while jobs run.

That covers the scheduler and everything it is built from: the scheduling loop, the job store and the
cluster manager and misfire handler it owns, the thread pool, the job factory. What it does not cover is
what no container builds — a listener or a trigger you constructed yourself, the static helpers, the jobs
in `Quartz.Jobs` — and a scheduler built by `QuartzSchedulerBuilder`, whose container has no logging
providers of its own unless you register some on its `Services`. Those say where logging goes with one
call:

<!-- snippet: sample_configuration_log_provider -->
```csharp
// obtain your logger factory, for example from IServiceProvider
ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

LogProvider.SetLogProvider(loggerFactory);
```
<!-- endSnippet -->

`LogProvider` is in `Quartz.Diagnostics`, and also hands out loggers — `LogProvider.CreateLogger<T>()` — for
the same situation.
