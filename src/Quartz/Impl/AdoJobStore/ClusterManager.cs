using System;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

internal sealed class ClusterManager
{
    private readonly ILog log;

    // keep constant lock requestor id for manager's lifetime
    private readonly Guid requestorId = Guid.NewGuid();

    private readonly JobStoreSupport jobStoreSupport;

    private QueuedTaskScheduler taskScheduler = null!;
    private readonly CancellationTokenSource cancellationTokenSource;
    private Task task = null!;

    // Timeout for waiting for the cluster manager task during shutdown.
    // This prevents hanging if the scheduler was disposed before it could schedule the task.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(1);

    private int numFails;

    internal ClusterManager(JobStoreSupport jobStoreSupport)
    {
        this.jobStoreSupport = jobStoreSupport;
        cancellationTokenSource = new CancellationTokenSource();
        log = LogProvider.GetLogger(typeof(ClusterManager));
    }

    public async Task Initialize()
    {
        await Manage().ConfigureAwait(false);
        string threadName = $"QuartzScheduler_{jobStoreSupport.InstanceName}-{jobStoreSupport.InstanceId}_ClusterManager";

        taskScheduler = new QueuedTaskScheduler(threadCount: 1, threadPriority: ThreadPriority.AboveNormal, threadName: threadName, useForegroundThreads: !jobStoreSupport.MakeThreadsDaemons);
        task = Task.Factory.StartNew(() => Run(cancellationTokenSource.Token), cancellationTokenSource.Token, TaskCreationOptions.HideScheduler, taskScheduler).Unwrap();
    }

    public async Task Shutdown()
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
                timeoutCts.Cancel();
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

    private async Task<bool> Manage()
    {
        bool res = false;
        try
        {
            res = await jobStoreSupport.DoCheckin(requestorId).ConfigureAwait(false);

            numFails = 0;
            log.Debug("Check-in complete.");
        }
        catch (Exception e)
        {
            if (numFails % jobStoreSupport.RetryableActionErrorLogThreshold == 0)
            {
                log.ErrorException("Error managing cluster: " + e.Message, e);
            }
            numFails++;
        }
        return res;
    }

    private async Task Run(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();

            TimeSpan timeToSleep = jobStoreSupport.ClusterCheckinInterval;
            TimeSpan transpiredTime = SystemTime.UtcNow() - jobStoreSupport.LastCheckin;
            timeToSleep = timeToSleep - transpiredTime;
            if (timeToSleep <= TimeSpan.Zero)
            {
                timeToSleep = TimeSpan.FromMilliseconds(100);
            }

            if (numFails > 0)
            {
                timeToSleep = jobStoreSupport.DbRetryInterval > timeToSleep ? jobStoreSupport.DbRetryInterval : timeToSleep;
            }

            await Task.Delay(timeToSleep, token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            if (await Manage().ConfigureAwait(false))
            {
                jobStoreSupport.SignalSchedulingChangeImmediately(SchedulerConstants.SchedulingSignalDateTime);
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }
}