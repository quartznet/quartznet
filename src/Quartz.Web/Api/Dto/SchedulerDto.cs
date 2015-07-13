namespace Quartz.Web.Api.Dto
{
    public class SchedulerDto
    {
        public SchedulerDto(IScheduler scheduler, SchedulerMetaData metaData)
        {
            Name = scheduler.SchedulerName;
            SchedulerInstanceId = scheduler.SchedulerInstanceId;
            Status = SchedulerHeaderDto.TranslateStatus(scheduler);

            ThreadPool = new SchedulerThreadPoolDto(metaData);
            JobStore = new SchedulerJobStoreDto(metaData);
            Statistics = new SchedulerStatisticsDto(metaData);
        }

        public string SchedulerInstanceId { get; private set; }
        public string Name { get; private set; }
        public SchedulerStatus Status { get; private set; }

        public SchedulerThreadPoolDto ThreadPool { get; private set; }
        public SchedulerJobStoreDto JobStore { get; private set; }
        public SchedulerStatisticsDto Statistics { get; private set; }
    }
}