---

title: Multiple Triggers
---

Quartz is designed with the ability to register a job with multiple triggers.
Each job can have a base line set of data, and then each trigger can bring its
own set of data as well. During the job execution Quartz will merge the data
for you, with the data in the trigger overriding the data in the job.

Our example job:

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

Below, we have two triggers, each with their own set of data, but
we only had to register the one job.

```csharp
public Task DoSomething(IScheduler schedule, CancellationToken ct)
{
    var job = JobBuilder.Create<HelloJob>()
                        .WithIdentity(HelloJob.Key)
                        .Build();
    
    await schedule.AddJob(job, replace: true, storeNonDurableWhileAwaitingScheduling: true, ct);

    // Trigger 1
    var jobData1 = new JobDataMap { { "CustomerId", "1" } };
    await scheduler.TriggerJob(new JobKey("customer-process", "group"), jobData1, ct);

    // Trigger 2
    var jobData2 = new JobDataMap { { "CustomerId", "2" } };
    await scheduler.TriggerJob(new JobKey("customer-process", "group"), jobData2, ct);
}
```

When this runs you will see:

```text
CustomerId=1 batch-size=
CustomerId=2 batch-size=
```

### Job Data and Trigger Data

You could even set common data parameters on the job itself. Here
we are adding some job data to the job itself.

```csharp
public Task DoSomething(IScheduler schedule, CancellationToken ct)
{
    var job = JobBuilder.Create<AnExampleJob>()
                        .WithIdentity(HelloJob.Key)
                        .UsingJobData("batch-size", "50")
                        .Build();
    
    await schedule.AddJob(job, replace: true, storeNonDurableWhileAwaitingScheduling: true, ct);

    // Trigger 1
    var jobData1 = new JobDataMap { { "CustomerId", 1 } };
    await scheduler.TriggerJob(HelloJob.Key, jobData1, ct);

    // Trigger 2
    var jobData2 = new JobDataMap { { "CustomerId", 2 } };
    await scheduler.TriggerJob(HelloJob.Key, jobData2, ct);
}
```

When this runs you will see:

```text
CustomerId=1 batch-size=50
CustomerId=2 batch-size=50
```
