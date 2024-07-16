using Microsoft.Extensions.Logging;

using Quartz.Impl;
using Quartz.Diagnostics;

using Topshelf;

namespace Quartz.Server;

/// <summary>
/// The main server logic.
/// </summary>
public class QuartzServer : ServiceControl, IQuartzServer
{
    private readonly ILogger<QuartzServer> logger;
    private ISchedulerFactory schedulerFactory = null!;
    private IScheduler scheduler = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuartzServer"/> class.
    /// </summary>
    public QuartzServer()
    {
        logger = LogProvider.CreateLogger<QuartzServer>();
    }

    /// <summary>
    /// Initializes the instance of the <see cref="QuartzServer"/> class.
    /// </summary>
    public virtual async ValueTask Initialize()
    {
        try
        {
            schedulerFactory = CreateSchedulerFactory();
            scheduler = await GetScheduler().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Server initialization failed: {ErrorMessage}", e.Message);
            throw;
        }
    }

    /// <summary>
    /// Gets the scheduler with which this server should operate with.
    /// </summary>
    /// <returns></returns>
    protected virtual ValueTask<IScheduler> GetScheduler()
    {
        return schedulerFactory.GetScheduler();
    }

    /// <summary>
    /// Returns the current scheduler instance (usually created in <see cref="Initialize" />
    /// using the <see cref="GetScheduler" /> method).
    /// </summary>
    protected virtual IScheduler Scheduler => scheduler;

    /// <summary>
    /// Creates the scheduler factory that will be the factory
    /// for all schedulers on this instance.
    /// </summary>
    /// <returns></returns>
    protected virtual ISchedulerFactory CreateSchedulerFactory()
    {
        return new StdSchedulerFactory();
    }

    /// <summary>
    /// Starts this instance, delegates to scheduler.
    /// </summary>
    public virtual async ValueTask Start()
    {
        try
        {
            await scheduler.Start().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Scheduler start failed: {ErrorMessage}", ex.Message);
            throw;
        }

        logger.LogInformation("Scheduler started successfully");
    }

    /// <summary>
    /// Stops this instance, delegates to scheduler.
    /// </summary>
#pragma warning disable CA1716
    public virtual async ValueTask Stop()
#pragma warning restore CA1716
    {
        try
        {
            await scheduler.Shutdown(waitForJobsToComplete: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduler stop failed: {ErrorMessage}", ex.Message);
            throw;
        }

        logger.LogInformation("Scheduler shutdown complete");
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public virtual void Dispose()
    {
        // no-op for now
    }

    /// <summary>
    /// Pauses all activity in scheduler.
    /// </summary>
    public virtual async ValueTask Pause()
    {
        await scheduler.PauseAll().ConfigureAwait(false);
    }

    /// <summary>
    /// Resumes all activity in server.
    /// </summary>
    public async ValueTask Resume()
    {
        await scheduler.ResumeAll().ConfigureAwait(false);
    }

    /// <summary>
    /// TopShelf's method delegated to <see cref="Start()"/>.
    /// </summary>
    public bool Start(HostControl hostControl)
    {
        Start().AsTask().GetAwaiter().GetResult();
        return true;
    }

    /// <summary>
    /// TopShelf's method delegated to <see cref="Stop()"/>.
    /// </summary>
    public bool Stop(HostControl hostControl)
    {
        Stop().AsTask().GetAwaiter().GetResult();
        return true;
    }
}