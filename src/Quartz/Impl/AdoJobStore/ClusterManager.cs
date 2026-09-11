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

    /// <summary>
    /// The least the loop sleeps: what an overdue check-in waits before it is attempted, and the floor
    /// under a retry that has almost no window left to land in.
    /// </summary>
    private static readonly TimeSpan ShortPause = TimeSpan.FromMilliseconds(100);

    private int numFails;

    /// <summary>
    /// When this node last wrote a check-in that reached the database — the timestamp its peers read.
    /// </summary>
    /// <remarks>
    /// Kept here rather than read off <see cref="JobStoreSupport.LastCheckin" />, which has a second
    /// writer: a check-in that fails to <em>read</em> the state table stamps it too, so that
    /// <c>CalcFailedIfAfter</c> does not count this node's own outage against its peers. The scan runs
    /// before the write, so that is the stamp a database blip leaves, and a retry timed from it would
    /// believe it had a whole window left when the peers' clock says otherwise (#3777). Starts at
    /// construction, as the store's own does.
    /// </remarks>
    private DateTimeOffset lastSuccessfulCheckIn;

    internal ClusterManager(JobStoreSupport jobStoreSupport)
    {
        this.jobStoreSupport = jobStoreSupport;
        cancellationTokenSource = new CancellationTokenSource();
        log = LogProvider.GetLogger(typeof(ClusterManager));
        lastSuccessfulCheckIn = SystemTime.UtcNow();
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

            // The write's own timestamp, not the clock: a check-in that recovered a peer returns later
            // than it wrote, and the peers measure from what it wrote.
            lastSuccessfulCheckIn = jobStoreSupport.LastCheckin;
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

            TimeSpan timeToSleep = ComputeTimeToSleep(
                jobStoreSupport.ClusterCheckinInterval,
                SystemTime.UtcNow() - lastSuccessfulCheckIn,
                jobStoreSupport.DbRetryInterval,
                numFails,
                jobStoreSupport.ClusterCheckinMisfireThreshold);

            await Task.Delay(timeToSleep, token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            if (await Manage().ConfigureAwait(false))
            {
                jobStoreSupport.SignalSchedulingChangeImmediately(SchedulerConstants.SchedulingSignalDateTime);
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }

    /// <summary>
    /// Determines how long to sleep before the next cluster check-in.
    /// </summary>
    /// <param name="clusterCheckinInterval">The configured check-in interval.</param>
    /// <param name="transpiredTime">Wall clock time elapsed since the last check-in that reached the database.</param>
    /// <param name="dbRetryInterval">The configured retry interval used when the last check-ins have failed.</param>
    /// <param name="numFails">Number of consecutive failed check-ins.</param>
    /// <param name="clusterCheckinMisfireThreshold">
    /// The slack the peers add to the interval before they write this node off. Together with the
    /// interval it is the window a failed check-in has to be retried in.
    /// </param>
    internal static TimeSpan ComputeTimeToSleep(
        TimeSpan clusterCheckinInterval,
        TimeSpan transpiredTime,
        TimeSpan dbRetryInterval,
        int numFails,
        TimeSpan clusterCheckinMisfireThreshold)
    {
        TimeSpan timeToSleep = clusterCheckinInterval - transpiredTime;
        if (timeToSleep <= TimeSpan.Zero)
        {
            timeToSleep = ShortPause;
        }
        else if (timeToSleep > clusterCheckinInterval)
        {
            // Backward clock jump: 'transpiredTime' went negative. Clamp so check-in resumes
            // within one interval instead of stalling for the length of the jump, which would
            // make peer nodes consider this instance failed.
            timeToSleep = clusterCheckinInterval;
        }

        if (numFails > 0)
        {
            // The peers write this node off once interval + threshold has passed since the row it
            // last wrote. A retry that lands after that arrives convicted, so while the window is
            // still open the retry is spent inside it — half of what is left each time, never longer
            // than DbRetryInterval, never shorter than the short pause — and only once it has closed
            // does the ordinary back-off apply. Java's ClusterManager sleeps
            // max(dbRetryInterval, timeToSleep) here; with the defaults that is a retry 22.5 s after
            // a row the peers stop trusting at 15 s, which is what #3777 reports, and this branch
            // departs from it on purpose.
            TimeSpan elapsed = transpiredTime < TimeSpan.Zero ? TimeSpan.Zero : transpiredTime; // a backward jump must not widen the window
            TimeSpan windowLeft = clusterCheckinInterval + clusterCheckinMisfireThreshold - elapsed;
            if (windowLeft > TimeSpan.Zero)
            {
                TimeSpan retry = TimeSpan.FromTicks(windowLeft.Ticks / 2); // TimeSpan / int does not exist on net462/net472/netstandard2.0
                if (retry > dbRetryInterval)
                {
                    retry = dbRetryInterval;
                }

                if (retry < ShortPause)
                {
                    retry = ShortPause;
                }

                timeToSleep = retry;
            }
            else if (dbRetryInterval > timeToSleep)
            {
                timeToSleep = dbRetryInterval;
            }
        }

        return timeToSleep;
    }
}
