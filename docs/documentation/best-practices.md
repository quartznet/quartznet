---

title: Best Practices
---

# Best Practices

## JobDataMap Tips

### Only Store Primitive Data Types (including Strings) In the JobDataMap

Only store primitive data types (including strings) in JobDataMap to avoid data serialization issues short and long-term.

### Use the Merged JobDataMap

The JobDataMap that is found on the `JobExecutionContext` during Job execution serves as a convenience.
The data in the JobDataMap is a merger of the JobDetail and the Trigger, with the data in the Trigger overriding
any same-named value in the Job.

Storing JobDataMap values on a Trigger can be useful in the case where you have a Job that is stored in the scheduler for regular/repeated use by multiple Triggers,
yet with each independent triggering, you want to supply the Job with different data inputs.

We recommend that code within the `IJob.Execute(..)` method should retrieve
values from the `MergedJobDataMap` on the `JobExecutionContext`, rather than directly
from the JobDetail or Trigger.

```csharp
public class SomeJob : IJob 
{
    public static readonly JobKey Key = new JobKey("job-name", "group-name");

    public Task Execute(IJobExecutionContext context) 
    {
        // don't do this
        var badMethod = context.JobDetail.JobDataMap.GetString("a-value");
        var alsoBadMethod = context.Trigger.JobDataMap.GetString("a-value");

        // do this
        var goodMethod = context.MergedJobDataMap.GetString("a-value");
    }
}
```

## Job Tips

### Static Job Key

To simplify `JobKey` access we recommend defining a static field that allows
easy access to the job's key.

```csharp
public class SomeJob : IJob 
{
    public static readonly JobKey Key = new JobKey("job-name", "group-name");

    public Task Execute(IJobExecutionContext context) { /* elided */ }
}
```

then later you can trigger the job directly with

```csharp
public async Task DoSomething(IScheduler schedule, CancellationToken ct)
{
    await schedule.TriggerJob(SomeJob.Key, ct)
}
```

or schedule it with a trigger

```csharp
public async Task DoSomething(IScheduler schedule, CancellationToken ct)
{
    var trigger = TriggerBuilder.Create()
                .WithIdentity("a-trigger", "a-group")
                .ForJob(SomeJob.Key)
                .StartNow()
                .Build();

    await schedule.ScheduleJob(trigger, ct)
}
```

## Trigger Tips

### Use TriggerUtils

TriggerUtils:

* Offers a simple way to create Dates (for start/end dates)
* Offers helpers for analyzing triggers (e.g. calculating future fire times)

### Use ScheduleJobs

When it is necessary to use multiple jobs with a large number of them in a scheduler (e.g. when calling the same job with different JobData) it is rational to call the `ScheduleJobs` method instead of triggering jobs in a loop or calling them manually one by one:

```csharp
Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> jobsDictionary = new();
foreach (var data in allData)
{
    var triggerSet = new HashSet<ITrigger>();
    IJobDetail job = JobBuilder.Create<JobName>()
        .UsingJobData("jobData", data.ToString())
        .Build();
    ITrigger trigger = TriggerBuilder.Create()
        .ForJob(job)
        .Build();
    triggerSet.Add(trigger);
    jobsDictionary.Add(job, triggerSet);
}
await scheduler.ScheduleJobs(jobsDictionary, replace: true);
```

## ADO.NET JobStore

### Never Write Directly To Quartz's Tables

Writing scheduling data directly to the database (via SQL) rather than using scheduling API:

* Results in data corruption (deleted data, scrambled data)
* Results in job seemingly "vanishing" without executing when a trigger's fire time arrives
* Results in job not executing "just sitting there" when a trigger's fire time arrives
* May result in: Dead-locks
* Other strange problems and data corruption

### Never Point A Non-Clustered Scheduler At the Same Database As Another Scheduler With The Same Scheduler Name

If you point more than one scheduler instance at the same set of database tables, and one or more of those instances is not configured for clustering, any of the following may occur:

* Results in data corruption (deleted data, scrambled data)
* Results in job seemingly "vanishing" without executing when a trigger's fire time arrives
* Results in job not executing, "just sitting there" when a trigger's fire time arrives
* May result in: Dead-locks
* Other strange problems and data corruption

