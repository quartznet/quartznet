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
    /// Kept here rather than read off <see cref="AdoJobStoreBase.LastCheckin" />, which has a second
    /// writer: a check-in that fails to <em>read</em> the state table stamps it too, so that
    /// <c>CalcFailedIfAfter</c> does not count this node's own outage against its peers. The scan runs
    /// before the write, so that is the stamp a database blip leaves, and a retry timed from it would
    /// believe it had a whole window left when the peers' clock says otherwise (#3777). Starts at
    /// construction, as the store's own does.
    /// </remarks>
    private DateTimeOffset lastSuccessfulCheckIn;

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
        lastSuccessfulCheckIn = jobStoreSupport.timeProvider.GetUtcNow();
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

            // The write's own timestamp, not the clock: a check-in that recovered a peer returns later
            // than it wrote, and the peers measure from what it wrote.
            lastSuccessfulCheckIn = jobStoreSupport.LastCheckin;
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
                jobStoreSupport.timeProvider.GetUtcNow() - lastSuccessfulCheckIn,
                jobStoreSupport.DbRetryInterval,
                numFails,
                jobStoreSupport.ClusterCheckinMisfireThreshold);

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
