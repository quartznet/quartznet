---

title: 'More About Jobs & JobDetails'
---

A job class holds the code that does the work. The `JobDetail`, built with `JobBuilder`, holds the
settings of one instance of that job. The scheduling code from Lesson 1:

<!-- snippet: sample_more_about_jobs_scheduling -->
```csharp
// define the job and tie it to our HelloJob class
IJobDetail job = JobBuilder.Create<HelloJob>()
    .WithIdentity("myJob", "group1")
    .Build();

// Trigger the job to run now, and then every 40 seconds
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger", "group1")
    .StartNow()
    .WithSimpleSchedule(x => x
        .WithInterval(TimeSpan.FromSeconds(40))
        .RepeatForever())
    .Build();

await scheduler.ScheduleJob(job, trigger);
```
<!-- endSnippet -->

and the job class `HelloJob`:

<!-- snippet: sample_more_about_jobs_hello_job -->
```csharp
public class HelloJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        await Console.Out.WriteLineAsync("HelloJob is executing.");
    }
}
```
<!-- endSnippet -->

The scheduler is given an `IJobDetail` that names the job's class. Every time it runs the job, it creates a
new instance of the class and calls `Execute`.

* Under `AddQuartz` the instance comes from the service container, in its own scope, so the job takes its
  dependencies through its constructor.
