---

title: 'More About Jobs & JobDetails'
---

As you saw in Lesson 2, jobs are rather easy to implement. There are just a few more things that you need to understand about
the nature of jobs, about the `Execute(..)` method of the `IJob` interface, and about JobDetails.

While a job class that you implement has the code that knows how to do the actual work
of the particular type of job, Quartz.NET needs to be informed about various attributes
that you may wish an instance of that job to have. This is done via the JobDetail class,
which was mentioned briefly in the previous section.

JobDetail instances are built using the `JobBuilder` class. `JobBuilder` allows you to describe
your job's details using a fluent interface.

Let's take a moment now to discuss a bit about the 'nature' of jobs and the life-cycle of job instances within Quartz.NET.
First lets take a look back at some of that snippet of code we saw in Lesson 1:

__Using Quartz.NET__

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

Now consider the job class __HelloJob__  defined as such:

```csharp
public class HelloJob : IJob
{
 public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
 {
  await Console.Out.WriteLineAsync("HelloJob is executing.");
 }
}
```

Notice that we give the scheduler a `IJobDetail` instance, and that it refers to the job to be executed by simply
providing the job's class. Each (and every) time the scheduler executes the job, it creates a new instance of the
class before calling its `Execute(..)` method. Under `AddQuartz` that instance comes from the service container,
inside a scope of its own, so the job can take its dependencies through its constructor the way the rest of your
application does. A ramification that does still hold is that it makes no sense to keep state in fields on the job
class — the instance is gone when the fire ends, so nothing in it is preserved between executions.

You may now be wanting to ask "how can I provide properties/configuration for a Job instance?" and "how can I
keep track of a job's state between executions?" The answer to these questions are the same: the key is the `JobDataMap`,
which is part of the JobDetail object.

## JobDataMap

The `JobDataMap` can be used to hold any number of (serializable) objects which you wish to have made available
to the job instance when it executes. `JobDataMap` implements `IDictionary<string, object?>`, and a set of typed
accessors — `GetString`, `GetInt`, `GetDateTimeOffset`, `GetEnum<T>`, `TryGet<T>` and the rest — come with it as
extension methods, so `map.GetString("key")` works on any map without a cast or a lookup of your own.

This section is the introduction. [Job Data](job-data-map.md) has the full inventory: every accessor, the
`PutAsString` round-trip formats, the merge rules, how string-mode storage changes what you may put in the
map, and what does not belong in job data at all.

Here's some quick snippets of putting data into the JobDataMap prior to adding the job to the scheduler:

__Setting Values in a JobDataMap__

```csharp
// define the job and tie it to our DumbJob class
IJobDetail job = JobBuilder.Create<DumbJob>()
 .WithIdentity("myJob", "group1") // name "myJob", group "group1"
 .UsingJobData("jobSays", "Hello World!")
 .UsingJobData("myFloatValue", 3.141f)
 .Build();
```

Here's a quick example of getting data from the JobDataMap during the job's execution:

__Getting Values from a JobDataMap__

```csharp
public class DumbJob : IJob
{
 public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
 {
  JobKey key = context.JobDetail.Key;

  JobDataMap dataMap = context.JobDetail.JobDataMap;

  string jobSays = dataMap.GetString("jobSays");
  float myFloatValue = dataMap.GetFloat("myFloatValue");

  await Console.Error.WriteLineAsync("Instance " + key + " of DumbJob says: " + jobSays + ", and val is: " + myFloatValue);
 }
}
```

If you use a persistent JobStore (discussed in the JobStore section of this tutorial) you should use some care
in deciding what you place in the JobDataMap, because the object in it will be serialized, and they therefore
become prone to class-versioning problems. Obviously standard .NET types should be very safe,  but beyond that,
any time someone changes the definition of a class for which you have serialized instances,
care has to be taken not to break compatibility.

Optionally, you can put AdoJobStore and JobDataMap into a mode where only primitives
and strings can be stored in the map, thus eliminating any possibility of later serialization problems.

If you add properties with set accessor to your job class that correspond to the names of keys in the JobDataMap,
then Quartz's default JobFactory implementation will automatically call those setters when the job is instantiated,
thus preventing the need to explicitly get the values out of the map within your execute method. Note this
functionality is not maintained by default when using a custom JobFactory.

### Naming the property instead of the key

When the value is meant for one of those properties, you can name the property rather than spell its key.
The key is then the property's own name, and the value has to be of the property's own type:

```csharp
public class DumbJob : IJob
{
 public string JobSays { get; set; }
 public float FloatValue { get; set; }
 // ...
}

IJobDetail job = JobBuilder.Create<DumbJob>()
 .WithIdentity("myJob", "group1")
 .UsingJobData(j => j.JobSays, "Hello World!")
 .UsingJobData(j => j.FloatValue, 3.141f)
 .Build();
```

