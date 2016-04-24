using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Spi;
using Quartz.Util;

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

        public virtual IReadOnlyList<IJobExecutionContext> ExecutingJobs => new List<IJobExecutionContext>(executingJobs.Values);

        private readonly ConcurrentDictionary<string, IJobExecutionContext> executingJobs = new ConcurrentDictionary<string, IJobExecutionContext>();

        private int numJobsFired;

        public virtual Task JobToBeExecuted(IJobExecutionContext context)
        {
            Interlocked.Increment(ref numJobsFired);
            executingJobs[((IOperableTrigger) context.Trigger).FireInstanceId] = context;
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException)
        {
            IJobExecutionContext temp;
            executingJobs.TryRemove(((IOperableTrigger) context.Trigger).FireInstanceId, out temp);
            return TaskUtil.CompletedTask;
        }

        public virtual Task JobExecutionVetoed(IJobExecutionContext context)
        {
            return TaskUtil.CompletedTask;
        }
    }
}