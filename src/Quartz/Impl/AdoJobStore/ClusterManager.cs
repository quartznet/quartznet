using Microsoft.Extensions.Logging;

using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

internal sealed class ClusterManager
{
    private readonly ILogger<ClusterManager> logger;

    // keep constant lock requestor id for manager's lifetime
    private readonly Guid requestorId = Guid.NewGuid();

    private readonly AdoJobStoreBase jobStoreSupport;

    private QueuedTaskScheduler taskScheduler = null!;
    private readonly CancellationTokenSource cancellationTokenSource;

    /// <summary>
    /// The loop's token, read once here rather than off the source each time it is wanted: the source
    /// is released at shutdown, after which asking it for its token throws, while the token itself
    /// stays readable.
    /// </summary>
    private readonly CancellationToken cancellationToken;

    /// <summary>Whether <see cref="Shutdown" /> has already run, so that it runs exactly once.</summary>
    private int shutdownEntered;

    private Task task = null!;

    // Timeout for waiting for the cluster manager task during shutdown.
    // This prevents hanging if the scheduler was disposed before it could schedule the task.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(1);

    private int numFails;

    /// <remarks>
    /// The logger is handed over rather than created here, because it is the store's - and therefore the
    /// container's. Cluster recovery and check-in failures are among the log lines an application most
    /// wants and least expects to have to opt into.
    /// </remarks>
    internal ClusterManager(AdoJobStoreBase jobStoreSupport, ILogger<ClusterManager> logger)
    {
        this.jobStoreSupport = jobStoreSupport;
        this.logger = logger;
        cancellationTokenSource = new CancellationTokenSource();
        cancellationToken = cancellationTokenSource.Token;
    }

    public async Task Initialize()
    {
        await Manage().ConfigureAwait(false);
        string threadName = $"QuartzScheduler_{jobStoreSupport.InstanceName}-{jobStoreSupport.InstanceId}_ClusterManager";

        taskScheduler = new QueuedTaskScheduler(threadCount: 1, threadPriority: ThreadPriority.AboveNormal, threadName: threadName, useForegroundThreads: !jobStoreSupport.UseBackgroundThreads);
        task = Task.Factory.StartNew(() => Run(cancellationToken), cancellationToken, TaskCreationOptions.HideScheduler, taskScheduler).Unwrap();
    }

    /// <remarks>
    /// One-shot: the token source is released at the end, and a source that has been released answers
    /// Cancel with an ObjectDisposedException rather than doing nothing.
    /// </remarks>
    public async Task Shutdown()
    {
        if (Interlocked.Exchange(ref shutdownEntered, 1) == 1)
        {
            return;
        }

        try
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
                // CancellationToken.None deliberately: the loop's own token is already cancelled at this
                // point, and passing it would abort the graceful wait we are here for.
                await task.WaitAsync(ShutdownTimeout, jobStoreSupport.timeProvider, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The task didn't complete within the timeout, it was likely never scheduled
            }
            catch (OperationCanceledException)
            {
                // Expected when the task is cancelled
            }
        }
        finally
        {
            // Cancelled first, which is what makes this safe: the loop reads a token it captured at
            // construction, and everything it hands the token to sees a cancellation that has already
            // happened rather than one it has to register for.
            cancellationTokenSource.Dispose();
        }
    }

    private async ValueTask<bool> Manage()
    {
        bool res = false;
        try
        {
            // CancellationToken.None deliberately: a check-in abandoned halfway leaves this node's row
            // unwritten, and the peers that read it decide a node that stops arriving has failed and
            // recover work it is still doing. The round trip is short and finishes.
            res = await jobStoreSupport.CheckIn(requestorId, CancellationToken.None).ConfigureAwait(false);

            numFails = 0;
            logger.CheckInComplete();
        }
        catch (Exception e)
        {
            if (numFails % jobStoreSupport.RetryableActionErrorLogThreshold == 0)
            {
                logger.ClusterManagementFailed(e.Message, e);
            }
            numFails++;
        }
        return res;
    }

    /// <summary>
    /// Checks in for as long as the scheduler is up.
    /// </summary>
    /// <remarks>
    /// The token is the loop's only way out and is tested on both sides of the wait, so a shutdown
    /// during a check-in ends the loop at the top of the next pass rather than starting another one.
    /// Written as a condition rather than as <c>while (true)</c> with a throw: the exit is then
    /// something a reader — and an analyzer — can see, and a shutdown that arrives between two passes
    /// ends the loop instead of the task.
    /// </remarks>
    private async Task Run(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TimeSpan timeToSleep = ComputeTimeToSleep(
                jobStoreSupport.ClusterCheckinInterval,
                jobStoreSupport.timeProvider.GetUtcNow() - jobStoreSupport.LastCheckin,
                jobStoreSupport.DbRetryInterval,
                numFails);

            await Task.Delay(timeToSleep, jobStoreSupport.timeProvider, token).ConfigureAwait(false);

            if (token.IsCancellationRequested)
            {
                break;
            }

            if (await Manage().ConfigureAwait(false))
            {
                await jobStoreSupport.SignalSchedulingChangeImmediately(SchedulerConstants.SchedulingSignalDateTime, token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Determines how long to sleep before the next cluster check-in.
    /// </summary>
    /// <param name="clusterCheckinInterval">The configured check-in interval.</param>
    /// <param name="transpiredTime">Wall clock time elapsed since the last successful check-in.</param>
    /// <param name="dbRetryInterval">The configured retry interval used when the last check-ins have failed.</param>
    /// <param name="numFails">Number of consecutive failed check-ins.</param>
    internal static TimeSpan ComputeTimeToSleep(
        TimeSpan clusterCheckinInterval,
        TimeSpan transpiredTime,
        TimeSpan dbRetryInterval,
        int numFails)
    {
        TimeSpan timeToSleep = clusterCheckinInterval - transpiredTime;
        if (timeToSleep <= TimeSpan.Zero)
        {
            timeToSleep = TimeSpan.FromMilliseconds(100);
        }
        else if (timeToSleep > clusterCheckinInterval)
        {
            // Backward clock jump: 'transpiredTime' went negative. Clamp so check-in resumes
            // within one interval instead of stalling for the length of the jump, which would
            // make peer nodes consider this instance failed.
            timeToSleep = clusterCheckinInterval;
        }

        if (numFails > 0 && dbRetryInterval > timeToSleep)
        {
            timeToSleep = dbRetryInterval;
        }

        return timeToSleep;
    }
}