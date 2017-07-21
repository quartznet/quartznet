using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;
using Quartz.Spi;

namespace Quartz.Simpl
{
    /// <summary>
    /// An IThreadPool implementation which schedules tasks using a TaskScheduler (provided by implementers)
    /// </summary>
    public abstract class TaskSchedulingThreadPool : IThreadPool
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof(TaskSchedulingThreadPool));

        // The token source used to cancel thread pool execution at shutdown
        // Note that cancellation is not propagated to the user-scheduled tasks currently executing,
        // only to the thread pool functions themselves (such as scheduling tasks).
        private readonly CancellationTokenSource shutdownCancellation = new CancellationTokenSource();

        // A list of running tasks (needed to optionally wait for executing tasks at shutdown)
        private readonly List<Task> runningTasks = new List<Task>();

        private readonly object taskListLock = new object();

        // The semaphore used to limit concurrency and integers representing maximim concurrent tasks
        private SemaphoreSlim concurrencySemaphore;

        private int maxConcurrency;
        protected const int DefaultMaxConcurrency = 10;

        private TaskScheduler scheduler;
        private bool isInitialized;

        /// <summary>
        /// The TaskScheduler used to schedule tasks queued by users
        /// </summary>
        public TaskScheduler Scheduler
        {
            get => scheduler;
            set
            {
                if (!isInitialized) scheduler = value;
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
        /// The maxmimum number of thread pool tasks which can be executing in parallel
        /// </summary>
        public int MaxConcurency
        {
            get => maxConcurrency;
            set
            {
                if (!isInitialized) maxConcurrency = value;
            }
        }

        /// <summary>
        /// The maxmimum number of thread pool tasks which can be executing in parallel
        /// </summary>
        /// <remarks>
        /// This alias for MaximumConcurrency is meant to make config files previously used with
        /// SimpleThreadPool or CLRThreadPool work more directly.
        /// </remarks>
        public int ThreadCount
        {
            get => MaxConcurency;
            set => MaxConcurency = value;
        }

        // ReSharper disable once UnusedMember.Global
        public string ThreadPriority
        {
            set => log.Warn("Thread priority is no longer supported for thread pool, ignoring");
        }

        /// <summary>
        /// The number of tasks that can run concurrently in this thread pool
        /// </summary>
        public virtual int PoolSize => MaxConcurency;

        public virtual string InstanceId { get; set; }

        public virtual string InstanceName { get; set; }

        public TaskSchedulingThreadPool() : this(DefaultMaxConcurrency)
        {
        }

        public TaskSchedulingThreadPool(int maxConcurrency)
        {
            MaxConcurency = maxConcurrency;
        }

        /// <summary>
        /// Initializes the tread pool for use
        /// </summary>
        /// <remarks>
        /// Note that after invoking this method, neither
        /// </remarks>
        public virtual void Initialize()
        {
            // Checking for null allows users to specify their own scheduler prior to initialization.
            // If this is undesirable, the scheduler should be set here unconditionally.
            if (Scheduler == null)
            {
                Scheduler = GetDefaultScheduler();
            }

            // Initialize the concurrency semaphore with the proper initial count
            concurrencySemaphore = new SemaphoreSlim(MaxConcurency);
            isInitialized = true;

            log.Debug($"TaskSchedulingThreadPool configured with max concurrency of {MaxConcurency} and TaskScheduler {Scheduler.GetType().Name}.");
        }

        /// <summary>
        /// Determines the number of threads that are currently available in
        /// the pool; blocks until at least one is avaialble
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
        /// Schedules a task to run (using the task scheduler) as soon as concurrency rules allow it
        /// </summary>
        /// <param name="runnable">The action to be executed</param>
        /// <returns>True if the task was successfully scheduled, false otherwise</returns>
        public bool RunInThread(Func<Task> runnable)
        {
            if (runnable == null || !isInitialized || shutdownCancellation.IsCancellationRequested) return false;

            // Acquire the semaphore (return false if shutdown occurs while waiting)
            try
            {
                concurrencySemaphore.Wait(shutdownCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            var task = new Task<Task>(runnable);

            // Unrap the task so that we can work with the underlying task
            var unwrappedTask = task.Unwrap();
            lock (taskListLock)
            {
                // Now that the taskListLock is held, shutdown can't proceed,
                // so double-check that no shutdown has started since the initial check.
                if (shutdownCancellation.IsCancellationRequested)
                {
                    concurrencySemaphore.Release();
                    return false;
                }

                // Record the underlying task as running
                runningTasks.Add(unwrappedTask);
            }
            // Register a callback to remove the task from the running list once it has completed
            unwrappedTask.ContinueWith(RemoveTaskFromRunningList);

            // Start the task using the task scheduler
            task.Start(Scheduler);

            return true;
        }

        /// <summary>
        /// Removes a task from the 'running' list (if it exists there) and releases
        /// the concurrency semaphore so that more tasks may begin running
        /// </summary>
        /// <param name="completedTask">The task which has completed</param>
        private void RemoveTaskFromRunningList(Task completedTask)
        {
            concurrencySemaphore.Release();
            lock (taskListLock)
            {
                if (completedTask != null && runningTasks.Contains(completedTask))
                {
                    runningTasks.Remove(completedTask);
                }
            }
        }

        /// <summary>
        /// Stops processing new tasks and optionally waits for currently running tasks to finish
        /// </summary>
        /// <param name="waitForJobsToComplete">True to wait for currently executing tasks to finish; false otherwise</param>
        public void Shutdown(bool waitForJobsToComplete = true)
        {
            log.Debug("Shutting down threadpool...");

            // Cancel using our shutdown token
            shutdownCancellation.Cancel();

            // If waitForJobsToComplete is true, wait for runningTasks
            if (waitForJobsToComplete)
            {
                log.DebugFormat($"Waiting for {runningTasks.Count} threads to complete.");

                Task[] tasksArray = new Task[0];
                lock (taskListLock)
                {
                    // Cancellation has been signaled, so no new tasks will begin once
                    // shutdown has acquired this lock
                    tasksArray = runningTasks.ToArray();
                }
                Task.WaitAll(tasksArray);
                log.Debug("No executing jobs remaining, all threads stopped.");
            }
            log.Debug("Shutdown of threadpool complete.");
        }
    }
}