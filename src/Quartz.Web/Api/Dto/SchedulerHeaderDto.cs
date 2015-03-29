namespace Quartz.Web.Api.Dto
{
    public class SchedulerHeaderDto
    {
        public SchedulerHeaderDto(IScheduler scheduler)
        {
            Name = scheduler.SchedulerName;
            SchedulerInstanceId = scheduler.SchedulerInstanceId;
            Status = TranslateStatus(scheduler);
        }

        public string Name { get; private set; }
        public string SchedulerInstanceId { get; private set; }
        public SchedulerStatus Status { get; private set; }

        internal static SchedulerStatus TranslateStatus(IScheduler scheduler)
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
}