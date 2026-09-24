---

title: 'Configuration, Resource Usage and Building a Scheduler'
---

# Configuration, Resource Usage and Building a Scheduler

A scheduler is assembled from a thread pool, a job store, the data sources that store needs, and its own
settings. The service container puts them together; `AddQuartz` is where you choose them.

| Component | Configure with |
|---|---|
| thread pool | `q.UseDefaultThreadPool(20)`, or `q.UseThreadPool<T>()` for your own |
| job store ([Job Stores](job-stores.md)) | `q.UseInMemoryStore()` or `q.UsePersistentStore(…)` |
| data sources, for a persistent store | the same `UsePersistentStore` call |
| the scheduler: name, id, idle wait time, batching | `q.ConfigureScheduler(options => …)` |

`DefaultThreadPool` runs jobs as tasks on
[the CLR's managed thread pool](https://learn.microsoft.com/dotnet/standard/threading/the-managed-thread-pool).
Its one setting, `MaxConcurrency`, limits how many jobs a node runs at once.

Every option, in both its typed and its flat spelling, is in the
[configuration reference](../configuration/reference.md).

## Building a scheduler without a container

An application with no host (a console application, a test) builds a scheduler with
`QuartzSchedulerBuilder`. Its `Create` callback is the `AddQuartz` callback over a container it creates
itself, so the same configuration works in both:

<!-- snippet: sample_configuration_building_a_scheduler -->
```csharp
IScheduler scheduler = await QuartzSchedulerBuilder
    .Create(q => q
        .ConfigureScheduler(options => options.InstanceName = "reporting")
        .UseDefaultThreadPool(maxConcurrency: 10)
        .UseInMemoryStore())
    .BuildScheduler();
```
<!-- endSnippet -->

Every configuration method returns the builder, so the whole thing is one expression. Nothing starts on
its own: without a hosted service, you start and shut down the scheduler.

[Building a Scheduler Without a Host](standalone-scheduler.md) covers `Build()` versus
`BuildScheduler()`, disposal of the container, persistent and clustered standalone schedulers, and what
the container-first path adds.

## Configuring from properties

A scheduler can also be configured from flat `quartz.*` properties (`NameValueCollection`), usually
loaded from a file, or created by your program:

<!-- snippet: sample_configuration_from_properties -->
```csharp
await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
    .UseProperties(properties)
    .Build();
```
<!-- endSnippet -->

* The keys become the same options and registrations the code API produces, so the scheduler is the
  same.
* Properties and code can be mixed; code wins.
* Keys are checked against the ones Quartz reads, so a misspelling is reported instead of leaving a
  setting at its default.

Every key and the option it maps to: [Legacy property keys](../configuration/reference.md#legacy-property-keys).

## Logging

Quartz logs through `Microsoft.Extensions.Logging`. A scheduler built from a container, such as under a
host, uses the application's logging with no setup. Quartz logs some information while starting, then
only serious problems while jobs run.

That covers everything the container builds: the scheduling loop, the job store with its cluster manager
and misfire handler, the thread pool, the job factory. It does not cover:

* what no container builds: a listener or trigger you constructed, the static helpers, the jobs in
  `Quartz.Jobs`;
* a scheduler built by `QuartzSchedulerBuilder`, whose container has no logging providers unless you
  register some on its `Services`.

For those, set the logger factory with one call:

<!-- snippet: sample_configuration_log_provider -->
```csharp
// obtain your logger factory, for example from IServiceProvider
ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

LogProvider.SetLogProvider(loggerFactory);
```
<!-- endSnippet -->

`LogProvider` is in `Quartz.Diagnostics`. It also creates loggers for the same situation:
`LogProvider.CreateLogger<T>()`.
