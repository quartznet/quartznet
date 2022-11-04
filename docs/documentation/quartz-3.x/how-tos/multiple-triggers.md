---
title: Multiple Triggers
---

One nice thing about Quartz is that you can split the job registration from the 
triggers. One nice use case is being able to split out the JobData for each
trigger. 

Below, we have two triggers, each with their own set of data, but
we only had to register the one job.


```csharp
public Task DoSomething(IScheduler schedule, CancellationToken ct)
{
    var job = JobBuilder.Create<AnExampleJob>()
                        .WithIdentity("customer-process", "group")
                        .Build();
    
    var replace = true;
    var durable = true;
    await schedule.AddJob(job, replace, durable, ct);

    // Trigger 1
    var jobData1 = new JobDataMap { { "CustomerId", 1 } };
    await scheduler.TriggerJob(new JobKey("customer-process", "group"), jobData1, ct);

    // Trigger 2
    var jobData2 = new JobDataMap { { "CustomerId", 2 } };
    await scheduler.TriggerJob(new JobKey("customer-process", "group"), jobData2, ct);
}
```

You could even set common data parameters on the job itself. Here
we are adding some job data to the job itself.

```csharp
public Task DoSomething(IScheduler schedule, CancellationToken ct)
{
    var job = JobBuilder.Create<AnExampleJob>()
                        .WithIdentity("customer-process", "group")
                        .UsingJobData("batch-size", 50)
                        .Build();
    
    var replace = true;
    var durable = true;
    await schedule.AddJob(job, replace, durable, ct);

    // Trigger 1
    var jobData1 = new JobDataMap { { "CustomerId", 1 } };
    await scheduler.TriggerJob(new JobKey("customer-process", "group"), jobData1, ct);

    // Trigger 2
    var jobData2 = new JobDataMap { { "CustomerId", 2 } };
    await scheduler.TriggerJob(new JobKey("customer-process", "group"), jobData2, ct);
}
```

Using this flexbility in the data model should make working with Quartz a flexible solution.
