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
    /// Guards <see cref="runningTasks" /> and <see cref="runningTasksDrained" /> against the shutdown
    /// that closes the pool to new work. A lock of its own, because both are replaced by
    /// <see cref="Initialize" />.
    /// </summary>
    private readonly Lock runningTasksLock = new();

    /// <summary>
    /// The number of work items still running, plus one for as long as the pool accepts new work.
    /// </summary>
    /// <remarks>
    /// That extra count is what keeps the pool from looking drained before it has been closed to new
    /// work; shutdown drops it, and the last work item to finish afterwards takes the count to zero.
    /// </remarks>
    private int runningTasks;

    /// <summary>
    /// Completes when <see cref="runningTasks" /> reaches zero, which is when nothing the pool was given
    /// is still running.
    /// </summary>
    /// <remarks>
    /// Continuations run asynchronously, so that the work item which happens to finish last does not run
    /// the rest of a caller's shutdown on the pool's own thread.
    /// </remarks>
    private TaskCompletionSource runningTasksDrained = null!;

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
    private int guardCountDropped;

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

        // We start with the guard count, to make sure the pool doesn't start out looking drained.
        // Assigned under the lock that guards them, so that a caller already inside TryRun cannot count
        // itself into the pair this replaces.
        lock (runningTasksLock)
        {
            runningTasks = 1;
            runningTasksDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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

            // Record an extra running task. Interlocked because the completion continuation decrements
            // without taking this lock; the lock is here to order the increment against shutdown.
            Interlocked.Increment(ref runningTasks);
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

        if (Interlocked.Decrement(ref runningTasks) == 0)
        {
            // Zero is only reachable once shutdown has dropped the guard count, so this really is the
            // last work item the pool was given, and nothing more can be handed to it.
            runningTasksDrained.TrySetResult();
        }
    }

    /// <summary>
    /// Stops processing new tasks and optionally waits for currently running tasks to finish.
    /// </summary>
    /// <param name="waitForJobsToComplete"><see langword="true"/> to wait for currently executing tasks to finish; otherwise, <see langword="false"/>.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <remarks>
    /// The wait for running jobs is deliberately unbounded and not cancellable, which is what this
    /// member has always promised. <see cref="Drain" /> is the same wait with a deadline and an answer.
    /// </remarks>
    public async ValueTask Shutdown(bool waitForJobsToComplete = true, CancellationToken cancellationToken = default)
    {
        // A pool that was never initialized has no counter, no semaphore and nothing running.
        if (!isInitialized)
        {
            return;
        }

        logger.LogDebug("Shutting down threadpool...");

        Task drained = CloseToNewWork();

        // If waitForJobsToComplete is true, wait for running tasks to complete
        if (waitForJobsToComplete)
        {
            // The wait is awaited rather than blocked on, so it costs no thread, but it still cannot be
            // abandoned: a caller of this overload has no way to learn that it was, and everything it
            // tears down afterwards would run with jobs still writing to the store.
            await drained.ConfigureAwait(false);

            logger.LogDebug("No executing jobs remaining, all threads stopped.");
        }

        ReleaseResources();
    }

    /// <inheritdoc />
    public async ValueTask<bool> Drain(CancellationToken cancellationToken = default)
    {
        // A pool that was never initialized has nothing running, so it is drained by definition.
        if (!isInitialized)
        {
            return true;
        }

        logger.LogDebug("Draining threadpool...");

        Task drained = CloseToNewWork();
        bool drainedInTime = true;

        if (!drained.IsCompleted)
        {
            try
            {
                await drained.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The work carries on; only the waiting stops. Reported rather than thrown, so that the
                // caller's own shutdown continues instead of unwinding out of it - the answer is the
                // return value, and the caller is the one that knows what it means for it.
                drainedInTime = false;
            }
        }

        if (drainedInTime)
        {
            logger.LogDebug("No executing jobs remaining, all threads stopped.");
        }
        else
        {
            logger.LogDebug("Gave up waiting for the thread pool to drain; work is still running.");
        }

        ReleaseResources();
        return drainedInTime;
    }

    /// <summary>
    /// Closes the pool to new work and hands back the task that completes once the work it is already
    /// running has finished.
    /// </summary>
    private Task CloseToNewWork()
    {
        // Cancel using our shutdown token
        shutdownCancellation.Cancel();

        lock (runningTasksLock)
        {
            // Cancellation has been signalled, so no new work can begin once this lock is held: TryRun
            // re-checks the token under this same lock before counting itself in.
            //
            // Dropping the guard count is one-shot, so that concurrent or repeated shutdowns cannot drop
            // it twice, which would call the pool drained one running task early.
            int remaining = Interlocked.Exchange(ref guardCountDropped, 1) == 0
                ? Interlocked.Decrement(ref runningTasks)
                : Volatile.Read(ref runningTasks);

            logger.LogDebug("Thread pool closed to new work with {RunningTaskCount} running tasks remaining.", remaining);

            if (remaining == 0)
            {
                // Nothing was running, so no completion is coming to do this. Safe under the lock
                // because the continuations of this task are configured to run asynchronously.
                runningTasksDrained.TrySetResult();
            }

            return runningTasksDrained.Task;
        }
    }

    private void ReleaseResources()
    {
        // Dispose the scheduler to release its resources (e.g. QueuedTaskScheduler threads)
        (scheduler as IDisposable)?.Dispose();

        logger.LogDebug("Shutdown of threadpool complete.");
    }
}
