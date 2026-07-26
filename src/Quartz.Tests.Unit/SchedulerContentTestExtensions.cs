#nullable enable

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;

namespace Quartz.Tests.Unit;

/// <summary>
/// Reads the jobs and triggers registered for a scheduler.
/// </summary>
/// <remarks>
/// They are per-scheduler service registrations rather than part of <see cref="QuartzOptions"/>, keyed by
/// the scheduler's name, so resolving them is what proves a named scheduler got its own content.
/// </remarks>
internal static class SchedulerContentTestExtensions
{
    public static IReadOnlyList<IJobDetail> ScheduledJobs(this IServiceProvider provider, string? schedulerName = null)
    {
        return Content(provider, schedulerName).SelectMany(x => x.Jobs).ToList();
    }

    public static IReadOnlyList<ITrigger> ScheduledTriggers(this IServiceProvider provider, string? schedulerName = null)
    {
        return Content(provider, schedulerName).SelectMany(x => x.Triggers).ToList();
    }

    private static ISchedulerContent[] Content(IServiceProvider provider, string? schedulerName)
    {
        return string.IsNullOrEmpty(schedulerName)
            ? provider.GetServices<ISchedulerContent>().ToArray()
            : provider.GetKeyedServices<ISchedulerContent>(schedulerName).ToArray();
    }
}
