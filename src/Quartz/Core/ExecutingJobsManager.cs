using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Quartz.Extensibility;

namespace Quartz.Core;

/// <summary>
/// The scheduler's own record of the firings it is running: what
/// <see cref="QuartzScheduler.GetCurrentlyExecutingJobs" /> lists and
/// <see cref="QuartzScheduler.NumberOfJobsExecutingHere" /> counts.
/// </summary>
/// <remarks>
/// It is told about the same two moments a user job listener is — but directly, rather than through
/// the loop that notifies them. A user listener that throws abandons that loop, and bookkeeping
/// carried along with it would leave a firing listed as executing for as long as the process lived
/// (#3502). <see cref="FiringStarted" /> and <see cref="FiringEnded" /> are the members the scheduler
/// calls, and they are synchronous because there has never been anything in them to await.
/// <para>
/// It used to implement <see cref="IJobListener" /> and be notified as one. Nothing has registered it
/// as a listener since the scheduler started calling it directly, so the interface, the name a
/// listener needs to be registered under, and its three notification bodies were all shape without a
/// caller.
/// </para>
/// </remarks>
internal sealed class ExecutingJobsManager
{
    private readonly ConcurrentDictionary<string, IJobExecutionContext> executingJobs = new ConcurrentDictionary<string, IJobExecutionContext>();
    private int numJobsFired;

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
    public List<IJobExecutionContext> GetExecutingJobs => [..executingJobs.Values];

    /// <summary>
    /// Finds one running execution by its fire instance id, which is the key this manager already uses.
    /// </summary>
    /// <remarks>
    /// The point of the member is that interrupting one execution does not have to materialize every
    /// other one to find it.
    /// </remarks>
    public bool TryGetExecutingJob(string fireInstanceId, [NotNullWhen(true)] out IJobExecutionContext? context)
    {
        return executingJobs.TryGetValue(fireInstanceId, out context);
    }

    /// <summary>
    /// Records that a firing has begun. It is listed as executing, and counted as fired, until
    /// <see cref="FiringEnded" /> is called for it.
    /// </summary>
    public void FiringStarted(IJobExecutionContext context)
    {
        Interlocked.Increment(ref numJobsFired);
        executingJobs[((IOperableTrigger) context.Trigger).FireInstanceId!] = context;
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
        executingJobs.TryRemove(((IOperableTrigger) context.Trigger).FireInstanceId!, out _);
    }
}