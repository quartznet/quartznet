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
    /// Updates non-schedule properties of an existing trigger without rescheduling.
    /// Preserves the trigger's fire times, state, and misfire handling context.
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
        if (scheduler is StdScheduler std)
        {
            return std.UpdateTriggerDetails(triggerKey, update, cancellationToken);
        }

        throw new SchedulerException(
            $"UpdateTriggerDetails is not supported by scheduler implementation '{scheduler.GetType().Name}'. " +
            "This feature requires StdScheduler with a compatible job store.");
    }
}