`JobBuilder.Create<DumbJob>()` returns a `JobBuilder<DumbJob>`, and it is that job type which lets `j` be
inferred. `JobBuilder.Create()` builds for `IJob`, which has no properties to name, so use the generic
overload when you want to configure a job this way. Nothing is instantiated - the expression is only read,
never run.

The property has to be readable as well as publicly settable for this to work, so a property declared
`{ private get; set; }` cannot be named this way. Give it an ordinary getter if you want to configure it by
name.

A mistake is caught rather than swallowed. A property of an unrelated job does not compile at all, and
everything the compiler cannot rule out is rejected on the spot: a property with no public setter, a nested
path such as `j => j.Something.Nested`, a property reached by casting the job to another type, a value that
will not convert to the property's type or would lose information doing so, and a `null` for a property
that cannot hold one. Compare that to a mistyped string key, which binds to nothing and leaves the job
running with its defaults, or a wrong-typed value, which is silently coerced.

The remaining rules all follow from one thing: only a *name* reaches the map, and the job factory turns
that name back into a property by its own lookup. So the check is simply to run that lookup and insist it
arrives back at the property you named. A property whose name starts with a lower-case letter fails it,
because keys are only ever looked up with the first character upper-cased; so does one that implements an
interface explicitly, because it is not public on the job class; and so does one whose name resolves to a
different property of another type, which is what a `new` member hiding a base property does.

An enum is stored as its name, so it survives a persistent store and still reads sensibly in the map itself.
Otherwise the same care applies as to any other job data: the store has to be able to serialize the value.

Triggers work the same way through `TriggerBuilder.Create<DumbJob>()`, which is how one job takes different
inputs per trigger:

```csharp
ITrigger trigger = TriggerBuilder.Create<DumbJob>()
 .WithIdentity("myTrigger", "group1")
 .ForJob(job)
 .UsingJobData(j => j.JobSays, "Good evening!")
 .Build();
```

`ForJob(IJobDetail)` also checks that the job really is a `DumbJob`, since that is the one overload that
knows the job's type.

The same applies to the configurators in the
[Microsoft DI integration](../packages/microsoft-di-integration.md), where the job type comes from the call
itself:

```csharp
q.AddJob<DumbJob>(j => j.UsingJobData(x => x.JobSays, "Hello World!"));

q.ScheduleJob<DumbJob>(
 t => t.StartNow().UsingJobData(x => x.JobSays, "Good evening!"),
 j => j.WithIdentity("myJob"));

// a trigger added on its own names the job type it fires
q.AddTrigger<DumbJob>(t => t.ForJob(jobKey).UsingJobData(x => x.JobSays, "Good evening!"));
```

`AddTrigger<IJob>` is the untyped form — `IJob` has no properties to name — so name the job's own type
when you want typed trigger data.

Triggers can also have JobDataMaps associated with them. This can be useful in the case where you have a Job that is stored in the scheduler
for regular/repeated use by multiple Triggers, yet with each independent triggering, you want to supply the Job with different data inputs.

The JobDataMap that is found on the JobExecutionContext during Job execution serves as a convenience. It is a merge of the JobDataMap
found on the JobDetail and the one found on the Trigger, with the values in the latter overriding any same-named values in the former.

Here's a quick example of getting data from the JobExecutionContext's merged JobDataMap during the job's execution:

```csharp
public class DumbJob : IJob
{
 public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
 {
  JobKey key = context.JobDetail.Key;

  JobDataMap dataMap = context.MergedJobDataMap;  // Note the difference from the previous example

  string jobSays = dataMap.GetString("jobSays");
  float myFloatValue = dataMap.GetFloat("myFloatValue");
  IList<DateTimeOffset> state = (IList<DateTimeOffset>)dataMap["myStateData"];
  state.Add(DateTimeOffset.UtcNow);

  await Console.Error.WriteLineAsync("Instance " + key + " of DumbJob says: " + jobSays + ", and val is: " + myFloatValue);
 }
}
```

Or if you wish to rely on the JobFactory "injecting" the data map values onto your class, it might look like this instead:

```csharp
public class DumbJob : IJob
{
 public string JobSays { private get; set; }
 public float FloatValue { private get; set; }

 public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
 {
  JobKey key = context.JobDetail.Key;

  JobDataMap dataMap = context.MergedJobDataMap;  // Note the difference from the previous example

  IList<DateTimeOffset> state = (IList<DateTimeOffset>)dataMap["myStateData"];
  state.Add(DateTimeOffset.UtcNow);

  await Console.Error.WriteLineAsync("Instance " + key + " of DumbJob says: " + JobSays + ", and val is: " + FloatValue);
 }
}
```

