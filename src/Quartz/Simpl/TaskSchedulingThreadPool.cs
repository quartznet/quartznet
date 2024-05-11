using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Spi;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Simpl;

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
    protected internal const int DefaultMaxConcurrency = 10;

    private TaskScheduler scheduler = null!;
    private bool isInitialized;

    protected TaskSchedulingThreadPool() : this(DefaultMaxConcurrency)
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
        set
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
        set
        {
            if (!isInitialized)
            {
                maxConcurrency = value;
            }
        }
    }

    /// <summary>
    /// The maximum number of thread pool tasks which can be executing in parallel
    /// </summary>
    /// <remarks>
    /// This alias for MaximumConcurrency is meant to make config files previously used with
    /// SimpleThreadPool or CLRThreadPool work more directly.
    /// </remarks>
    public int ThreadCount
    {
        get => MaxConcurrency;
        set => MaxConcurrency = value;
    }

    /// <summary>
    /// The number of tasks that can run concurrently in this thread pool
    /// </summary>
    public virtual int PoolSize => MaxConcurrency;

    public virtual string InstanceId { get; set; } = null!;

    public virtual string InstanceName { get; set; } = null!;

    /// <summary>
    /// Initializes the thread pool for use
    /// </summary>
    /// <remarks>
    /// Note that after invoking this method, neither
    /// </remarks>
    public virtual void Initialize()
    {
        // Checking for null allows users to specify their own scheduler prior to initialization.
        // If this is undesirable, the scheduler should be set here unconditionally.
        if (Scheduler is null)
        {
            Scheduler = GetDefaultScheduler();
        }

        // Initialize the concurrency semaphore with the proper initial count
        concurrencySemaphore = new SemaphoreSlim(MaxConcurrency);

        // We start with an initial count of one to make sure it doesn't start in "signaled" state
        runningTasksCountdown = new CountdownEvent(1);

        // Reduce allocations by caching the delegate to mark a task as complete
        completeTask = SignalTaskComplete;

        // Thread pool is ready to go
        isInitialized = true;

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("TaskSchedulingThreadPool configured with max concurrency of {MaxConcurrency} and TaskScheduler {SchedulerName}.",
                MaxConcurrency, Scheduler.GetType().Name);
        }
    }

    /// <summary>
    /// Determines the number of threads that are currently available in
    /// the pool; blocks until at least one is available
    /// </summary>
    /// <returns>The number of currently available threads</returns>
    public int BlockForAvailableThreads()
    {
        if (isInitialized && !shutdownCancellation.IsCancellationRequested)
        {
            try
            {
                // There is a race condition here such that it's possible the method could return
                // 1 (or more) but no threads would be available a short time later when the scheduler
                // calls RunInThread. This could be avoided by 'reserving' threads for callers of
                // BlockForAvailableThreads, but that would complicate this code and nothing should
                // break functionally if threads are used for other tasks in between BlockForAvailableThreads
                // being called and RunInThread being called.
                //
                // The window of opportunity for such a race should be very small (unless the scheduler takes
                // a very long time to call RunInThread).
                //
                // In the worst case, RunInThread will just wait
                // for the next thread and clustered scenarios may experience some imbalanced loads.
                concurrencySemaphore.Wait(shutdownCancellation.Token);
                return 1 + concurrencySemaphore.Release();
            }
            catch (OperationCanceledException)
            {
            }
        }

        return 0;
    }

    /// <summary>
    /// Schedules a task to run (using the task scheduler) as soon as concurrency rules allow it.
    /// </summary>
    /// <param name="runnable">The action to be executed</param>
    /// <returns>
    /// <see langword="true"/> if the task was successfully scheduled; otherwise, <see langword="false"/>.
    /// </returns>
    public bool RunInThread(Func<Task> runnable)
    {
        if (runnable is null || !isInitialized || shutdownCancellation.IsCancellationRequested) return false;

        // Acquire the semaphore (return false if shutdown occurs while waiting)
        try
        {
            concurrencySemaphore.Wait(shutdownCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        // Wrap the runnable in a Task to start it asynchronously
        var task = new Task<Task>(runnable);

        // Unrap the task so that we can work with the underlying task
        var unwrappedTask = task.Unwrap();

        lock (runningTasksCountdown)
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
        unwrappedTask.ContinueWith(completeTask);
#pragma warning restore MA0134

        // Start the task using the task scheduler
        task.Start(Scheduler);

        return true;
    }

    /// <summary>
    /// Decrements the number of running tasks and releases the concurrency semaphore so that more
    /// tasks may begin running.
    /// </summary>
    /// <param name="completedTask">The task which has completed.</param>
    private void SignalTaskComplete(Task completedTask)
    {
        concurrencySemaphore.Release();
        runningTasksCountdown.Signal();
    }

    /// <summary>
    /// Stops processing new tasks and optionally waits for currently running tasks to finish.
    /// </summary>
    /// <param name="waitForJobsToComplete"><see langword="true"/> to wait for currently executing tasks to finish; otherwise, <see langword="false"/>.</param>
    public void Shutdown(bool waitForJobsToComplete = true)
    {
        logger.LogDebug("Shutting down threadpool...");

        // Cancel using our shutdown token
        shutdownCancellation.Cancel();

        // If waitForJobsToComplete is true, wait for running tasks to complete
        if (waitForJobsToComplete)
        {
            lock (runningTasksCountdown)
            {
                // Cancellation has been signaled, so no new tasks will begin once
                // shutdown has acquired this lock
                logger.LogDebug("Waiting for {ThreadCount} threads to complete.", runningTasksCountdown.CurrentCount.ToString());
            }

            // Signal the initial count that we used to make sure the CountDownEvent didn't start
            // in "signaled" state
            runningTasksCountdown.Signal();

            // Wait for pending tasks to complete
            runningTasksCountdown.Wait();

            logger.LogDebug("No executing jobs remaining, all threads stopped.");
        }

        logger.LogDebug("Shutdown of threadpool complete.");
    }
}