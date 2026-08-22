using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Extensibility;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl;

/// <summary>
/// An <see cref="IThreadPool"/> implementation which schedules tasks using
/// a <see cref="TaskScheduler"/> (provided by implementers).
/// </summary>
public abstract class TaskSchedulingThreadPool : IThreadPool
{
    private readonly ILogger<TaskSchedulingThreadPool> logger;

    // The token source used to cancel thread pool execution at shutdown
    // Note that cancellation is not propagated to the user-scheduled tasks currently executing,
    // only to the thread pool functions themselves (such as scheduling tasks).
    private readonly CancellationTokenSource shutdownCancellation = new CancellationTokenSource();

    /// <summary>
    /// Guards <see cref="runningTasksCountdown" />. A lock of its own, because the countdown itself is
    /// replaced by <see cref="Initialize" /> and is a BCL type others could lock on.
    /// </summary>
    private readonly Lock runningTasksLock = new();

    /// <summary>
    /// Allows us to wait until no running tasks remain.
    /// </summary>
    private CountdownEvent runningTasksCountdown = null!;

    /// <summary>
    /// Cached delegate to mark a given task as complete.
    /// </summary>
    private Action<Task> completeTask = null!;

    /// <summary>
    /// The semaphore used to limit concurrency and integers representing maximum
    /// concurrent tasks.
    /// </summary>
    private SemaphoreSlim concurrencySemaphore = null!;

    private int maxConcurrency;

    private TaskScheduler scheduler = null!;
    private bool isInitialized;
    private int shutdownInitialSignalDone;

    protected TaskSchedulingThreadPool() : this(ThreadPoolOptions.DefaultMaxConcurrency)
    {
    }

    protected TaskSchedulingThreadPool(int maxConcurrency)
    {
        logger = LogProvider.CreateLogger<TaskSchedulingThreadPool>();
        MaxConcurrency = maxConcurrency;
    }

    /// <summary>
    /// Gets or sets the <see cref="TaskScheduler"/> used to schedule tasks
    /// queued by users.
    /// </summary>
    /// <remarks>
    /// Once the thread pool is initialized, any attempts to change the value
    /// will be silently ignored.
    /// </remarks>
    public TaskScheduler Scheduler
    {
        get => scheduler;
        protected internal set
        {
            if (!isInitialized)
            {
                scheduler = value;
            }
        }
    }

    /// <summary>
    /// Implementers should override this to provide the TaskScheduler used
    /// by their thread pool.
    /// </summary>
    /// <remarks>
    /// The TaskScheduler is provided through this factory method instead of as a property
    /// so that it can take respect MaxConcurrency changes prior to initialization time
    /// </remarks>
    /// <returns>
    /// The default TaskScheduler the thread pool will use if users do
    /// not specify a different TaskScheduler prior to initialization
    /// </returns>
    protected abstract TaskScheduler GetDefaultScheduler();

    /// <summary>
    /// Gets or sets the maximum number of thread pool tasks which can be
    /// executing in parallel.
    /// </summary>
    /// <remarks>
    /// Once the thread pool is initialized, any attempts to change the value
    /// will be silently ignored.
    /// </remarks>
    public int MaxConcurrency
    {
        get => maxConcurrency;
        protected internal set
        {
            if (!isInitialized)
            {
                maxConcurrency = value;
            }
        }
    }

    /// <summary>
    /// The number of tasks that can run concurrently in this thread pool
    /// </summary>
    public virtual int PoolSize => MaxConcurrency;

    /// <summary>
    /// Initializes the thread pool for use
    /// </summary>
    /// <remarks>
    /// Note that after invoking this method, changes to <see cref="MaxConcurrency"/>
    /// and <see cref="Scheduler"/> are silently ignored.
    /// </remarks>
    public virtual ValueTask Initialize(CancellationToken cancellationToken = default)
    {
        // Checking for null allows users to specify their own scheduler prior to initialization.
        // If this is undesirable, the scheduler should be set here unconditionally.
        if (Scheduler is null)
        {
            Scheduler = GetDefaultScheduler();
        }

        // Initialize the concurrency semaphore with the proper initial count
        concurrencySemaphore = new SemaphoreSlim(MaxConcurrency);

        // We start with an initial count of one to make sure it doesn't start in "signaled" state.
        // Assigned under the lock that guards it, so that a caller already inside TryRun cannot add a
        // count to the countdown this replaces.
        lock (runningTasksLock)
        {
            runningTasksCountdown = new CountdownEvent(1);
        }

        // Reduce allocations by caching the delegate to mark a task as complete
        completeTask = SignalTaskComplete;

        // Thread pool is ready to go
        isInitialized = true;

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("TaskSchedulingThreadPool configured with max concurrency of {MaxConcurrency} and TaskScheduler {SchedulerName}.",
                MaxConcurrency, Scheduler.GetType().Name);
        }

