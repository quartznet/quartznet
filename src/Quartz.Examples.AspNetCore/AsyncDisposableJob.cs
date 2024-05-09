using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Quartz.Examples.AspNetCore;

public class AsyncDisposableJob : IJob, IAsyncDisposable
{
    private readonly ILogger<AsyncDisposableDependency> logger;
    private readonly AsyncDisposableDependency dependency;

    public AsyncDisposableJob(
        ILogger<AsyncDisposableDependency> logger,
        AsyncDisposableDependency dependency)
    {
        this.logger = logger;
        this.dependency = dependency;
    }

    public Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("{JobType} running with dependency {DependencyType}", GetType().Name, dependency.GetType().Name);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
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
        logger.LogInformation("AsyncDisposableDependency async-disposing");
        return ValueTask.CompletedTask;
    }
}