You'll notice that the overall code of the class is longer, but the code in the `Execute()` method is cleaner.
One could also argue that although the code is longer, that it actually took less coding, if the programmer's IDE was used to auto-generate the properties,
rather than having to hand-code the individual calls to retrieve the values from the JobDataMap. The choice is yours.

## Job "Instances"

Many users spend time being confused about what exactly constitutes a "job instance".
We'll try to clear that up here and in the section below about job state and concurrency.

You can create a single job class, and store many 'instance definitions' of it within the scheduler by creating multiple instances of JobDetails

- each with its own set of properties and JobDataMap - and adding them all to the scheduler.

For example, you can create a class that implements the `IJob` interface called "SalesReportJob".
The job might be coded to expect parameters sent to it (via the JobDataMap) to specify the name of the sales person that the sales
report should be based on. They may then create multiple definitions (JobDetails) of the job, such as "SalesReportForJoe"
and "SalesReportForMike" which have "Joe" and "Mike" specified in the corresponding JobDataMaps as input to the respective jobs.

When a trigger fires, the JobDetail (instance definition) it is associated to is loaded,
and the job class it refers to is instantiated via the JobFactory configured on the Scheduler.
Under `AddQuartz` that factory is `MicrosoftDependencyInjectionJobFactory`: it opens a service scope, resolves
the job type from the container — constructor injection and all — and then sets any properties of the job whose
names match keys in the merged JobDataMap. The scope is disposed when the fire ends, so a scoped `DbContext`
belongs to the one execution that used it.

In "Quartz speak", we refer to each stored JobDetail as a "job definition" or "JobDetail instance",
and we refer to a each executing job as a "job instance" or "instance of a job definition".
Usually if we just use the word "job" we are referring to a named definition, or JobDetail.
When we are referring to the class implementing the job interface, we usually use the term "job type".

## JobFactory

When a trigger fires, the Job it is associated to is instantiated via the JobFactory configured on the Scheduler.
`MicrosoftDependencyInjectionJobFactory` is the registered default, both under `AddQuartz` and under
`QuartzSchedulerBuilder`, which builds a container of its own. `SimpleJobFactory` — which activates the type
through its parameterless constructor and sets properties from the job data — is the base it is built on, and what
a scheduler assembled without any container at all would use.

Write your own `IJobFactory` when a job has to be built some other way — resolved from a tenant's container,
proxied, or handed something that only exists at fire time. A factory is set where the scheduler is configured —
`q.UseJobFactory<MyJobFactory>()` or `q.UseJobFactory(new MyJobFactory())` — rather than assigned to the scheduler
afterwards. To keep the container factory and only add to the scope it opens, use
`q.ConfigureJobScope((scope, bundle, scheduler) => …)` instead of replacing the factory.

A factory returns a `JobScope`: the job, plus an optional opaque `State` object that Quartz hands straight back to
`ReturnJob` when the job has finished. That is where anything the factory had to allocate in order to build the job
belongs — a dependency injection scope, a connection, a tenant context — so the job itself stays the job.

::: tip
There's [built-in support for integrating with Microsoft Dependency Injection](../packages/microsoft-di-integration.md) which in
turn allows to use different IoC container implementations.
:::

## Job State and Concurrency

Now, some additional notes about a job's state data (aka JobDataMap) and concurrency.
There are a couple attributes that can be added to your Job class that affect Quartz's behaviour with respect to these aspects.

`[DisallowConcurrentExecution]` is an attribute that can be added to the Job class that tells Quartz not to execute multiple instances
of a given job definition (that refers to the given job class) concurrently.
Notice the wording there, as it was chosen very carefully. In the example from the previous section, if "SalesReportJob" has this attribute,
than only one instance of "SalesReportForJoe" can execute at a given time, but it can execute concurrently with an instance of "SalesReportForMike".
The constraint is based upon an instance definition (JobDetail), not on instances of the job class.
However, it was decided (during the design of Quartz) to have the attribute carried on the class itself, because it does often make a difference to how the class is coded.

`[PersistJobDataAfterExecution]` is an attribute that can be added to the Job class that tells Quartz to update the stored copy of
the JobDetail's JobDataMap after the Execute() method completes (even if it throws a JobExecutionException), such that the next
execution of the same job (JobDetail) receives the updated values rather than the originally stored values.
Like the `[DisallowConcurrentExecution]` attribute, this applies to a job definition instance, not a job class instance,
though it was decided to have the job class carry the attribute because it does often make a difference to how the class is coded
(e.g. the 'statefulness' will need to be explicitly 'understood' by the code within the execute method).

If you use the __PersistJobDataAfterExecution__ attribute, you should strongly consider also using the `[DisallowConcurrentExecution]` attribute,
in order to avoid possible confusion (race conditions) of what data was left stored when two instances of the same job (JobDetail) executed concurrently.

