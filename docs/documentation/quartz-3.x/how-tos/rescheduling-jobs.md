# Rescheduling Jobs

Ways to reschedule a job.

## Manually Retry

An unhandled exception escaping a running `IJob` ends that firing, and the trigger stays on its schedule. Reschedule the job by whatever method suits your system.

Quartz sets a trigger to the `Error` state only when its job cannot be built, or, since 3.22.4, when its fire fails [`quartz.jobStore.maxConsecutiveFireFailures`](../configuration/reference.md#quartz-jobstore-maxconsecutivefirefailures) times in a row. `IScheduler.ResetTriggerFromErrorState` brings it back.

## Using JobExecutionException

`JobExecutionException` controls whether the job refires immediately.

```csharp
public async Task Execute(IJobExecutionContext context)
{
    try 
    {
        // do work
    } catch (Exception ex)
    {
        throw new JobExecutionException(ex, refireImmediately: true)
        {
            UnscheduleFiringTrigger = true,
            UnscheduleAllTriggers = true
        };
    }
}
```

## Polly Retries

To retry the job's work, wrap it in a [Polly](https://github.com/App-vNext/Polly) retry policy. Long-running Polly retries hold a job slot, so the scheduler can do less other work.

## Self-Rescheduling

If the job needs to wait, say 5 minutes, reschedule it through the scheduler on `IJobExecutionContext` and let it exit normally.

```csharp
public async Task Execute(IJobExecutionContext context)
{
    // something happens, that tells you to delay the processing
    // like getting an HTTP 429 - Too Many requests
    var oldTrigger = context.Trigger;
    var newTrigger = TriggerBuilder.Create()
        .ForJob(context.JobDetail)
        .WithIdentity($"{oldTrigger.Key.Name}-retry", oldTrigger.Key.Group)
        .StartAt(DateTimeOffset.UtcNow.AddMinutes(5))
        .Build();
    await context.Scheduler.ScheduleJob(newTrigger);
}
```

## Self-Descheduling

Run the job every 5 minutes (or another suitable cadence) and have it unschedule itself after it succeeds. This is easier to reason about, but the job may still be calling the downstream services.

```csharp
public async Task Execute(IJobExecutionContext context)
{
    // work succeeds
    if(success)
    {
        await context.Scheduler.UnscheduleJob(context.Trigger.Key);
    }
}
```

---

[GitHub Discussion](https://github.com/quartznet/quartznet/discussions/2073)
