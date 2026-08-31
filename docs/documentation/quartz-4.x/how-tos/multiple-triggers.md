---

title: Multiple Triggers
---

# Multiple Triggers

A job can have any number of triggers. The job carries the data every firing shares; each trigger carries the
data that firing needs. Quartz merges the two before the job runs, and the trigger's values win where the keys
are the same.

Our example job reads both:

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

`StoreDurably()` is what lets the job be registered on its own rather than alongside one trigger. Without it a
job is deleted as soon as its last trigger is gone, which for a job with several triggers is rarely what you
want.

The same two triggers built at run time, for a job whose customers are not known at startup:

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

`ScheduleJob(trigger)` — the overload that takes no job detail — schedules a trigger against a job that is
already stored, which is why the job was added durably first.

## Firing once, with data of its own

`TriggerJob` fires a stored job immediately, with a data map that is merged the same way a trigger's would be.
It creates no trigger, so this is the way to run a job on demand rather than the way to schedule it:

<!-- snippet: sample_multiple_triggers_ad_hoc -->
```csharp
JobDataMap data = new() { { "CustomerId", "3" }, { "batch-size", 10 } };
await scheduler.TriggerJob(CustomerProcessJob.Key, data, cancellationToken);
```
<!-- endSnippet -->

::: warning `GetString` is the strict one
The numeric accessors are forgiving: `GetInt` parses `"50"` as happily as it returns `50`. `GetString` is not —
given a value stored as an `int` it returns null rather than `"50"`, and `TryGetString` returns false. So a job
that reads its data with `GetString` has to be given strings, which is worth remembering when the data comes
from somewhere loosely typed. On a persistent store with `StoreJobDataAsStrings` the question does not arise,
because everything is stored, and read back, as a string.
:::
