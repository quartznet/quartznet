namespace Quartz;

/// <summary>
/// Strongly typed configuration for the scheduler's thread pool.
/// </summary>
/// <remarks>
/// Binds from the <c>ThreadPool</c> section of the Quartz configuration, and is the typed
/// replacement for the <c>quartz.threadPool.*</c> property keys.
/// </remarks>
public sealed class ThreadPoolOptions
{
    /// <summary>
    /// The default value for <see cref="MaxConcurrency"/>.
    /// </summary>
    public const int DefaultMaxConcurrency = 10;

    /// <summary>
    /// The maximum number of jobs that may execute in parallel.
    /// </summary>
    public int MaxConcurrency { get; set; } = DefaultMaxConcurrency;
}
