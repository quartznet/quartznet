namespace Quartz.HttpApiContract;

internal record JobDetailDto(
    string Name,
    string Group,
    string JobType,
    string? Description,
    bool Durable,
    bool RequestsRecovery,
    bool ConcurrentExecutionDisallowed,
    bool PersistJobDataAfterExecution,
    JobDataMap JobDataMap
)
{
    public (IJobDetail? JobDetail, string? ErrorReason) AsIJobDetail()
    {
        var jobType = Type.GetType(JobType, throwOnError: false);
        if (jobType == null)
        {
            return (null, "Unknown job type");
        }

        var jobDetail = JobBuilder.Create(jobType)
            .WithIdentity(Name, Group)
            .WithDescription(Description)
            .StoreDurably(Durable)
            .RequestRecovery(RequestsRecovery)
            .DisallowConcurrentExecution(ConcurrentExecutionDisallowed)
            .PersistJobDataAfterExecution(PersistJobDataAfterExecution)
            .UsingJobData(JobDataMap)
            .Build();

        return (jobDetail, null);
    }

    public static JobDetailDto Create(IJobDetail jobDetail)
    {
        return new JobDetailDto(
            Name: jobDetail.Key.Name,
            Group: jobDetail.Key.Group,
            JobType: jobDetail.JobType.FullName,
            Description: jobDetail.Description,
            Durable: jobDetail.Durable,
            RequestsRecovery: jobDetail.RequestsRecovery,
            ConcurrentExecutionDisallowed: jobDetail.ConcurrentExecutionDisallowed,
            PersistJobDataAfterExecution: jobDetail.PersistJobDataAfterExecution,
            JobDataMap: jobDetail.JobDataMap
        );
    }
}