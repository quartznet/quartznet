---

title: Migration Guide
---

*Check the steps for every version you skip, and [the release notes](https://github.com/quartznet/quartznet/releases) for each version.*

::: tip
New users starting with the latest version do not need this guide. Go to [the tutorial](tutorial/index.html).
:::

Quartz 3.0 moved to async/await and added .NET Core support. Most changes are in the APIs, and in which features are available on full .NET Framework versus .NET Core.

## Packaging changes

The Quartz NuGet package was split:

* [Quartz.Jobs](https://www.nuget.org/packages/Quartz.Jobs) is now a separate NuGet dependency you might need
  * DirectoryScanJob
  * FileScanJob
  * NativeJob
  * SendMailJob
* [Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) is now a separate NuGet dependency you might need
  * XMLSchedulingDataProcessorPlugin

Reference the packages you need, and make sure your configuration names the correct assembly.

### Database schema changes

The 2.6 schema should work with 3.0 unchanged.

### Migrating HolidayCalendar binary format

If you have `HolidayCalendar`s stored in the database in binary format (as AdoJobStore stores them), first load them with Quartz 2.4 or a later 2.x version and store them again. The serialization format then no longer depends on the C5 library.

### Thread pool changes

* `SimpleThreadPool` was removed altogether and it's now a synonym for `DefaultThreadPool`
* Jobs now run in the CLR thread pool
* `ThreadCount` still limits how many items are queued at most to the CLR thread pool
* Thread priority is no longer supported; remove the `threadPriority` parameter

### API Changes

Scheduler and job API methods are now Task-based.

#### Scheduler

Await your scheduler calls:

```csharp
// operating with scheduler is now Task-based and requires appropriate awaits
await scheduler.ScheduleJob(job, trigger);
await scheduler.Start();
await scheduler.Shutdown(waitForJobsToComplete: true);
```

#### Jobs

A job's Execute method returns a Task and can contain async code:

```csharp
// Jobs now return tasks from their Execute methods
public class MyJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        // dummy 1ms sleep
        await Task.Delay(1);
    }
}
```

A job with no async work can return `Task.CompletedTask` at the end of Execute (available from .NET 4.6 onwards).

##### IInterruptableJob

The `IInterruptableJob` interface was removed. Check `IJobExecutionContext`'s `CancellationToken.IsCancellationRequested` to see whether interruption was requested.

##### IStatefulJob

The `IStatefulJob` interface, obsolete in 2.x, was removed. Use the `DisallowConcurrentExecution` and `PersistJobDataAfterExecution` attributes instead.

#### Other APIs

Custom implementations of services Quartz uses must become async-based.

### Job store serialization configuration changes

With a persistent job store (AdoJobStore), you must now state whether to use binary or JSON serialization when you configure the scheduler.

* Existing setups should keep binary serialization so things work as before (see the [Quartz.Serialization.SystemTextJson documentation](packages/system-text-json) for the migration path).
* New projects should use JSON: it should be marginally faster, and it is more robust because it avoids binary versioning issues.
* JSON is more secure and is the way forward.

For JSON, add a NuGet package reference to **[Quartz.Serialization.SystemTextJson](https://www.nuget.org/packages/Quartz.Serialization.SystemTextJson/)** or **[Quartz.Serialization.Json](https://www.nuget.org/packages/Quartz.Serialization.Json/)**.

Configuring binary serialization strategy:

```csharp
var properties = new NameValueCollection
{
 ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
 // "binary" is alias for "Quartz.Simpl.BinaryObjectSerializer, Quartz"
 ["quartz.serializer.type"] = "binary"
};
ISchedulerFactory sf = new StdSchedulerFactory(properties);
```

Configuring JSON serialization strategy (recommended):

```csharp
var properties = new NameValueCollection
{
 ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
 // "newtonsoft" and "json" are aliases for "Quartz.Simpl.JsonObjectSerializer, Quartz.Serialization.Json"
 // you should prefer "newtonsoft" as it's more explicit from Quartz 3.10 onwards
 ["quartz.serializer.type"] = "newtonsoft"
};
ISchedulerFactory sf = new StdSchedulerFactory(properties);
```

## Simplified job store provider names

ADO.NET provider names no longer carry a version, e.g. `SqlServer-20` => `SqlServer`. They bind to whatever version can be loaded.

### C5 Collections

C5 Collections are no longer ILMerged into Quartz; .NET 4.5 has the needed collections.

### Logging

[LibLog](https://github.com/damianh/LibLog) replaced Common.Logging, so there are no logging dependencies. LibLog detects your logging framework automatically if it is supported.

### Remoting

Remoting is only supported on the full framework.
