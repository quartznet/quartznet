namespace Quartz.Examples.Worker;

public class TestSchedulerListener : ISchedulerListener
{
    private readonly ILogger<TestSchedulerListener> logger;

    public TestSchedulerListener(ILogger<TestSchedulerListener> logger)
    {
        this.logger = logger;
    }

    public ValueTask SchedulerStarting(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Scheduler starting");
        return ValueTask.CompletedTask;
    }
}

public class TestJobListener : IJobListener
{
    private readonly ILogger<TestJobListener> logger;

    public TestJobListener(ILogger<TestJobListener> logger)
    {
        this.logger = logger;
    }

    public string Name => nameof(TestJobListener);

    public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Job {Job} to be executed", context.JobDetail.Key);
        return ValueTask.CompletedTask;
    }
}

public class TestTriggerListener : ITriggerListener
{
    private readonly ILogger<TestTriggerListener> logger;

    public TestTriggerListener(ILogger<TestTriggerListener> logger)
    {
        this.logger = logger;
    }

    public string Name => nameof(TestSchedulerListener);

    public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Trigger {Trigger} fired", trigger.Key);
        return ValueTask.CompletedTask;
    }
}
