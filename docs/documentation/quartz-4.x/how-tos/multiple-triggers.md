---

title: Multiple Triggers
---

# Multiple Triggers

A job can have any number of triggers. Put the data every firing shares on the job, and each firing's own
data on its trigger. Quartz merges the two before the job runs; on a shared key the trigger's value wins.

The example job reads both:

<!-- snippet: sample_multiple_triggers_job -->
```csharp
public sealed class CustomerProcessJob : IJob
{
    public static readonly JobKey Key = new("customer-process", "batch");

    private readonly ILogger<CustomerProcessJob> logger;

    public CustomerProcessJob(ILogger<CustomerProcessJob> logger)
    {
        this.logger = logger;
    }

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobDataMap data = context.MergedJobDataMap;

        string? customerId = data.GetString("CustomerId");
        int batchSize = data.GetInt("batch-size");

        logger.LogInformation("CustomerId={CustomerId} batch-size={BatchSize}", customerId, batchSize);
        return default;
    }
}
```
<!-- endSnippet -->

## One job, two triggers

Register the job once and give it two triggers, each with its own data:

<!-- snippet: sample_multiple_triggers_configuration -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<CustomerProcessJob>(j => j
        .WithIdentity(CustomerProcessJob.Key)
        .StoreDurably()
        .UsingJobData("batch-size", 50));

    q.AddTrigger<CustomerProcessJob>(t => t
        .ForJob(CustomerProcessJob.Key)
        .WithIdentity("customer-1-hourly")
        .UsingJobData("CustomerId", "1")
        .WithCronSchedule("0 0 * ? * *"));

    q.AddTrigger<CustomerProcessJob>(t => t
        .ForJob(CustomerProcessJob.Key)
        .WithIdentity("customer-2-nightly")
        .UsingJobData("CustomerId", "2")
        .UsingJobData("batch-size", 500)   // this trigger overrides the job's value
        .WithCronSchedule("0 0 2 ? * *"));
});
```
<!-- endSnippet -->

The hourly firing logs `CustomerId=1 batch-size=50`; the nightly one logs `CustomerId=2 batch-size=500`.

`StoreDurably()` lets the job be registered on its own rather than alongside one trigger. Without it the job
is deleted as soon as its last trigger is gone.

The same triggers built at run time, for customers not known at startup:

<!-- snippet: sample_multiple_triggers_at_run_time -->
```csharp
public async ValueTask ScheduleFor(
    IScheduler scheduler,
    IReadOnlyCollection<string> customers,
    CancellationToken cancellationToken)
{
    IJobDetail job = JobBuilder.Create<CustomerProcessJob>()
        .WithIdentity(CustomerProcessJob.Key)
        .StoreDurably()
        .UsingJobData("batch-size", 50)
        .Build();

    await scheduler.AddJob(job, new AddJobOptions { Replace = true }, cancellationToken);

    foreach (string customer in customers)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity($"customer-{customer}", "batch")
            .ForJob(CustomerProcessJob.Key)
            .UsingJobData("CustomerId", customer)
            .WithCronSchedule("0 0 * ? * *")
            .Build();

        await scheduler.ScheduleJob(trigger, cancellationToken: cancellationToken);
    }
}
```
<!-- endSnippet -->

`ScheduleJob(trigger)`, the overload without a job detail, schedules a trigger against a job that is already
stored. That is why the job is added durably first.

## Once each time the scheduler starts

From 4.4. `RunAtStartup(jobKey)` runs a job once whenever the scheduler starts, beside its own triggers. It is
what a crontab's `@reboot` means.

<!-- snippet: sample_multiple_triggers_run_at_startup -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.ScheduleJob<CustomerProcessJob>(
        trigger => trigger.WithIdentity("customer-process-hourly").WithCronSchedule("0 0 * ? * *"),
        job => job.WithIdentity(CustomerProcessJob.Key));

    // And once each time the scheduler starts, so the first run does not wait for the hour.
    q.RunAtStartup(CustomerProcessJob.Key);
});
```
<!-- endSnippet -->

| When | Runs |
|---|---|
| The scheduler starts | once, as soon as it can |
| It is built in standby, and started later | once, at that start |
| It leaves standby | no |
| The application restarts | once again |
| Each node of a cluster starts | once on that node |

- The run is a one-shot trigger in `SchedulerConstants.StartupGroup` (`QRTZ_STARTUP`), named anew on each
  start and deleted once it has fired. `context.Trigger.Key.Group` tells a startup run from a scheduled one.
- It is an ordinary trigger: `[DisallowConcurrentExecution]`, listeners and history apply.
- On a persistent store it is stored until it has fired. A run that a crash interrupts is recovered like any
  other firing.
- **A run that a crash prevented is replaced, not added to.** The next start of the same node unschedules that
  node's unfired startup triggers for the job. In a cluster it leaves other nodes' alone, and one already
  reserved or running is left to finish.
- In a cluster it is pinned to its node with [`PreferredNode.For`](../tutorial/node-affinity.md). It fails over
  to another node only while that node is down.
- The job has to be stored by then, durably or with a trigger of its own. If it is not, the start fails with
  `SchedulerException`.
- Naming the same job twice registers one run.

## Firing once, with data of its own

`TriggerJob` fires a stored job immediately, with a data map merged the same way a trigger's is. It creates no
trigger: use it to run a job on demand, not to schedule it.

<!-- snippet: sample_multiple_triggers_ad_hoc -->
```csharp
JobDataMap data = new() { { "CustomerId", "3" }, { "batch-size", 10 } };
await scheduler.TriggerJob(CustomerProcessJob.Key, data, cancellationToken);
```
<!-- endSnippet -->

::: warning `GetString` is the strict one
The numeric accessors are forgiving: `GetInt` returns `50` for both `50` and `"50"`. `GetString` is not: for
a value stored as an `int` it returns null rather than `"50"`, and `TryGetString` returns false. A job that
reads with `GetString` must be given strings. On a persistent store with `StoreJobDataAsStrings` this cannot
happen, because everything is stored and read back as a string.
:::
