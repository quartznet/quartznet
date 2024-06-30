namespace Quartz.HttpApiContract;

internal record SchedulerHeaderDto(string Name, string SchedulerInstanceId, SchedulerStatus Status)
{
    public static SchedulerHeaderDto Create(IScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        return new SchedulerHeaderDto(scheduler.SchedulerName, scheduler.SchedulerInstanceId, TranslateStatus(scheduler));
    }

    public static SchedulerStatus TranslateStatus(IScheduler scheduler)
    {
        if (scheduler.IsShutdown)
        {
            return SchedulerStatus.Shutdown;
        }

        if (scheduler.InStandbyMode)
        {
            return SchedulerStatus.Standby;
        }

        if (scheduler.IsStarted)
        {
            return SchedulerStatus.Running;
        }

        return SchedulerStatus.Unknown;
    }
}