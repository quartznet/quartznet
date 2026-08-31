namespace Quartz.Examples.AspNetCore;

// Five seconds for this job, whichever scheduler-wide default AddJobTimeout was given.
[JobTimeout("00:00:05")]
public class SlowJob : IJob
{
    private readonly Random random = new Random();
    private readonly ILogger<SlowJob> logger;

    public SlowJob(ILogger<SlowJob> logger)
    {
        this.logger = logger;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // simulate slow behavior happening from time to time
        var sleepTime = random.Next() % 2 == 0
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromSeconds(20);

        // in your own logic you should check if cancellationToken.IsCancellationRequested is set
        // for simplicity we just use Task.Delay which throws accordingly when interrupt requested.
        // The exception is deliberately not caught: the timeout middleware turns it into a
        // JobExecutionException naming the budget, which is what makes the overrun visible.
        await Task.Delay(sleepTime, cancellationToken);
        logger.LogInformation("Ran fast enough for the timeout not to interrupt");
    }
}
