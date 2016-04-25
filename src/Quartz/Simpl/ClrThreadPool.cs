using System;
using System.Threading;
using System.Threading.Tasks;
using Quartz.Logging;
using Quartz.Spi;

namespace Quartz.Simpl
{
    public class ClrThreadPool : IThreadPool
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof(ClrThreadPool));

        public bool RunInThread(Action runnable)
        {
            throw new NotSupportedException("This ThreadPool should not be used for running non-async jobs");
        }

        public bool RunInThread(Func<Task> runnable)
        {
            Task.Run(runnable);
            return true;
        }

        public int BlockForAvailableThreads()
        {
            int workerThreads;
            int completionPortThreads;
            ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);
            return workerThreads;
        }

        public void Initialize()
        {
            int workerThreads;
            int completionPortThreads;

            ThreadPool.GetMaxThreads(out workerThreads, out completionPortThreads);

            if (WorkerThreadCount != null)
            {
                workerThreads = WorkerThreadCount.Value;
            }
            if (CompletionPortThreadCount != null)
            {
                completionPortThreads = CompletionPortThreadCount.Value;
            }

            log.InfoFormat("CLR thread pool configured with {0} worker threads and {1} completion port threads", workerThreads, completionPortThreads);
            ThreadPool.SetMaxThreads(workerThreads, completionPortThreads);
        }

        public int? WorkerThreadCount { get; set; }

        public int? CompletionPortThreadCount { get; set; }

        /// <summary>
        /// Gets or sets the number of worker threads in the pool.
        /// Set  has no effect after <see cref="Initialize()" /> has been called.
        /// </summary>
        public int ThreadCount
        {
            get
            {
                int workerThreads;
                int completionPortThreads;

                ThreadPool.GetMaxThreads(out workerThreads, out completionPortThreads);
                return WorkerThreadCount.GetValueOrDefault(workerThreads);
            }
            set { WorkerThreadCount = value; }
        }

        /// <summary>
        /// Get or set the thread priority of worker threads in the pool.
        /// Set operation has no effect after <see cref="Initialize()" /> has been called.
        /// </summary>
        public ThreadPriority ThreadPriority { get; set; } = ThreadPriority.Normal;

        /// <summary>
        /// Gets or sets the thread name prefix.
        /// </summary>
        /// <value>The thread name prefix.</value>
        public string ThreadNamePrefix { get; set; }

        /// <summary> 
        /// Gets or sets the value of makeThreadsDaemons.
        /// </summary>
        public bool MakeThreadsDaemons { get; set; }

        public void Shutdown(bool waitForJobsToComplete = true)
        {
        }

        public int PoolSize
        {
            get
            {
                int workerThreads;
                int completionPortThreads;

                ThreadPool.GetMaxThreads(out workerThreads, out completionPortThreads);

                return WorkerThreadCount.GetValueOrDefault(workerThreads);
            }
        }

        public string InstanceId { get; set; }
        public string InstanceName { get; set; }
    }
}