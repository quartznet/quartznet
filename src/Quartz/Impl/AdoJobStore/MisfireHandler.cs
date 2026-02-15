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
        
        // If the task was scheduled and started running, wait for it to complete
        // If it's still WaitingForActivation after disposing the scheduler, it means
        // the scheduler was disposed before it could schedule the task, so it will never run
        try
        {
            // Give a brief moment for the task to transition states if it was just queued
            await Task.Delay(10).ConfigureAwait(false);
            
            // Check if the task has actually started or will start
            // WaitingForActivation means the scheduler hasn't picked it up yet
            // After disposing the scheduler, it never will
            if (task.Status != TaskStatus.WaitingForActivation)
            {
                await task.ConfigureAwait(false);
            }
            // else: Task was never scheduled by the disposed scheduler, which is acceptable during shutdown
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