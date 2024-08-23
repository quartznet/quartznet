---

title: Migration Guide
---

*This document outlines changes needed per version upgrade basis. You need to check the steps for each version you are jumping over. You should also check [the complete change log](https://raw.github.com/quartznet/quartznet/master/changelog.md).*

::: tip
If you are a new user starting with the latest version, you don't need to follow this guide. Just jump right to [the tutorial](tutorial/index.html)
:::

Quartz jumped to async/await world and added support for .NET Core with 3.0 release so most significant changes
can be found on APIs and functionality available depending on whether you target full .NET Framework or the .NET Core.

## Packaging changes

Quartz NuGet package was split to more specific packages.

* [Quartz.Jobs](https://www.nuget.org/packages/Quartz.Jobs) is now a separate NuGet dependency you might need
  * DirectoryScanJob
  * FileScanJob
  * NativeJob
  * SendMailJob
* [Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) is now a separate NuGet dependency you might need
  * XMLSchedulingDataProcessorPlugin

Check that you reference the required NuGet packages and that your configuration references also the correct assembly.

### Database schema changes

2.6 schema should work with 3.0 with no changes.

### Migrating HolidayCalendar binary format

If you have `HolidayCalendar`s stored in database in binary format (just stored with AdoJobStore). You need to first load them with Quartz 2.4 or later 2.x version and then re-store them.
This will make the serialization use format that is not dependent on presence of C5 library.

### Thread pool changes

* `SimpleThreadPool` was removed altogether and it's now a synonym for `DefaultThreadPool`
* Jobs are now ran in CLR thread pool
* `ThreadCount` parameter still limits how many items will be queued at most to CLR thread pool
* Thread priority is no longer supported, you need to remove `threadPriority` parameter

### API Changes

Scheduler and job API methods now are based on Tasks. This reflects how you define your jobs and operate with scheduler.

#### Scheduler

You now need to make sure that you have proper awaits in place when you operate with the scheduler:

```csharp
// operating with scheduler is now Task-based and requires appropriate awaits
await scheduler.ScheduleJob(job, trigger);
await scheduler.Start();
await scheduler.Shutdown(waitForJobsToComplete: true);
```

#### Jobs

Job's Execute method now returns a Task and can easily contain async code:

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

If you don't have any async'ness in your job, you can just  return `Task.CompletedTask` at the end of Execute method (available from .NET 4.6 onwards).

##### IInterruptableJob

`IInterruptableJob` interface has been removed. You need to check for `IJobExecutionContext`'s`CancellationToken.IsCancellationRequested` to determine whether job interruption has been requested.

##### IStatefulJob

`IStatefulJob` interface that was obsoleted in 2.x has been removed, you should use `DisallowConcurrentExecution` and `PersistJobDataAfterExecution` attributes to achieve your goal.

#### Other APIs

If you have created custom implementations of services used by Quartz, you're going to need to adapt your code to be async-based.

### Job store serialization configuration changes

You need to now explicitly state whether you want to use binary or json serialization if you are using persistent job store (AdoJobStore) when you configure your scheduler.

* For existing setups you should use the old binary serialization to ensure things work like before (see [Quartz.Serialization.SystemTextJson documentation](packages/system-text-json) for migration path)
* For new projects the JSON serialization is recommended as it should be marginally faster and more robust as it's not dealing with binary versioning issues
* JSON is more secure and generally the way to use moving forward

If you choose to go with JSON serialization, remember to add NuGet package reference to either **[Quartz.Serialization.SystemTextJson](https://www.nuget.org/packages/Quartz.Serialization.SystemTextJson/)** or **[Quartz.Serialization.Json](https://www.nuget.org/packages/Quartz.Serialization.Json/)** to your project.

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

ADO.NET provider names have been simplified, the provider names are without version, e.g. `SqlServer-20` => `SqlServer`. They are now bound to whatever version that can be loaded.

### C5 Collections

C5 Collections are no longer ILMerged inside Quartz, .NET 4.5 offers the needed collections.

### Logging

Common.Logging has been replaced with [LibLog](https://github.com/damianh/LibLog) to reduce dependencies to none. LibLog should automatically detect your logging framework of choice if it's supported.

### Remoting

Remoting is currently only supported when running on full framework version.
