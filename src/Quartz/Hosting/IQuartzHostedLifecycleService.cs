#if NET8_0_OR_GREATER
namespace Quartz;

/// <summary>
/// Defines methods that are run before or after
/// <see cref="QuartzHostedService.StartAsync(CancellationToken)"/> and
/// <see cref="QuartzHostedService.StopAsync(CancellationToken)"/>.
/// </summary>
public interface IQuartzHostedLifecycleService
{
    /// <summary>
    /// The method that is called before start Quartz hosted service.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    public Task StartingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The method that is called after start Quartz hosted service.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    public Task StartedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The method that is called before stop Quartz hosted service.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the stop process has been aborted.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    public Task StoppingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The method that is called after stop Quartz hosted service.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the stop process has been aborted.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    public Task StoppedAsync(CancellationToken cancellationToken);
}
#endif