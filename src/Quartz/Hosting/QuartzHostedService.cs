using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Lifetime = Microsoft.Extensions.Hosting.IHostApplicationLifetime;

namespace Quartz;

public class QuartzHostedService : IHostedLifecycleService
{
    private readonly Lifetime applicationLifetime;
    private readonly ISchedulerFactory schedulerFactory;
    private readonly IOptions<QuartzHostedServiceOptions> options;
    private IScheduler? scheduler;
    internal Task? startupTask;

    public QuartzHostedService(
        Lifetime applicationLifetime,
        ISchedulerFactory schedulerFactory,
        IOptions<QuartzHostedServiceOptions> options)
    {
        this.applicationLifetime = applicationLifetime;
        this.schedulerFactory = schedulerFactory;
        this.options = options;
    }

    public virtual Task StartingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual async Task StartAsync(CancellationToken cancellationToken)
    {
        // Require successful initialization for application startup to succeed
        scheduler = await schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);

        if (options.Value.AwaitApplicationStarted) // Sensible mode: proceed with startup, and have jobs start after application startup
        {
            // Follow the pattern from BackgroundService.StartAsync: https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/BackgroundService.cs

            startupTask = AwaitStartupCompletionAndStartSchedulerAsync(cancellationToken);

            // If the task completed synchronously, await it in order to bubble potential cancellation/failure to the caller
            // Otherwise, return, allowing application startup to complete
            if (startupTask.IsCompleted)
            {
                await startupTask.ConfigureAwait(false);
            }
        }
        else // Legacy mode: start jobs inline
        {
            startupTask = StartSchedulerAsync(cancellationToken);
            await startupTask.ConfigureAwait(false);
        }
    }

    public virtual Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task AwaitStartupCompletionAndStartSchedulerAsync(CancellationToken startupCancellationToken)
    {
        using var combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(startupCancellationToken, applicationLifetime.ApplicationStarted);

        await Task.Delay(Timeout.InfiniteTimeSpan, combinedCancellationSource.Token) // Wait "indefinitely", until startup completes or is aborted
            .ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.OnlyOnCanceled, TaskScheduler.Default) // Without an OperationCanceledException on cancellation
            .ConfigureAwait(false);

        if (!startupCancellationToken.IsCancellationRequested)
        {
            await StartSchedulerAsync(applicationLifetime.ApplicationStopping).ConfigureAwait(false); // Startup has finished, but ApplicationStopping may still interrupt starting of the scheduler
        }
    }

    /// <summary>
    /// Starts the <see cref="IScheduler"/>, either immediately or after the delay configured in the <see cref="options"/>.
    /// </summary>
    private async Task StartSchedulerAsync(CancellationToken cancellationToken)
    {
        if (scheduler is null)
        {
            throw new InvalidOperationException("The scheduler should have been initialized first.");
        }

        // Avoid potential race conditions between ourselves and StopAsync, in case it has already made its attempt to stop the scheduler
        if (applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            return;
        }

        if (options.Value.StartDelay.HasValue)
        {
            await scheduler.StartDelayed(options.Value.StartDelay.Value, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await scheduler.Start(cancellationToken).ConfigureAwait(false);
        }
    }

    public virtual Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stopped without having been started
        if (scheduler is null || startupTask is null)
        {
            return;
        }

        try
        {
            // Wait until any ongoing startup logic has finished or the graceful shutdown period is over
            await Task.WhenAny(startupTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            // we always need to call shutdown to ensure that we unbind the scheduler from global repository
            await scheduler.Shutdown(options.Value.WaitForJobsToComplete, cancellationToken).ConfigureAwait(false);
        }
    }

    public virtual Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}