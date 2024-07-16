using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

internal sealed class ClusterManager
{
    private readonly ILogger<ClusterManager> logger;

    // keep constant lock requestor id for manager's lifetime
    private readonly Guid requestorId = Guid.NewGuid();

    private readonly JobStoreSupport jobStoreSupport;

    private QueuedTaskScheduler taskScheduler = null!;
    private readonly CancellationTokenSource cancellationTokenSource;
    private Task task = null!;

    private int numFails;

    internal ClusterManager(JobStoreSupport jobStoreSupport)
    {
        this.jobStoreSupport = jobStoreSupport;
        cancellationTokenSource = new CancellationTokenSource();
        logger = LogProvider.CreateLogger<ClusterManager>();
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
        try
        {
            taskScheduler.Dispose();
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async ValueTask<bool> Manage()
    {
        bool res = false;
        try
        {
            res = await jobStoreSupport.DoCheckin(requestorId).ConfigureAwait(false);

            numFails = 0;
            logger.LogDebug("Check-in complete.");
        }
        catch (Exception e)
        {
            if (numFails % jobStoreSupport.RetryableActionErrorLogThreshold == 0)
            {
                logger.LogError(e, "Error managing cluster: {ExceptionMessage}", e.Message);
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
            TimeSpan transpiredTime = jobStoreSupport.timeProvider.GetUtcNow() - jobStoreSupport.LastCheckin;
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
                await jobStoreSupport.SignalSchedulingChangeImmediately(SchedulerConstants.SchedulingSignalDateTime).ConfigureAwait(false);
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }
}