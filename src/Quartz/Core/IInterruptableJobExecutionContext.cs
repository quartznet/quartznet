namespace Quartz.Core;

/// <summary>
/// The scheduler's side of the interruption story: a job execution context whose
/// <see cref="IJobExecutionContext.CancellationToken" /> the scheduler can cancel.
/// </summary>
/// <remarks>
/// <para>
/// Interruption has exactly two names in the public API.
/// <see cref="IScheduler.Interrupt(JobKey, CancellationToken)" /> and
/// <see cref="IScheduler.InterruptFireInstance" /> request it;
/// <see cref="IJobExecutionContext.CancellationToken" />, which is the same token the job receives
/// as the <c>cancellationToken</c> parameter of <see cref="IJob.Execute" />, observes it. This
/// interface is the plumbing between the two and is deliberately internal: cancelling a context
/// directly would bypass the scheduler, work only for in-process contexts, and give one concept a
/// third name.
/// </para>
/// </remarks>
internal interface IInterruptableJobExecutionContext : IJobExecutionContext
{
    /// <summary>
    /// Cancels this execution's <see cref="IJobExecutionContext.CancellationToken" />. Whether the
    /// execution stops is up to the job, which has to observe the token.
    /// </summary>
    void Interrupt();
}