### Ensure Adequate Datasource Connection Size

It is recommended that your Datasource max connection size be configured to be at least the number of worker threads in the thread pool plus three.
You may need additional connections if your application is also making frequent calls to the scheduler API.

## Daylight Savings Time

### Avoid Scheduling Jobs Near the Transition Hours of Daylight Savings Time

NOTE: Specifics of the transition hour and the amount of time the clock moves forward or back varies by locale see: [https://secure.wikimedia.org/wikipedia/en/wiki/Daylight_saving_time_around_the_world](https://secure.wikimedia.org/wikipedia/en/wiki/Daylight_saving_time_around_the_world).

SimpleTriggers are not affected by Daylight Savings Time as they always fire at an exact millisecond in time, and repeat an exact number of milliseconds apart.

Because CronTriggers fire at given hours/minutes/seconds, they are subject to some oddities when DST transitions occur.

As an example of possible issues, scheduling in the United States within TimeZones/locations that observe Daylight Savings time, the following problems may occur if using CronTrigger and scheduling fire times during the hours of 1:00 AM and 2:00 AM:

* 1:05 AM may occur twice! - duplicate firings on CronTrigger possible
* 2:05 AM may never occur! - missed firings on CronTrigger possible

Again, specifics of time and amount of adjustment varies by locale.

Other trigger types that are based on sliding along a calendar (rather than exact amounts of time), such as CalenderIntervalTrigger, will be similarly affected - but rather than missing a firing, or firing twice, may end up having it's fire time shifted by an hour.

## Jobs

### Waiting For Conditions

Long-running jobs prevent others from running (if all threads in the ThreadPool are busy).

If you feel the need to call Thread.sleep() on the worker thread executing the Job, it is typically a sign that the job is not ready to do the rest of its work because it needs to wait for some condition (such as the availability of a data record) to become true.

A better solution is to release the worker thread (exit the job) and allow other jobs to execute on that thread. The job can reschedule itself, or other jobs before it exits.

### Throwing Exceptions

A Job's execute method should contain a try-catch block that handles all possible exceptions.

If a job throws an exception, Quartz will treat the current firing as failed and, by default, will not immediately re-execute the job. (Unhandled exceptions are wrapped into a `JobExecutionException` with `refireImmediately: false`.)

If you do want Quartz to immediately re-execute the job, you can throw a `JobExecutionException` with `refireImmediately: true`, but use this with caution as it can create a tight failure loop.

It's better if the job catches all exceptions it may encounter, handles them, and reschedules itself or other jobs to work around the issue.

### Recoverability and Idempotence

In-progress Jobs marked "recoverable" are automatically re-executed after a scheduler fails. This means some of the job's "work" will be executed twice.

This means the job should be coded in such a way that its work is idempotent.

## Listeners (TriggerListener, JobListener, SchedulerListener)

### Keep Code In Listeners Concise And Efficient

Performing large amounts of work is discouraged, as the thread that would be executing the job (or completing the trigger and moving on to firing another job, etc.) will be tied up within the listener.

### Handle Exceptions

Every listener method should contain a try-catch block that handles all possible exceptions.

If a listener throws an exception, it may cause other listeners not to be notified and/or prevent the execution of the job, etc.

## Exposing Scheduler Functionality Through Applications

### Be Careful of Security

Some users expose Quartz's Scheduler functionality through an application user interface. This can be very useful, though it can also be extremely dangerous.

Be sure you don't mistakenly allow users to define jobs of any type they wish, with whatever parameters they wish.
For example, Quartz.Jobs package ships with a pre-made job `NativeJob`, which will execute any arbitrary native (operating system) system command that it is defined to.
Malicious users could use this to take control of, or destroy your system.

Likewise other jobs such as `SendEmailJob`, and virtually any others could be used for malicious intent.

Allowing users to define whatever job they want effectively opens your system to all sorts of vulnerabilities comparable/equivalent to Command Injection Attacks as defined by OWASP and MITRE.
