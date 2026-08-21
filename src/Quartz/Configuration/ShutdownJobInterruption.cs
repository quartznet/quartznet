namespace Quartz;

/// <summary>
/// When a shutting-down scheduler signals cancellation to the jobs still executing.
/// </summary>
/// <remarks>
/// <para>
/// A shutdown either waits for running jobs to finish or it does not, and interrupting them is a
/// reasonable thing to want in either case — or in only one of them. This says which, in one setting.
/// </para>
/// <para>
/// Only a job whose execution context implements <c>IInterruptableJobExecutionContext</c> is signalled;
/// a job that ignores its cancellation token runs to completion whatever this says.
/// </para>
/// </remarks>
public enum ShutdownJobInterruption
{
    /// <summary>
    /// Running jobs are never interrupted. The default.
    /// </summary>
    Never = 0,

    /// <summary>
    /// Running jobs are interrupted only on a shutdown that does not wait for them.
    /// </summary>
    /// <remarks>
    /// The shutdown is not going to wait, so the alternative is leaving the jobs running with nothing
    /// left to report to.
    /// </remarks>
    WhenNotWaitingForJobs,

    /// <summary>
    /// Running jobs are interrupted only on a shutdown that waits for them to finish.
    /// </summary>
    /// <remarks>
    /// The wait still happens: the jobs are asked to stop and then given as long as they need to,
    /// which is how a job that checks its cancellation token gets to unwind cleanly.
    /// </remarks>
    WhenWaitingForJobs,

    /// <summary>
    /// Running jobs are interrupted on every shutdown, whether it waits for them or not.
    /// </summary>
    Always
}