        return default;
    }

    /// <summary>
    /// Determines the number of threads that are currently available in
    /// the pool; waits until at least one is available
    /// </summary>
    /// <returns>The number of currently available threads</returns>
    public async ValueTask<int> WaitForAvailableThreads(CancellationToken cancellationToken = default)
    {
        if (!isInitialized || shutdownCancellation.IsCancellationRequested)
        {
            return 0;
        }

        using CancellationTokenSource? linked = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(shutdownCancellation.Token, cancellationToken)
            : null;

        try
        {
            // There is a race condition here such that it's possible the method could return
            // 1 (or more) but no threads would be available a short time later when the scheduler
            // calls TryRun. This could be avoided by 'reserving' threads for callers of
            // WaitForAvailableThreads, but that would complicate this code and nothing should
            // break functionally if threads are used for other tasks in between WaitForAvailableThreads
            // being called and TryRun being called.
            //
            // The window of opportunity for such a race should be very small (unless the scheduler takes
            // a very long time to call TryRun).
            //
            // In the worst case, TryRun will just wait
            // for the next thread and clustered scenarios may experience some imbalanced loads.
            await concurrencySemaphore.WaitAsync(linked?.Token ?? shutdownCancellation.Token).ConfigureAwait(false);
            return 1 + concurrencySemaphore.Release();
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Schedules a task to run (using the task scheduler) as soon as concurrency rules allow it.
    /// </summary>
    /// <param name="action">The action to be executed</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see langword="true"/> if the task was successfully scheduled; otherwise, <see langword="false"/>.
    /// </returns>
    public async ValueTask<bool> TryRun(Func<ValueTask> action, CancellationToken cancellationToken = default)
    {
        if (action is null || !isInitialized || shutdownCancellation.IsCancellationRequested)
        {
            return false;
        }

        // Acquire the semaphore (return false if shutdown occurs while waiting)
        using (CancellationTokenSource? linked = cancellationToken.CanBeCanceled
                   ? CancellationTokenSource.CreateLinkedTokenSource(shutdownCancellation.Token, cancellationToken)
                   : null)
        {
            try
            {
                await concurrencySemaphore.WaitAsync(linked?.Token ?? shutdownCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        // Wrap the action in a Task to start it asynchronously. AsTask costs nothing when the
        // work completed synchronously and is the Task the machinery below needs otherwise.
        var task = new Task<Task>(() => action().AsTask());

        // Unrap the task so that we can work with the underlying task
        var unwrappedTask = task.Unwrap();

        lock (runningTasksLock)
        {
            // Now that the lock is held, shutdown can't proceed,
            // so double-check that no shutdown has started since the initial check.
            if (shutdownCancellation.IsCancellationRequested)
            {
                concurrencySemaphore.Release();
                return false;
            }

            // Record an extra running task
            runningTasksCountdown.AddCount();
        }

        // Register a callback to remove the task from the running list once it has completed
#pragma warning disable MA0134
        // Always runs: this is what releases the concurrency semaphore, so it must not be
        // skipped because the caller's token fired.
        _ = unwrappedTask.ContinueWith(completeTask, CancellationToken.None);
#pragma warning restore MA0134

        // Start the task using the task scheduler
        try
        {
            task.Start(Scheduler);
        }
        catch (TaskSchedulerException)
        {
            // Shutdown(waitForJobsToComplete: false) disposed the scheduler between the double-check
            // above and Start. The task is faulted rather than lost, so the completion continuation
            // still fires and releases the semaphore and countdown — do not release them here.
            return false;
        }

        return true;
    }

    /// <summary>
    /// Decrements the number of running tasks and releases the concurrency semaphore so that more
    /// tasks may begin running.
    /// </summary>
    /// <param name="completedTask">The task which has completed.</param>
    private void SignalTaskComplete(Task completedTask)
    {
        if (completedTask.Exception is not null)
        {
            // Observing the fault here keeps it off the UnobservedTaskException path; a failure
            // can reach this point only by escaping the job run shell's own error handling.
            logger.LogError(completedTask.Exception, "A task handed to the thread pool faulted.");
        }

        concurrencySemaphore.Release();
        runningTasksCountdown.Signal();
    }

    /// <summary>
    /// Stops processing new tasks and optionally waits for currently running tasks to finish.
    /// </summary>
    /// <param name="waitForJobsToComplete"><see langword="true"/> to wait for currently executing tasks to finish; otherwise, <see langword="false"/>.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <remarks>
    /// The wait for running jobs is still a blocking one. Shutdown happens once, off the scheduling
    /// loop, so it is not the hot path that <see cref="WaitForAvailableThreads" /> and
    /// <see cref="TryRun" /> are.
    /// </remarks>
    public ValueTask Shutdown(bool waitForJobsToComplete = true, CancellationToken cancellationToken = default)
    {
        // A pool that was never initialized has no countdown, no semaphore and nothing running.
        if (!isInitialized)
        {
            return default;
        }

        logger.LogDebug("Shutting down threadpool...");

        // Cancel using our shutdown token
        shutdownCancellation.Cancel();

        // If waitForJobsToComplete is true, wait for running tasks to complete
        if (waitForJobsToComplete)
        {
            lock (runningTasksLock)
            {
                // Cancellation has been signaled, so no new tasks will begin once
                // shutdown has acquired this lock. CurrentCount includes the +1 guard
                // that keeps the event from starting in "signaled" state.
                logger.LogDebug("Waiting for {RunningTaskCount} running tasks to complete.", runningTasksCountdown.CurrentCount - 1);
            }

            // Signal the initial count that we used to make sure the CountDownEvent didn't start
            // in "signaled" state. One-shot so that concurrent or repeated shutdowns cannot
            // double-signal, which would end the wait one running task early.
            if (Interlocked.Exchange(ref shutdownInitialSignalDone, 1) == 0)
            {
                runningTasksCountdown.Signal();
            }

            // Wait for pending tasks to complete. Deliberately not cancellable: the caller is
            // QuartzScheduler.Shutdown, and abandoning this wait would skip the job store shutdown,
            // plugin shutdown and listener notification that follow it, leaving the scheduler wedged.
            runningTasksCountdown.Wait(CancellationToken.None);

            logger.LogDebug("No executing jobs remaining, all threads stopped.");
        }

        // Dispose the scheduler to release its resources (e.g. QueuedTaskScheduler threads)
        (scheduler as IDisposable)?.Dispose();

        logger.LogDebug("Shutdown of threadpool complete.");

        return default;
    }
}
