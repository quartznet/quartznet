# Rescheduling Jobs

A few ways to approach a need to reschedule a job.

## Manually Retry

When a Quartz job is running, and an unhandled exception escapes the `IJob`, the Quartz system will mark the job in an error state. This would then allow you to reschedule the job using any method that would be work for your system.

## Using JobExecutionException

One simple option is to use the `JobExecutionException` to control if the job should refire immediately or not.

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

If your job simply needs to retry its work, then you could wrap the job in a [Polly](https://github.com/App-vNext/Polly) policy, and use the policy definitions to retry it. Note that using Polly to implement long running retries will maintain a job slot, and prevent the job engine for performing more work.

## Self-Rescheduling

If your job needs more time, say it needs to wait 5 minutes, the `IJobExecutionContext` has access to the scheduler on it. You could use that to reschedule the job, and let it exit normally.

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

Another approach, is to have the job run every 5 minutes (or some other suitable cadence) and after succeeding cancel itself. This has the added benefit of being easier to logically reason about, but could still be making calls to the downstream services.

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
