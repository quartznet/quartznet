---

title: Multiple Triggers
---

A job can have several triggers. The job can carry baseline data and each trigger its own; at execution Quartz merges them, and trigger data overrides job data.

The example job:

```csharp
public class HelloJob : IJob
{
    public static readonly JobKey Key = new JobKey("customer-process", "group");

    public async Task Execute(IJobExecutionContext context)
    {
        var customerId = context.MergedJobDataMap.GetString("CustomerId");
        var batchSize = context.MergedJobDataMap.GetString("batch-size");

        await Console.WriteLineAsync($"CustomerId={customerId} batch-size={batchSize}")
    }
}
```

Two triggers, each with its own data, for one registered job:

```csharp
public async Task DoSomething(IScheduler scheduler, CancellationToken ct)
{
    var job = JobBuilder.Create<HelloJob>()
                        .WithIdentity(HelloJob.Key)
                        .Build();
    
    await scheduler.AddJob(job, replace: true, storeNonDurableWhileAwaitingScheduling: true, ct);

    // Trigger 1
    var jobData1 = new JobDataMap { { "CustomerId", "1" } };
    await scheduler.TriggerJob(HelloJob.Key, jobData1, ct);

    // Trigger 2
    var jobData2 = new JobDataMap { { "CustomerId", "2" } };
    await scheduler.TriggerJob(HelloJob.Key, jobData2, ct);
}
```

Output:

```text
CustomerId=1 batch-size=
CustomerId=2 batch-size=
```

### Job Data and Trigger Data

Set common data on the job itself:

```csharp
public async Task DoSomething(IScheduler scheduler, CancellationToken ct)
{
    var job = JobBuilder.Create<AnExampleJob>()
                        .WithIdentity(HelloJob.Key)
                        .UsingJobData("batch-size", "50")
                        .Build();
    
    await scheduler.AddJob(job, replace: true, storeNonDurableWhileAwaitingScheduling: true, ct);

    // Trigger 1
    var jobData1 = new JobDataMap { { "CustomerId", 1 } };
    await scheduler.TriggerJob(HelloJob.Key, jobData1, ct);

    // Trigger 2
    var jobData2 = new JobDataMap { { "CustomerId", 2 } };
    await scheduler.TriggerJob(HelloJob.Key, jobData2, ct);
}
```

Output:

```text
CustomerId=1 batch-size=50
CustomerId=2 batch-size=50
```
