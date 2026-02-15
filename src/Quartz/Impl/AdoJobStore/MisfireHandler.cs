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

            TimeSpan timeToSleep = TimeSpan.FromMilliseconds(50); // At least a short pause to help balance threads
            if (!recoverMisfiredJobsResult.HasMoreMisfiredTriggers)
            {
                timeToSleep = jobStoreSupport.MisfireHandlerFrequency - (jobStoreSupport.timeProvider.GetUtcNow() - sTime);
                if (timeToSleep <= TimeSpan.Zero)
                {
                    timeToSleep = TimeSpan.FromMilliseconds(50);
                }

                if (numFails > 0)
                {
                    timeToSleep = jobStoreSupport.DbRetryInterval > timeToSleep ? jobStoreSupport.DbRetryInterval : timeToSleep;
                }
            }

            await Task.Delay(timeToSleep, token).ConfigureAwait(false);
        }
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
            var timeout = Task.Delay(TimeSpan.FromSeconds(1));
            var completedTask = await Task.WhenAny(task, timeout).ConfigureAwait(false);
            
            if (completedTask == task)
            {
                // Task completed normally, await it to propagate any exceptions
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