* Do not keep state in fields on the job class. The instance is gone when the fire ends.
* A registered job may not take a part that belongs to one scheduler by constructor: `IScheduler`,
  `ISchedulerFactory`, `IJobStore`, `IThreadPool` or `IOptions<QuartzSchedulerOptions>`. The container
  does not know which scheduler is firing the job, so startup refuses such a constructor. Use
  `context.Scheduler`; see
  [which scheduler's parts a job is built from](../multi-tenancy.md#which-scheduler-s-parts-a-job-is-built-from).

Configuration for a job instance, and state kept between executions, both go in the `JobDataMap`.

## JobDataMap

The `JobDataMap` holds any number of (serializable) objects for the job to read when it runs. It
implements `IDictionary<string, object?>`. Typed accessors (`GetString`, `GetInt`, `GetDateTimeOffset`,
`Get<T>`, `TryGet<T>` and the rest) are extension methods, so `map.GetString("key")` works on any map
without a cast. [Job Data](job-data-map.md) has every accessor, the `PutAsString` round-trip formats, the
merge rules, string-mode storage, and what does not belong in job data.

Putting data into the map before adding the job to the scheduler:

<!-- snippet: sample_more_about_jobs_setting_job_data -->
```csharp
// define the job and tie it to our DumbJob class
IJobDetail job = JobBuilder.Create<DumbJob>()
    .WithIdentity("myJob", "group1") // name "myJob", group "group1"
    .UsingJobData("jobSays", "Hello World!")
    .UsingJobData("myFloatValue", 3.141f)
    .Build();
```
<!-- endSnippet -->

Reading it during execution:

<!-- snippet: sample_more_about_jobs_getting_job_data -->
```csharp
public class DumbJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobKey key = context.JobDetail.Key;

        JobDataMap dataMap = context.JobDetail.JobDataMap;

        string? jobSays = dataMap.GetString("jobSays");
        float myFloatValue = dataMap.GetFloat("myFloatValue");

        await Console.Error.WriteLineAsync("Instance " + key + " of DumbJob says: " + jobSays + ", and val is: " + myFloatValue);
    }
}
```
<!-- endSnippet -->

* With a persistent JobStore the map is serialized, so its contents are prone to class-versioning
  problems. Standard .NET types are safe; changing a class that has serialized instances can break
  compatibility.
* AdoJobStore and `JobDataMap` can be put in a mode that stores only primitives and strings, which rules
  out later serialization problems.
* If the job class has settable properties named like keys in the map, the default JobFactory sets them
  when it creates the job. A custom JobFactory does not do this by default.

### Naming the property instead of the key

To set a value meant for one of those properties, name the property instead of spelling its key. Given
the properties:

<!-- snippet: sample_more_about_jobs_job_with_properties -->
```csharp
public class DumbJob : IJob
{
    public string JobSays { get; set; } = "";
    public float FloatValue { get; set; }

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}
```
<!-- endSnippet -->

the key is the property's name, and the value must be of the property's type:

<!-- snippet: sample_more_about_jobs_naming_the_property -->
```csharp
IJobDetail job = JobBuilder.Create<DumbJob>()
    .WithIdentity("myJob", "group1")
    .UsingJobData(j => j.JobSays, "Hello World!")
    .UsingJobData(j => j.FloatValue, 3.141f)
    .Build();
```
<!-- endSnippet -->

* Use the generic `JobBuilder.Create<DumbJob>()`. It returns a `JobBuilder<DumbJob>`, which is what lets
  `j` be inferred; `JobBuilder.Create()` builds for `IJob`, which has no properties to name.
* Nothing is instantiated. The expression is only read, never run.
* The property must be readable and publicly settable. A property declared `{ private get; set; }` cannot
  be named; give it an ordinary getter.

Mistakes fail instead of being ignored. A property of an unrelated job does not compile. These are
rejected when the builder runs:

* a property with no public setter;
* a nested path such as `j => j.Something.Nested`;
* a property reached by casting the job to another type;
* a value that will not convert to the property's type, or would lose information doing so;
* a `null` for a property that cannot hold one.

By contrast, a mistyped string key binds to nothing and leaves the job running with its defaults, and a
wrong-typed value is silently coerced.

Only a name reaches the map, and the job factory finds the property by that name, with the first
character upper-cased. The builder runs the same lookup and rejects a property it does not arrive back
at:

* a property whose name starts with a lower-case letter;
* an explicit interface implementation, which is not public on the job class;
* a name that resolves to a different property of another type, such as a `new` member hiding a base
  property.

An enum is stored as its name, so it survives a persistent store and reads sensibly in the map. Otherwise
the store must be able to serialize the value, as with any job data.

Triggers work the same way through `TriggerBuilder.Create<DumbJob>()`, so one job can take different
inputs per trigger:

<!-- snippet: sample_more_about_jobs_naming_the_property_on_a_trigger -->
```csharp
ITrigger trigger = TriggerBuilder.Create<DumbJob>()
    .WithIdentity("myTrigger", "group1")
    .ForJob(job)
    .UsingJobData(j => j.JobSays, "Good evening!")
    .Build();
```
<!-- endSnippet -->

`ForJob(IJobDetail)` also checks that the job is a `DumbJob`; it is the one overload that knows the job's
type.

The configurators in the [Microsoft DI integration](../packages/microsoft-di-integration.md) take the job
type from the call:

<!-- snippet: sample_more_about_jobs_naming_the_property_under_di -->
```csharp
q.AddJob<DumbJob>(j => j.UsingJobData(x => x.JobSays, "Hello World!"));

q.ScheduleJob<DumbJob>(
    t => t.StartNow().UsingJobData(x => x.JobSays, "Good evening!"),
    j => j.WithIdentity("myJob"));

// a trigger added on its own names the job type it fires
q.AddTrigger<DumbJob>(t => t.ForJob(jobKey).UsingJobData(x => x.JobSays, "Good evening!"));
```
<!-- endSnippet -->

`AddTrigger` without a type argument is untyped and has no properties to name. Name the job's type when
you want typed trigger data.

A trigger's `JobDataMap` lets a job stored for use by several triggers take different inputs from each.
During execution, `context.MergedJobDataMap` merges the JobDetail's map with the trigger's; the trigger's
values override same-named values from the JobDetail.

Reading the merged map:

<!-- snippet: sample_more_about_jobs_merged_job_data -->
```csharp
public class DumbJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobKey key = context.JobDetail.Key;

        JobDataMap dataMap = context.MergedJobDataMap;  // Note the difference from the previous example

        string? jobSays = dataMap.GetString("jobSays");
        float myFloatValue = dataMap.GetFloat("myFloatValue");
        IList<DateTimeOffset> state = (IList<DateTimeOffset>) dataMap["myStateData"]!;
        state.Add(DateTimeOffset.UtcNow);

        await Console.Error.WriteLineAsync("Instance " + key + " of DumbJob says: " + jobSays + ", and val is: " + myFloatValue);
    }
}
```
<!-- endSnippet -->

Or let the JobFactory set the values as properties:

<!-- snippet: sample_more_about_jobs_injected_job_data -->
```csharp
public class DumbJob : IJob
{
    public string JobSays { private get; set; } = "";
    public float FloatValue { private get; set; }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobKey key = context.JobDetail.Key;

        JobDataMap dataMap = context.MergedJobDataMap;  // Note the difference from the previous example

        IList<DateTimeOffset> state = (IList<DateTimeOffset>) dataMap["myStateData"]!;
        state.Add(DateTimeOffset.UtcNow);

        await Console.Error.WriteLineAsync("Instance " + key + " of DumbJob says: " + JobSays + ", and val is: " + FloatValue);
    }
}
```
<!-- endSnippet -->

## Job "Instances"

One job class can have many job definitions (JobDetails) in the scheduler, each with its own properties
and `JobDataMap`. For example, a `SalesReportJob` class can read the sales person's name from its
`JobDataMap` and be stored as two definitions, "SalesReportForJoe" and "SalesReportForMike", with "Joe"
and "Mike" in their maps.

When a trigger fires, its JobDetail is loaded and the job class is instantiated through the scheduler's
JobFactory. Under `AddQuartz` that is `MicrosoftDependencyInjectionJobFactory`, which:

1. opens a service scope;
2. resolves the job type from the container, with constructor injection;
3. sets the job's properties whose names match keys in the merged `JobDataMap`;
4. disposes the scope when the fire ends, so a scoped `DbContext` belongs to one execution.

Terms used in this documentation:

| Term | Means |
|---|---|
| job, job definition, JobDetail instance | a stored JobDetail |
| job instance, instance of a job definition | one executing job |
| job type | the class implementing `IJob` |

## JobFactory

The scheduler's JobFactory instantiates the job when a trigger fires.

| Factory | Used |
|---|---|
| `MicrosoftDependencyInjectionJobFactory` | by default, under `AddQuartz` and under `QuartzSchedulerBuilder` (which builds its own container) |
| `SimpleJobFactory` | by a scheduler built without any container; the base of the default factory |

`SimpleJobFactory` activates the type through its parameterless constructor and sets properties from the
job data.

Write your own `IJobFactory` when a job must be built another way: resolved from a tenant's container,
proxied, or handed something that only exists at fire time.

* Set it where the scheduler is configured: `q.UseJobFactory<MyJobFactory>()` or
  `q.UseJobFactory(new MyJobFactory())`.
* To keep the container factory and only add to the scope it opens, use
  `q.ConfigureJobScope((scope, bundle, scheduler) => …)` instead.
* A factory returns a `JobScope`: the job, plus an optional opaque `State` that Quartz passes back to
  `ReturnJob` when the job finishes. Put anything the factory allocated to build the job there: a
  dependency injection scope, a connection, a tenant context.

::: tip
There's [built-in support for integrating with Microsoft Dependency Injection](../packages/microsoft-di-integration.md), which in
turn lets you use other IoC containers.
:::

## Job State and Concurrency

Two attributes on the job class control state and concurrency. Both apply to a job definition
(JobDetail), not to instances of the class. They sit on the class because they change how it is written.

| Attribute | Effect |
|---|---|
| `[DisallowConcurrentExecution]` | no two executions of the same JobDetail run at once |
| `[PersistJobDataAfterExecution]` | after `Execute` completes, even with a `JobExecutionException`, the stored `JobDataMap` is updated, so the next execution sees the new values |

With `[DisallowConcurrentExecution]` on "SalesReportJob", only one "SalesReportForJoe" runs at a time, but
it can run concurrently with "SalesReportForMike".

If you use `[PersistJobDataAfterExecution]`, also use `[DisallowConcurrentExecution]`. Otherwise two
concurrent executions race over which data is left stored.

## Other Attributes Of Jobs

| Property | Effect |
|---|---|
| `Durability` | a non-durable job is deleted once no active trigger is associated with it |
| `RequestsRecovery` | a job executing during a hard shutdown (process crash, machine off) is re-executed when the scheduler starts again, with `JobExecutionContext.Recovering` true |

The job store remembers an interrupted execution until the scheduler starts and recovers it.
Rescheduling its trigger in the meantime, as re-applying your `AddJob`/`AddTrigger` registrations on
startup does, keeps it. Unscheduling the trigger forgets it.

## A JobDetail of your own

`JobBuilder` builds Quartz's own `IJobDetail`, which almost every application wants. To carry something
of yours with the definition (a tenant, a correlation id), implement `IJobDetail`. Everything Quartz asks
of a detail is on the interface:

<!-- snippet: sample_more_about_jobs_custom_job_detail -->
```csharp
public sealed class TenantJobDetail : IJobDetail
{
    public TenantJobDetail(JobKey key, JobType jobType, string tenant, JobDataMap? jobDataMap = null)
    {
        Key = key;
        JobType = jobType;
        Tenant = tenant;
        JobDataMap = jobDataMap ?? new JobDataMap();
    }

    public string Tenant { get; }

    public JobKey Key { get; }
    public string Description => $"jobs for {Tenant}";
    public JobType JobType { get; }
    public JobDataMap JobDataMap { get; }
    public bool Durable => true;
    public bool PersistJobDataAfterExecution => true;
    public bool ConcurrentExecutionDisallowed => true;
    public bool RequestsRecovery => false;

    // How a job store re-stores the data a [PersistJobDataAfterExecution] job left behind: it asks the
    // detail for a copy of itself rather than building one, which it could only do as Quartz's own type.
    public IJobDetail WithJobData(JobDataMap jobDataMap)
        => new TenantJobDetail(Key, JobType, Tenant, jobDataMap);

    public IJobDetail Clone()
        => new TenantJobDetail(Key, JobType, Tenant, new JobDataMap(JobDataMap));
}
```
<!-- endSnippet -->

::: warning How far it travels
A detail of your own round-trips through `RAMJobStore`, which holds the instances it is given and returns
clones of them. It does not survive a store or transport that keeps a detail as data. The ADO.NET job
store writes the columns of `QRTZ_JOB_DETAILS` and rebuilds every detail through `JobBuilder`, and the
HTTP client rebuilds one from its wire payload; both return Quartz's own implementation. Anything your
type carries beyond the members above is lost. Put it in the `JobDataMap` if it has to come back.
:::

`detail.GetJobBuilder()` is an extension method over the interface, so it works on your own detail too,
but what it builds is Quartz's `IJobDetail`. Use `WithJobData` to vary the data of your own detail, and
`Clone()` to copy it.

## Trimming

The `Quartz` package is marked `IsTrimmable` and declares `IsAotCompatible`. An application can publish
with `PublishTrimmed` or `PublishAot` and get an executable that starts without a runtime installed.

| Store | What you do |
|---|---|
| in-memory | nothing |
| persistent | register the database with the driver's `DbProviderFactory` rather than its name, and declare your own job-data value types to the serializer |

The repository publishes a scheduler both ways and runs it over a real SQLite store on Windows, Linux and
macOS on every pull request.

A job type that arrives as a string is not covered, because a trimmer cannot follow a string. That is the
`JOB_CLASS_NAME` column an ADO.NET store reads back, a schedule file, an HTTP request body, and the flat
`quartz.*` keys. Each is either an API marked `[RequiresUnreferencedCode]` or a path only one
configuration style reaches, and none is suppressed in the shipped assembly. Registering job types with
`AddJob<TJob>()` covers the common case. See
__[Publishing Trimmed and Native AOT](../how-tos/trimming-and-native-aot.md)__ for what each package
claims, how to read what a publish reports, and the full recipe.

## JobExecutionException

The only exception `IJob.Execute(..)` should throw is `JobExecutionException`, so wrap the body of
`Execute` in a try-catch. The exception's directives to the scheduler are init-only properties:

<!-- snippet: sample_more_about_jobs_job_execution_exception -->
```csharp
catch (Exception ex)
{
    // ask the scheduler to run this fire again with the same context
    throw new JobExecutionException(ex) { RefireImmediately = true };
}
```
<!-- endSnippet -->

| Property | Effect |
|---|---|
| `RefireImmediately` | runs this fire again with the same context |
| `UnscheduleFiringTrigger` | the trigger that fired the job does not fire again |
| `UnscheduleAllTriggers` | no trigger of the job fires again |

When `RefireImmediately` is set, the unschedule flags are ignored.
