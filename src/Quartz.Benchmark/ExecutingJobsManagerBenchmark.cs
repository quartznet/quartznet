using BenchmarkDotNet.Attributes;
using Quartz.Core;
using Quartz.Spi;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Quartz.Benchmark
{
    [MemoryDiagnoser]
    public class ExecutingJobsManagerBenchmark
    {
        private ExecutingJobsManager _executionJobsManagerNew;
        private ExecutingJobsManagerLegacy _executionJobsManagerLegacy;

        public ExecutingJobsManagerBenchmark()
        {
            _executionJobsManagerNew = new ExecutingJobsManager();
            _executionJobsManagerLegacy = new ExecutingJobsManagerLegacy();
        }

        [Benchmark]
        public string Name_New()
        {
            return _executionJobsManagerNew.Name;
        }

        [Benchmark]
        public string Name_Old()
        {
            return _executionJobsManagerLegacy.Name;
        }

        /// <summary>
        /// ExecutingJobsManager - Job Listener Class.
        /// </summary>
        internal class ExecutingJobsManagerLegacy : IJobListener
        {
            public virtual string Name => GetType()!.FullName!;

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
                executingJobs[((IOperableTrigger)context.Trigger).FireInstanceId] = context;
                return Task.CompletedTask;
            }

            public virtual Task JobWasExecuted(IJobExecutionContext context,
                JobExecutionException? jobException,
                CancellationToken cancellationToken = default)
            {
                executingJobs.TryRemove(((IOperableTrigger)context.Trigger).FireInstanceId, out _);
                return Task.CompletedTask;
            }

            public virtual Task JobExecutionVetoed(
                IJobExecutionContext context,
                CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }
    }
}
