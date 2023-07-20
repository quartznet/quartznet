namespace Quartz.Examples.AspNetCore;

public class SlowJob : IJob
{
    private readonly Random random = new Random();
    private readonly ILogger<SlowJob> logger;

    public SlowJob(ILogger<SlowJob> logger)
    {
        this.logger = logger;
    }

    public async ValueTask Execute(IJobExecutionContext context)
    {
        // simulate slow behavior happening from time to time
        var sleepTime = random.Next() % 2 == 0
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromSeconds(20);

        try
        {
            // in your own logic you should check if context.CancellationToken.IsCancellationRequested is set
            // for simplicity we just use Task.Delay which throws accordingly when interrupt requested

            await Task.Delay(sleepTime, context.CancellationToken);
            logger.LogInformation("Run fast enough for monitor not to interrupt");
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("Stopped processing due to interrupt, was I taking too long?");
        }
    }
}