using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

internal sealed class MisfireHandler
{
    private readonly ILogger<MisfireHandler> logger;
    // keep constant lock requestor id for handler's lifetime
    private readonly Guid requestorId = Guid.NewGuid();

    private readonly JobStoreSupport jobStoreSupport;
    private int numFails;

    private readonly CancellationTokenSource cancellationTokenSource;
    private readonly QueuedTaskScheduler taskScheduler;
    private Task task = null!;

    // Timeout for waiting for the misfire handler task during shutdown.
    // This prevents hanging if the scheduler was disposed before it could schedule the task.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(1);

    internal MisfireHandler(JobStoreSupport jobStoreSupport)
    {
        this.jobStoreSupport = jobStoreSupport;
        logger = LogProvider.CreateLogger<MisfireHandler>();

        string threadName = $"QuartzScheduler_{jobStoreSupport.InstanceName}-{jobStoreSupport.InstanceId}_MisfireHandler";
        taskScheduler = new QueuedTaskScheduler(threadCount: 1, threadName: threadName, useForegroundThreads: !jobStoreSupport.MakeThreadsDaemons);
        cancellationTokenSource = new CancellationTokenSource();
    }

    public void Initialize()
    {
        task = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.HideScheduler, taskScheduler).Unwrap();
    }

    private async Task Run()
    {
        var token = cancellationTokenSource.Token;
        while (!token.IsCancellationRequested)
        {
            token.ThrowIfCancellationRequested();

            DateTimeOffset sTime = jobStoreSupport.timeProvider.GetUtcNow();

            RecoverMisfiredJobsResult recoverMisfiredJobsResult = await Manage().ConfigureAwait(false);

            if (recoverMisfiredJobsResult.ProcessedMisfiredTriggerCount > 0)
            {
                await jobStoreSupport.SignalSchedulingChangeImmediately(recoverMisfiredJobsResult.EarliestNewTime).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();

            TimeSpan timeToSleep = ComputeTimeToSleep(
                recoverMisfiredJobsResult.HasMoreMisfiredTriggers,
                jobStoreSupport.MisfireHandlerFrequency,
                jobStoreSupport.timeProvider.GetUtcNow() - sTime,
                jobStoreSupport.DbRetryInterval,
                numFails);

            await Task.Delay(timeToSleep, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Determines how long to sleep before the next misfire scan.
    /// </summary>
    /// <param name="hasMoreMisfiredTriggers">Whether the last scan left misfired triggers unprocessed.</param>
    /// <param name="misfireHandlerFrequency">The configured scan frequency.</param>
    /// <param name="elapsed">Wall clock time consumed by the last scan.</param>
    /// <param name="dbRetryInterval">The configured retry interval used when the last scans have failed.</param>
    /// <param name="numFails">Number of consecutive failed scans.</param>
    internal static TimeSpan ComputeTimeToSleep(
        bool hasMoreMisfiredTriggers,
        TimeSpan misfireHandlerFrequency,
        TimeSpan elapsed,
        TimeSpan dbRetryInterval,
        int numFails)
    {
        // At least a short pause to help balance threads
        TimeSpan minimumSleep = TimeSpan.FromMilliseconds(50);

        if (hasMoreMisfiredTriggers)
        {
            return minimumSleep;
        }

        TimeSpan timeToSleep = misfireHandlerFrequency - elapsed;
        if (timeToSleep <= TimeSpan.Zero)
        {
            timeToSleep = minimumSleep;
        }
        else if (timeToSleep > misfireHandlerFrequency)
        {
            // A negative 'elapsed' means the system clock jumped backward (NTP correction,
            // manual change, VM migration). Never sleep longer than one full cycle, otherwise
            // misfire handling stops for the entire duration of the jump.
            timeToSleep = misfireHandlerFrequency;
        }

        if (numFails > 0 && dbRetryInterval > timeToSleep)
        {
            timeToSleep = dbRetryInterval;
        }

        return timeToSleep;
    }

    public async ValueTask Shutdown()
    {
        cancellationTokenSource.Cancel();
        
        taskScheduler.Dispose();
        
        // Wait for the task to complete, but with a timeout to handle the race condition where
        // the scheduler was disposed before it could schedule the task.
        // In that scenario, the task will remain in WaitingForActivation indefinitely.
        // We use a short timeout because:
        // 1. If the task was already running, it will complete quickly due to the cancellation
        // 2. If the task was never scheduled, no amount of waiting will help
        try
        {
            using CancellationTokenSource timeoutCts = new CancellationTokenSource();
            var timeoutTask = Task.Delay(ShutdownTimeout, timeoutCts.Token);
            var completedTask = await Task.WhenAny(task, timeoutTask).ConfigureAwait(false);
            
            if (completedTask == task)
            {
                // Task completed normally, cancel the timeout timer to free resources
                await timeoutCts.CancelAsync().ConfigureAwait(false);
                // Await the task to propagate any exceptions
                await task.ConfigureAwait(false);
            }
            // else: Task didn't complete within timeout, it was likely never scheduled
        }
        catch (OperationCanceledException)
        {
            // Expected when the task is cancelled
        }
    }

    private async ValueTask<RecoverMisfiredJobsResult> Manage()
    {
        try
        {
            logger.LogDebug("Scanning for misfires...");

            RecoverMisfiredJobsResult res = await jobStoreSupport.DoRecoverMisfires(requestorId, CancellationToken.None).ConfigureAwait(false);
            numFails = 0;
            return res;
        }
        catch (Exception e)
        {
            if (numFails % jobStoreSupport.RetryableActionErrorLogThreshold == 0)
            {
                logger.LogError(e, "Error handling misfires: {ExceptionMessage}", e.Message);
            }
            numFails++;
        }
        return RecoverMisfiredJobsResult.NoOp;
    }
}