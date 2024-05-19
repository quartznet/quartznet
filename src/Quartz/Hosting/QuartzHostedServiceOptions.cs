namespace Quartz;

#if NET8_0_OR_GREATER
/// <summary>
/// 
/// </summary>
public delegate Task QuartzHostedServiceHandler(IServiceProvider provider, CancellationToken cancellationToken);
#endif

public class QuartzHostedServiceOptions
{
    /// <summary>
    /// If <see langword="true" /> the scheduler will not allow shutdown process
    /// to return until all currently executing jobs have completed.
    /// </summary>
    public bool WaitForJobsToComplete { get; set; }

    /// <summary>
    /// <para>
    /// If not <see langword="null" /> the scheduler will start after specified delay.
    /// </para>
    /// <para>
    /// If <see cref="AwaitApplicationStarted"/> is true, the delay starts when application startup completes.
    /// </para>
    /// </summary>
    public TimeSpan? StartDelay { get; set; }

    /// <summary>
    /// If true (default), jobs will not be started until application startup completes.
    /// This avoids the running of jobs <em>during</em> application startup.
    /// </summary>
    public bool AwaitApplicationStarted { get; set; } = true;

#if NET8_0_OR_GREATER
    /// <summary>
    /// 
    /// </summary>
    public QuartzHostedServiceHandler? HostedServiceStartingHandler { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public QuartzHostedServiceHandler? HostedServiceStartedHandler { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public QuartzHostedServiceHandler? HostedServiceStoppingHandler { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public QuartzHostedServiceHandler? HostedServiceStoppedHandler { get; set; }
#endif
}