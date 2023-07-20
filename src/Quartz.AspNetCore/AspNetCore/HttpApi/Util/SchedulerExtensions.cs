namespace Quartz.AspNetCore.HttpApi.Util;

internal static class SchedulerExtensions
{
    public static async Task<ICalendar> GetCalendarOrThrow(this IScheduler scheduler, string calendarName, CancellationToken cancellationToken)
    {
        var calendar = await scheduler.GetCalendar(calendarName, cancellationToken).ConfigureAwait(false);
        if (calendar is null)
        {
            throw NotFoundException.ForCalendar(calendarName);
        }

        return calendar;
    }

    public static async Task<IJobDetail> GetJobDetailOrThrow(this IScheduler scheduler, string jobName, string jobGroup, CancellationToken cancellationToken)
    {
        var jobKey = new JobKey(jobName, jobGroup);

        var jobDetail = await scheduler.GetJobDetail(jobKey, cancellationToken).ConfigureAwait(false);
        if (jobDetail is null)
        {
            throw NotFoundException.ForJob(jobKey);
        }

        return jobDetail;
    }

    public static async Task<ITrigger> GetTriggerOrThrow(this IScheduler scheduler, string triggerName, string triggerGroup, CancellationToken cancellationToken)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);

        var trigger = await scheduler.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        if (trigger is null)
        {
            throw NotFoundException.ForTrigger(triggerKey);
        }

        return trigger;
    }
}