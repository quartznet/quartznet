---

title: Job Template
---

# Job Template

The documentation's recommendations in one job class, to copy and cut down.

<!-- snippet: sample_job_template -->
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
<!-- endSnippet -->

* **`[DisallowConcurrentExecution]`** applies per job definition, not per class: two job details of the
  same class still run side by side. Leave it off for a job that is safe to overlap. A job that writes the
  same rows every run usually is not.
* **`JobExecutionException`** is the exception to throw from `Execute`. Its directives are init-only
  properties:
  * `RefireImmediately` re-runs the same firing.
  * `UnscheduleFiringTrigger` stops this trigger from firing again; `UnscheduleAllTriggers` stops every
    trigger of the job.

  Any other exception is caught, logged, reported to scheduler listeners as a
  `JobExecutionProcessException` and wrapped in a `JobExecutionException` with none of those flags set.
  The failure is visible, and the schedule carries on.
* **The cancellation token** is the same one as `context.CancellationToken`. Forward it, or neither a
  shutdown that waits for jobs nor an `Interrupt` call reaches the work.
* **`context.Result`** is stored on the execution context and passed to job listeners after the job
  returns. It is not persisted.
