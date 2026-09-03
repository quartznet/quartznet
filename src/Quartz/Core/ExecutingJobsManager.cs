using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Spi;

namespace Quartz.Core;

/// <summary>
/// The scheduler's own record of the firings it is running: what
/// <see cref="QuartzScheduler.CurrentlyExecutingJobs" /> lists and
/// <see cref="QuartzScheduler.NumJobsExecuted" /> counts.
/// </summary>
/// <remarks>
/// It is the scheduler's built-in job listener, and it is told about the same two moments a user
/// listener is — but directly, rather than through the loop that notifies them. A user listener that
/// throws abandons that loop, and bookkeeping carried along with it would leave a firing listed as
/// executing for as long as the process lived (#3502). <see cref="FiringStarted" /> and
/// <see cref="FiringEnded" /> are the members the scheduler calls, and they are synchronous because
/// there has never been anything in them to await.
/// </remarks>
internal sealed class ExecutingJobsManager : IJobListener
{
    public string Name => GetType()!.FullName!;

    public int NumJobsCurrentlyExecuting => executingJobs.Count;

    public int NumJobsFired => numJobsFired;

    public IReadOnlyCollection<IJobExecutionContext> ExecutingJobs => new List<IJobExecutionContext>(executingJobs.Values);

    private readonly ConcurrentDictionary<string, IJobExecutionContext> executingJobs = new ConcurrentDictionary<string, IJobExecutionContext>();

    private int numJobsFired;

    /// <summary>
    /// Records that a firing has begun. It is listed as executing, and counted as fired, until
    /// <see cref="FiringEnded" /> is called for it.
    /// </summary>
    public void FiringStarted(IJobExecutionContext context)
    {
        Interlocked.Increment(ref numJobsFired);
        executingJobs[((IOperableTrigger) context.Trigger).FireInstanceId] = context;
    }

    /// <summary>
    /// Records that a firing is over, whether the job ran or a listener stopped it on the way in.
    /// </summary>
    /// <remarks>
    /// The count of jobs fired is deliberately not taken back: it counts the firings this scheduler
    /// dispatched, which a firing a listener then abandoned still is.
    /// </remarks>
    public void FiringEnded(IJobExecutionContext context)
    {
        executingJobs.TryRemove(((IOperableTrigger) context.Trigger).FireInstanceId, out _);
    }

    public Task JobToBeExecuted(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        FiringStarted(context);
        return Task.CompletedTask;
    }

    public Task JobWasExecuted(IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        FiringEnded(context);
        return Task.CompletedTask;
    }

    public Task JobExecutionVetoed(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}