namespace Quartz.Web.Api.Dto
{
    public class SchedulerStatisticsDto
    {
        public SchedulerStatisticsDto(SchedulerMetaData metaData)
        {
            NumberOfJobsExecuted = metaData.NumberOfJobsExecuted;
        }

        public int NumberOfJobsExecuted { get; private set; }
    }
}