## Other Attributes Of Jobs

Here's a quick summary of the other properties which can be defined for a job instance via the JobDetail object:

- `Durability` - if a job is non-durable, it is automatically deleted from the scheduler once there are no longer any active triggers associated with it.
In other words, non-durable jobs have a life span bounded by the existence of its triggers.
- `RequestsRecovery` - if a job "requests recovery", and it is executing during the time of a 'hard shutdown' of the scheduler
(i.e. the process it is running within crashes, or the machine is shut off), then it is re-executed when the scheduler is started again.
In this case, the `JobExecutionContext.Recovering` property will return true.

## A JobDetail of your own

`JobBuilder` builds Quartz's own `IJobDetail`, and that is what almost every application wants. If you need a
job definition that carries something of yours alongside the ones above — a tenant, a correlation id, whatever
the rest of your system keys on — you can implement `IJobDetail` yourself. Everything Quartz asks of a detail is
declared on the interface:

```csharp
public sealed class TenantJobDetail : IJobDetail
{
    public TenantJobDetail(JobKey key, JobType jobType, string tenant, JobDataMap jobDataMap = null)
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

::: warning How far it travels
A detail of your own round-trips through `RAMJobStore`, which holds the instances it is given and hands back
clones of them. It does not survive a store or a transport that keeps a detail as data: the ADO.NET job store
writes the columns of `QRTZ_JOB_DETAILS` and rebuilds every detail it reads through `JobBuilder`, so what comes
back is Quartz's own implementation, and the HTTP client rebuilds one the same way from its wire payload.
Anything your type carries beyond the members above is gone by then — put it in the `JobDataMap` if it has to
come back.
:::

`detail.GetJobBuilder()` is an extension method over the interface, so it works on a detail of your own too. It
describes the detail rather than preserving it: what it builds is Quartz's `IJobDetail`. Use `WithJobData` to
vary the data of a detail of your own, and `Clone()` to copy one.

## Trimming

The `Quartz` package is marked trimmable, so an application published with `PublishTrimmed` can cut
away what it does not use. A job type is only kept if something the trimmer can see points at it, and
that is the one thing an application has to get right.

**Name job types as types.** `JobBuilder.Create<TJob>()`, `OfType<TJob>()`, `AddJob<TJob>()` and
`AddJobType<TJob>()` all declare what Quartz reflects over on a job — its public constructors, its
public properties and the interfaces it implements — so the trimmer keeps exactly those:

```csharp
builder.AddJob<SendEmailJob>(j => j.WithIdentity("send-email"));
```

**An API that takes a type name says so.** The ones that accept a name instead of a type carry
`[RequiresUnreferencedCode]`, so publishing trimmed reports them:

```csharp
// IL2026: a type named only by a string is not guaranteed to survive trimming
IJobDetail detail = JobBuilder.Create().OfType("Acme.Jobs.SendEmailJob, Acme.Jobs").Build();
```

That is `JobBuilder<TJob>.OfType(string)`, the `JobType(string)` constructor and the explicit cast from
`string`. Prefer the typed spelling; where you cannot, tell the trimmer to keep the type anyway with a
[trimmer root descriptor](https://learn.microsoft.com/dotnet/core/deploying/trimming/trimming-options#root-descriptors).

**Three places name job types by string whatever you do**, and each needs the job type registered or
rooted:

- an ADO.NET job store, because a stored job's type is the `JOB_CLASS_NAME` column;
- `job_scheduling_data` XML, whose loader is `[RequiresUnreferencedCode]` for that reason;
- the flat `quartz.*` configuration keys, which name plugins, listeners and job stores by string, and
  set their properties by name.

Registering every job type with `AddJob<TJob>()` or `AddJobType<TJob>()` covers the first of those:
those calls are what the trimmer follows, and the store then finds the type it needs.

::: tip
Native AOT is not supported yet — that work is tracked on
[issue #3341](https://github.com/quartznet/quartznet/issues/3341).
:::

## JobExecutionException

Finally, we need to inform you of a few details of the `IJob.Execute(..)` method. The only type of exception
that you should throw from the execute method is the JobExecutionException. Because of this, you should generally wrap the entire contents of the
execute method with a 'try-catch' block. The exception's directives to the scheduler are init-only
properties, set with an object initializer:

```csharp
catch (Exception ex)
{
    // ask the scheduler to run this fire again with the same context
    throw new JobExecutionException(ex) { RefireImmediately = true };
}
```

Besides `RefireImmediately` there are `UnscheduleFiringTrigger` and `UnscheduleAllTriggers`, which stop
the trigger that fired the job — or every trigger of the job — from firing again. When
`RefireImmediately` is set, the unschedule flags are ignored.
