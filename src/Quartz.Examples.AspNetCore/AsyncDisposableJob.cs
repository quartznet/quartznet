namespace Quartz.Examples.AspNetCore;

public class AsyncDisposableJob : IJob, IAsyncDisposable
{
    private readonly ILogger<AsyncDisposableJob> logger;
    private readonly AsyncDisposableDependency dependency;

    public AsyncDisposableJob(
        ILogger<AsyncDisposableJob> logger,
        AsyncDisposableDependency dependency)
    {
        this.logger = logger;
        this.dependency = dependency;
    }

    public ValueTask Execute(IJobExecutionContext context)
    {
        logger.LogInformation("{JobType} running with dependency {DependencyType}", GetType().Name, dependency.GetType().Name);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        logger.LogInformation("{JobType} async-disposing", GetType().Name);
        return ValueTask.CompletedTask;
    }
}


public class AsyncDisposableDependency : IAsyncDisposable
{
    private readonly ILogger<AsyncDisposableDependency> logger;

    public AsyncDisposableDependency(ILogger<AsyncDisposableDependency> logger)
    {
        this.logger = logger;
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        logger.LogInformation("AsyncDisposableDependency async-disposing");
        return ValueTask.CompletedTask;
    }
}