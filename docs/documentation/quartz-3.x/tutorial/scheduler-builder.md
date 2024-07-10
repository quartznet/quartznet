---

title: Tuning the Scheduler
---

# Tuning the Scheduler

| Property                                   |                                                                                                                      |
|--------------------------------------------|----------------------------------------------------------------------------------------------------------------------|
| Scheduler Name                             | The [instance name](/documentation/quartz-3.x/configuration/reference.html#main-configuration), used when clustering |
| Scheduler Id                               | The [instance id](/documentation/quartz-3.x/configuration/reference.html#main-configuration). Can be auto-generated  |
| Max Batch Size                             | max number of jobs to run at one time                                                                                |
| InterruptJobsOnShutdown                    | ..                                                                                                                   |
| InterruptJobsOnShutdownWithWait            | ..                                                                                                                   |
| BatchTriggerAcquisitionFireAheadTimeWindow | ..                                                                                                                   |

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
            opt.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.Zero;
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
