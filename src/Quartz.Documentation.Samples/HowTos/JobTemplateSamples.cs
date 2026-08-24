using Microsoft.Extensions.Logging;

namespace Quartz.Documentation.Samples.HowTos;

#region sample_job_template

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

#endregion
