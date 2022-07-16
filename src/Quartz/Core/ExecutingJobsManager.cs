using System.Collections.Concurrent;

using Quartz.Spi;

namespace Quartz.Core
{
    /// <summary>
    /// ExecutingJobsManager - Job Listener Class.
    /// </summary>
    internal sealed class ExecutingJobsManager : IJobListener
    {
        private ConcurrentDictionary<string, IJobExecutionContext> executingJobs = new ConcurrentDictionary<string, IJobExecutionContext>();
        private int numJobsFired;

        /// <summary>
        /// Initializes a new <see cref="ExecutingJobsManager"/> instance.
        /// </summary>
        public ExecutingJobsManager()
        {
            Name = GetType().ToString();
        }

        /// <summary>
        /// Get the name of the <see cref="IJobListener" />.
        /// </summary>
        /// <value>
        /// The name of the <see cref="IJobListener" />.
        /// </value>
        public string Name { get; }

        /// <summary>
        /// Gets the number of jobs that are currently executing.
        /// </summary>
        /// <value>
        /// The number of jobs that are currently executing.
        /// </value>
        public int NumJobsCurrentlyExecuting => executingJobs.Count;

        /// <summary>
        /// Gets the number of jobs executed.
        /// </summary>
        /// <value>
        /// The number of jobs executed.
        /// </value>
        public int NumJobsFired => numJobsFired;

        /// <summary>
        /// Gets the jobs that are currently executing.
        /// </summary>
        /// <value>
        /// The jobs that are currently executing.
        /// </value>
        public IReadOnlyCollection<IJobExecutionContext> ExecutingJobs => new List<IJobExecutionContext>(executingJobs.Values);

        public Task JobToBeExecuted(
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref numJobsFired);
            executingJobs[((IOperableTrigger) context.Trigger).FireInstanceId] = context;
            return Task.CompletedTask;
        }

        public Task JobWasExecuted(IJobExecutionContext context,
            JobExecutionException? jobException,
            CancellationToken cancellationToken = default)
        {
            executingJobs.TryRemove(((IOperableTrigger) context.Trigger).FireInstanceId, out _);
            return Task.CompletedTask;
        }

        public Task JobExecutionVetoed(
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}