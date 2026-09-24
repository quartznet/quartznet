---

title: 'More About Jobs'
---

# More About Jobs

Your job class holds the code that does the work. Quartz.NET also needs the attributes of each job instance; these live in a `JobDetail`, built with the fluent `JobBuilder`.

The code from Lesson 1:

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
   .WithIntervalInSeconds(40)
   .RepeatForever())
  .Build();
  
await sched.ScheduleJob(job, trigger);
```

with the job class **HelloJob**:

```csharp
public class HelloJob : IJob
{
 public async Task Execute(IJobExecutionContext context)
 {
  await Console.Out.WriteLineAsync("HelloJob is executing.");
 }
}
```

The scheduler gets an `IJobDetail` that names the job's class. Every time it executes the job, it creates a new instance of the class before calling `Execute(..)`. So:

- jobs must have a no-argument constructor;
- data fields on the job class are pointless, because their values are not kept between executions.

:::tip Dependency Injection
With a dependency injection framework, the constructor can take service dependencies, like a controller in ASP.NET MVC.
:::

To give a job instance configuration, or to keep a job's state between executions, use the `JobDataMap` on the JobDetail.

## JobDataMap

`JobDataMap` holds any number of (serializable) objects for the job instance to use when it executes. It implements `IDictionary` and adds convenience methods for storing and retrieving primitive types.

Putting data into the JobDataMap before adding the job to the scheduler:

**Setting Values in a JobDataMap**

```csharp
// define the job and tie it to our DumbJob class
IJobDetail job = JobBuilder.Create<DumbJob>()
 .WithIdentity("myJob", "group1") // name "myJob", group "group1"
 .UsingJobData("jobSays", "Hello World!")
 .UsingJobData("myFloatValue", 3.141f)
 .Build();
```

Reading it during the job's execution:

**Getting Values from a JobDataMap**

```csharp
public class DumbJob : IJob
{
 public async Task Execute(IJobExecutionContext context)
 {
  JobKey key = context.JobDetail.Key;

        // note: use context.MergedJobDataMap in production code
  JobDataMap dataMap = context.JobDetail.JobDataMap;

  string jobSays = dataMap.GetString("jobSays");
  float myFloatValue = dataMap.GetFloat("myFloatValue");

  await Console.Error.WriteLineAsync("Instance " + key + " of DumbJob says: " + jobSays + ", and val is: " + myFloatValue);
 }
}
```

With a persistent JobStore (see [Job Stores](job-stores.md)), choose carefully what goes in the `JobDataMap`. Its objects are serialized, so they are prone to class-versioning problems. Standard .NET types are safe; for your own classes, any change to a class with serialized instances must not break compatibility.

`AdoJobStore` and `JobDataMap` can be put in a mode where the map stores only primitives and strings, which rules out later serialization problems.

If your job class has properties with a public `set` accessor named like the `JobDataMap` keys, Quartz's default JobFactory calls those setters when it instantiates the job, so `Execute` need not read the map. A custom `JobFactory` does not do this by default.

Triggers can have `JobDataMap`s too. Use them when one stored job is fired by several triggers and each trigger should pass different data.

The JobDataMap on the `JobExecutionContext` merges the `JobDataMap` of the `JobDetail` and of the `Trigger`; the trigger's values override same-named values from the job.

Reading the merged JobDataMap during execution:

```csharp
public class DumbJob : IJob
{
 public async Task Execute(IJobExecutionContext context)
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

Or let the JobFactory inject the data map values into properties:

```csharp
public class DumbJob : IJob
{
 public string JobSays { private get; set; }
 public float MyFloatValue { private get; set; }

 public async Task Execute(IJobExecutionContext context)
 {
  JobKey key = context.JobDetail.Key;

  JobDataMap dataMap = context.MergedJobDataMap;  // Note the difference from the previous example

  IList<DateTimeOffset> state = (IList<DateTimeOffset>)dataMap["myStateData"];
  state.Add(DateTimeOffset.UtcNow);

  await Console.Error.WriteLineAsync("Instance " + key + " of DumbJob says: " + JobSays + ", and val is: " + MyFloatValue);
 }
}
```

The class is longer, but `Execute()` is cleaner.

## Job "Instances"

One job class can have many instance definitions in the scheduler: create several JobDetails, each with its own properties and JobDataMap, and add them all.

For example, a job class "SalesReportJob" reads the sales person's name from its JobDataMap. You create two JobDetails, "SalesReportForJoyce" and "SalesReportForMike", with "Joyce" and "Mike" in their JobDataMaps.

When a trigger fires, its JobDetail is loaded and the job class is instantiated by the scheduler's JobFactory. The default JobFactory calls the job class's default constructor with `Activator.CreateInstance`, then calls the setter properties that match the JobDataMap's keys. Write your own JobFactory to, for example, have your IoC or DI container create and initialize the job instance.

Terms:

| Term | Means |
|---|---|
| "job definition" or "JobDetail instance" | a stored JobDetail |
| "job instance" or "instance of a job definition" | an executing job |
| "job" | usually a named definition, or JobDetail |
| "job type" | the class implementing the job interface |

## Job State and Concurrency

Two attributes on the job class change how Quartz handles a job's state (its JobDataMap) and concurrency.

`[DisallowConcurrentExecution]` tells Quartz not to execute multiple instances of one job definition concurrently. It applies per JobDetail, not per job class: if "SalesReportJob" has it, only one "SalesReportForJoyce" runs at a time, but it can run alongside "SalesReportForMike". The attribute is on the class because it often affects how the class is coded.

`[PersistJobDataAfterExecution]` tells Quartz to update the stored JobDataMap of the JobDetail after `Execute()` completes, even if it throws a JobExecutionException. The next execution of the same JobDetail then receives the updated values instead of the originally stored ones. It also applies per job definition, and is on the class because the code in `Execute` must be written with the statefulness in mind.

If you use **PersistJobDataAfterExecution**, strongly consider also using `[DisallowConcurrentExecution]`. Otherwise, when two instances of the same JobDetail run concurrently, which data is left stored is a race.

## Other Attributes Of Jobs

Other properties you can set on a JobDetail:

| Attribute | Effect |
|-|--|
| `Durability` | A non-durable job is deleted from the scheduler automatically once no active trigger is associated with it. |
| `RequestsRecovery` | The job is re-executed when the scheduler starts again, if it was executing during a hard shutdown. |

A hard shutdown means the process crashed or the machine was shut off. When a job is re-executed this way, `JobExecutionContext.Recovering` returns true.

## JobExecutionException

The only exception to throw from `IJob.Execute(..)` is JobExecutionException, so generally wrap the whole method body in a try-catch block. A JobExecutionException can also give the scheduler directives on how to handle the exception; see its documentation.
