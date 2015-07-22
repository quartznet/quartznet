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

        public string SchedulerInstanceId { get; }
        public string Name { get; }
        public SchedulerStatus Status { get; }

        public SchedulerThreadPoolDto ThreadPool { get; }
        public SchedulerJobStoreDto JobStore { get; }
        public SchedulerStatisticsDto Statistics { get; }
    }
}