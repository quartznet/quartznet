using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;
using Quartz.Spi;

namespace Quartz.Simpl
{
    public abstract class TaskSchedulingThreadPool : IThreadPool
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof(SimpleThreadPool));
        private readonly CancellationTokenSource shutdownCancellation = new CancellationTokenSource();
        private readonly List<Task> runningTasks = new List<Task>();
        private readonly object taskListLock = new object();

        protected const int DefaultMaxConcurrency = 10;

        private SemaphoreSlim concurrencySemaphore;
        private bool isInitialized = false;
        private int maxConcurrency;
        private TaskScheduler scheduler;

        public TaskScheduler Scheduler
        {
            get { return scheduler; }
            set { if (!isInitialized) scheduler = value; }
        }

        protected abstract TaskScheduler GetDefaultScheduler();

        public int MaxConcurency
        {
            get { return maxConcurrency; }
            set { if (!isInitialized) maxConcurrency = value; }
        }

        public int ThreadCount {
            get { return MaxConcurency; }
            set { MaxConcurency = value; }
        }

        public int PoolSize => MaxConcurency;

        public string InstanceId { get; set; }

        public string InstanceName { get; set; }

        public TaskSchedulingThreadPool() : this(DefaultMaxConcurrency) { }

        public TaskSchedulingThreadPool(int maxConcurrency)
        {
            MaxConcurency = maxConcurrency;
        }

        public virtual void Initialize()
        {
            scheduler = GetDefaultScheduler();
            concurrencySemaphore = new SemaphoreSlim(MaxConcurency);
            isInitialized = true;
            log.Debug($"TaskSchedulingThreadPool configured with max concurrency of {MaxConcurency} and TaskScheduler {Scheduler.GetType().Name}.");
        }

        public int BlockForAvailableThreads()
        {
            if (isInitialized && !shutdownCancellation.IsCancellationRequested)
            {
                try
                {
                    concurrencySemaphore.Wait(shutdownCancellation.Token);
                    return 1 + concurrencySemaphore.Release();
                }
                catch (OperationCanceledException)
                {
                }
            }

            return 0;
        }

        public bool RunInThread(Action runnable)
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

            var task = new Task(runnable);
            lock (taskListLock)
            {
                runningTasks.Add(task);
            }
            task.ContinueWith(RemoveTaskFromRunningList);

            task.Start(Scheduler);
            return true;
        }

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
            var unwrappedTask = task.Unwrap();
            lock (taskListLock)
            {
                runningTasks.Add(unwrappedTask);
            }
            unwrappedTask.ContinueWith(RemoveTaskFromRunningList);
            
            task.Start(Scheduler);
            return true;
        }

        private void RemoveTaskFromRunningList(Task completedTask)
        {
            lock (taskListLock)
            {
                if (completedTask != null && runningTasks.Contains(completedTask))
                {
                    runningTasks.Remove(completedTask);
                }
                concurrencySemaphore.Release();
            }
        }

        public void Shutdown(bool waitForJobsToComplete = true)
        {
            log.Debug("Shutting down threadpool...");
            shutdownCancellation.Cancel();
            if (waitForJobsToComplete)
            {
                log.DebugFormat($"Waiting for {runningTasks.Count} threads to complete.");
                Task.WaitAll(runningTasks.ToArray());
                log.Debug("No executing jobs remaining, all threads stopped.");
            }
            log.Debug("Shutdown of threadpool complete.");
        }
    }
}
