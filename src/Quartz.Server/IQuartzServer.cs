namespace Quartz.Server;

/// <summary>
/// Service interface for core Quartz.NET server.
/// </summary>
public interface IQuartzServer
{
    /// <summary>
    /// Initializes the instance of <see cref="IQuartzServer"/>.
    /// Initialization will only be called once in server's lifetime.
    /// </summary>
    ValueTask Initialize();

    /// <summary>
    /// Starts this instance.
    /// </summary>
    ValueTask Start();

    /// <summary>
    /// Stops this instance.
    /// </summary>
#pragma warning disable CA1716
    ValueTask Stop();
#pragma warning restore CA1716

    /// <summary>
    /// Pauses all activity in scheduler.
    /// </summary>
    ValueTask Pause();

    /// <summary>
    /// Resumes all activity in server.
    /// </summary>
#pragma warning disable CA1716
    ValueTask Resume();
#pragma warning restore CA1716
}