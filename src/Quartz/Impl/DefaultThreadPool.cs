using Microsoft.Extensions.Logging;

namespace Quartz.Impl;

/// <summary>
/// An implementation of the TaskSchedulerThreadPool using the default task scheduler
/// </summary>
public sealed class DefaultThreadPool : TaskSchedulingThreadPool
{
    /// <inheritdoc cref="TaskSchedulingThreadPool(ILogger{TaskSchedulingThreadPool})" />
    /// <remarks>
    /// Declared rather than left implicit so that the container has a constructor to fill the logger
    /// into: this is the pool <c>UseDefaultThreadPool</c> registers, and it is built by
    /// <see cref="Microsoft.Extensions.DependencyInjection.ActivatorUtilities" />.
    /// </remarks>
    public DefaultThreadPool(ILogger<TaskSchedulingThreadPool>? logger = null) : base(logger)
    {
    }

    /// <summary>
    /// Returns TaskScheduler.Default
    /// </summary>
    /// <returns>TaskScheduler.Default</returns>
    protected override TaskScheduler GetDefaultScheduler()
    {
        return TaskScheduler.Default;
    }
}
