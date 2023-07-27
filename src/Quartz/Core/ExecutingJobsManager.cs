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
    internal sealed class ExecutingJobsManager : IJobListener
    {
        public string Name => GetType()!.FullName!;

        public int NumJobsCurrentlyExecuting => executingJobs.Count;

        public int NumJobsFired => numJobsFired;

        public IReadOnlyCollection<IJobExecutionContext> ExecutingJobs => new List<IJobExecutionContext>(executingJobs.Values);

        private readonly ConcurrentDictionary<string, IJobExecutionContext> executingJobs = new ConcurrentDictionary<string, IJobExecutionContext>();

        private int numJobsFired;

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