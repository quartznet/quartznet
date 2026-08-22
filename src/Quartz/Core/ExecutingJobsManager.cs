using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Quartz.Extensibility;

namespace Quartz.Core;

/// <summary>
/// ExecutingJobsManager - Job Listener Class.
/// </summary>
internal sealed class ExecutingJobsManager : IJobListener
{
    private readonly ConcurrentDictionary<string, IJobExecutionContext> executingJobs = new ConcurrentDictionary<string, IJobExecutionContext>();
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