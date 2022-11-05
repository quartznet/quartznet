---
title: Tuning the Scheduler
---

# Tuning the Scheduler

| Property | |
|---|--|
| Scheduler Id | .. |
| Scheduler Name | .. |
| Max Batch Size | .. |
| InterruptJobsOnShutdown | .. |
| InterruptJobsOnShutdownWithWait| .. |
| BatchTriggerAcquisitionFireAheadTimeWindow | .. |

## Microsoft Hosting Extensions

```csharp
var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services => 
    {
        services.AddQuartz(opt => 
        {
            opt.SchedulerId = "";
            opt.SchedulerName = "";
            opt.MaxBatchSize = "";
            opt.InterruptJobsOnShutdown = true;
            opt.InterruptJobsOnShutdownWithWait = true;
            q.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.Zero;
        });
    })
    .Build();
```

# Building By Hand

```csharp
var scheduler = ScheduleBuilder().Create()
    .WithMisfireThreshold(TimeSpan.FromDays(1))
    .WithId("")
    .WithName("")
    .WithMaxBatchSize(2)
    .WithInterruptJobsOnShutdown(true)
    .WithInterruptJobsOnShutdownWithWait(true)
    .WithBatchTriggerAcquisitionFireAheadTimeWindow(TimeSpan.FromMilliseconds(1))
    .Build();
```


