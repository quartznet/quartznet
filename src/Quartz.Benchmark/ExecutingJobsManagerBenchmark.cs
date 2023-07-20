using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;
using Quartz.Core;
using Quartz.Spi;

namespace Quartz.Benchmark;

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
    internal sealed class ExecutingJobsManagerLegacy : IJobListener
    {
        public string Name => GetType()!.FullName!;

        public int NumJobsCurrentlyExecuting => executingJobs.Count;

        public int NumJobsFired => numJobsFired;

        public IReadOnlyCollection<IJobExecutionContext> ExecutingJobs => new List<IJobExecutionContext>(executingJobs.Values);

        private readonly ConcurrentDictionary<string, IJobExecutionContext> executingJobs = new ConcurrentDictionary<string, IJobExecutionContext>();

        private int numJobsFired;

        public ValueTask JobToBeExecuted(
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref numJobsFired);
            executingJobs[((IOperableTrigger) context.Trigger).FireInstanceId] = context;
            return default;
        }

        public ValueTask JobWasExecuted(IJobExecutionContext context,
            JobExecutionException? jobException,
            CancellationToken cancellationToken = default)
        {
            executingJobs.TryRemove(((IOperableTrigger) context.Trigger).FireInstanceId, out _);
            return default;
        }

        public ValueTask JobExecutionVetoed(
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}