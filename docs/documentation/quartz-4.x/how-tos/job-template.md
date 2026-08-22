---

title: Job Template
---

# Job Template

This page pulls the recommendations scattered through the documentation into one job class that can be copied
and cut down.

```csharp
// one job definition at a time: a second firing waits for the one in progress
[DisallowConcurrentExecution]
public sealed class SampleJob : IJob
{
    // a public key that is easy to reference from configuration and from maintenance code;
    // the group is what lets you address a set of jobs at once, e.g. pause everything in "integration"
    public static readonly JobKey Key = new("sample-job", "examples");

    // the job is resolved from the container for every firing, in a scope of its own,
    // so scoped dependencies are safe to take here
    private readonly IOrderService orders;
    private readonly ILogger<SampleJob> logger;

    public SampleJob(IOrderService orders, ILogger<SampleJob> logger)
    {
        this.orders = orders;
        this.logger = logger;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.RefireCount > 10)
        {
            // we might not ever succeed!
            // maybe log a warning, throw another type of error, inform the engineer on call
            logger.LogWarning("{JobKey} has refired {Count} times; giving up", Key, context.RefireCount);
            return;
        }

        try
        {
            // read configuration from the merged map: the job's own data, with this trigger's on top
            string? region = context.MergedJobDataMap.GetString("region");

            // ... do work — and forward the cancellation token, so an interrupt
            // or a shutdown can actually stop the job
            int processed = await orders.Process(region, cancellationToken);

            // anything a listener, the history plugin or a chained job should see
            context.Result = processed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // the scheduler asked the job to stop; let the cancellation flow
            throw;
        }
        catch (Exception ex)
        {
            // do you want the job to refire?
            throw new JobExecutionException(ex) { RefireImmediately = true };
        }
    }
}
```

A few notes on the choices in it:

* **`[DisallowConcurrentExecution]`** applies per job definition, not per class, so two different job details of
  the same class still run side by side. Leave it off for a job that is safe to overlap; a job that writes to
  the same rows every run usually is not.
* **`JobExecutionException`** is the exception to throw out of `Execute`. Its directives are init-only
  properties: `RefireImmediately` re-runs the same firing, and `UnscheduleFiringTrigger` /
  `UnscheduleAllTriggers` stop this trigger, or every trigger of the job, from firing again. Any other
  exception is caught, logged, reported to scheduler listeners as a `JobExecutionProcessException` and wrapped
  in a `JobExecutionException` with none of those flags set — so the failure is visible, but the schedule
  simply carries on.
* **The cancellation token** is the same one as `context.CancellationToken`. Forwarding it is what makes a
  shutdown that waits for jobs, or an `Interrupt` call, actually reach the work.
* **`context.Result`** is stored on the execution context and passed to job listeners after the job returns.
  It is not persisted.
