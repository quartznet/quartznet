using System;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl;

namespace Quartz;

/// <summary>
/// Extension methods for <see cref="IScheduler"/> providing capabilities
/// that are not part of the base interface in 3.x.
/// </summary>
public static class SchedulerExtensions
{
    /// <summary>
    /// Updates trigger metadata and selected settings without rescheduling.
    /// Fire times and trigger state are preserved. Supported properties include
    /// Description, Priority, JobDataMap, CalendarName, and MisfireInstruction.
    /// </summary>
    /// <remarks>
    /// This operation is supported when the underlying scheduler is a <see cref="StdScheduler"/>
    /// (the standard implementation). If the scheduler implementation does not support this
    /// operation, a <see cref="SchedulerException"/> is thrown.
    /// </remarks>
    /// <param name="scheduler">The scheduler instance.</param>
    /// <param name="triggerKey">The key identifying the trigger to update.</param>
    /// <param name="update">The details to update.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns><see langword="true"/> if the trigger was found and updated, <see langword="false"/> if not found.</returns>
    /// <exception cref="SchedulerException">If the scheduler or its job store does not support this operation.</exception>
    public static Task<bool> UpdateTriggerDetails(
        this IScheduler scheduler,
        TriggerKey triggerKey,
        TriggerDetailsUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (scheduler is null)
        {
            throw new ArgumentNullException(nameof(scheduler));
        }

        if (scheduler is StdScheduler std)
        {
            return std.UpdateTriggerDetails(triggerKey, update, cancellationToken);
        }

        throw new SchedulerException(
            $"UpdateTriggerDetails is not supported by scheduler implementation '{scheduler.GetType().Name}'. " +
            "This feature requires StdScheduler with a compatible job store.");
    }

    /// <summary>
    /// Sets the execution group limits for this scheduler node. Execution groups
    /// allow per-node thread limits so that resource-intensive jobs do not saturate
    /// all available threads.
    /// </summary>
    /// <remarks>
    /// Limits take effect on the next trigger acquisition cycle. Pass <see langword="null"/>
    /// to clear all limits.
    /// </remarks>
    /// <param name="scheduler">The scheduler instance.</param>
    /// <param name="limits">The execution limits to apply, or <see langword="null"/> to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SchedulerException">If the scheduler implementation does not support this operation.</exception>
    public static Task SetExecutionLimits(
        this IScheduler scheduler,
        ExecutionLimits? limits,
        CancellationToken cancellationToken = default)
    {
        if (scheduler is null)
        {
            throw new ArgumentNullException(nameof(scheduler));
        }

        if (scheduler is StdScheduler std)
        {
            std.SetExecutionLimits(limits);
            return Task.CompletedTask;
        }

        throw new SchedulerException(
            $"SetExecutionLimits is not supported by scheduler implementation '{scheduler.GetType().Name}'. " +
            "This feature requires StdScheduler.");
    }

    /// <summary>
    /// Gets the currently configured execution group limits for this scheduler node,
    /// or <see langword="null"/> if none are configured.
    /// </summary>
    /// <param name="scheduler">The scheduler instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current execution limits, or <see langword="null"/>.</returns>
    /// <exception cref="SchedulerException">If the scheduler implementation does not support this operation.</exception>
    public static Task<ExecutionLimits?> GetExecutionLimits(
        this IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        if (scheduler is null)
        {
            throw new ArgumentNullException(nameof(scheduler));
        }

        if (scheduler is StdScheduler std)
        {
            return Task.FromResult(std.GetExecutionLimits());
        }

        throw new SchedulerException(
            $"GetExecutionLimits is not supported by scheduler implementation '{scheduler.GetType().Name}'. " +
            "This feature requires StdScheduler.");
    }
}
