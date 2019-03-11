using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Spi;

namespace Quartz.Core
{
    /// <summary>
    /// ExecutingJobsManager - Job Listener Class.
    /// </summary>
    internal class ExecutingJobsManager : IJobListener
    {
        public virtual string Name => GetType().FullName;

        public virtual int NumJobsCurrentlyExecuting => executingJobs.Count;

        public virtual int NumJobsFired => numJobsFired;

        public virtual IReadOnlyCollection<IJobExecutionContext> ExecutingJobs => new List<IJobExecutionContext>(executingJobs.Values);

        private readonly ConcurrentDictionary<string, IJobExecutionContext> executingJobs = new ConcurrentDictionary<string, IJobExecutionContext>();

        private int numJobsFired;

        public virtual Task JobToBeExecuted(
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref numJobsFired);
            executingJobs[((IOperableTrigger) context.Trigger).FireInstanceId] = context;
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobWasExecuted(
            IJobExecutionContext context,
            JobExecutionException jobException,
            CancellationToken cancellationToken = default)
        {
            executingJobs.TryRemove(((IOperableTrigger) context.Trigger).FireInstanceId, out _);
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobExecutionVetoed(
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }
    }
}