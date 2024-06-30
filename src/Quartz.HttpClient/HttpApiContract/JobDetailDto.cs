// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - Can be null when received from Web API
// ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract

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
) : IValidatable
{
    public IEnumerable<string> Validate()
    {
        if (Name is null)
        {
            yield return "Job detail is missing name";
        }

        if (Group is null)
        {
            yield return "Job detail is missing group";
        }

        if (JobType is null)
        {
            yield return "Job detail is missing job type";
        }
        else
        {
            var jobType = Type.GetType(JobType, throwOnError: false);
            if (jobType is null)
            {
                yield return "Job detail has unknown job type " + JobType;
            }
        }
    }

    public (IJobDetail? JobDetail, string? ErrorReason) AsIJobDetail()
    {
        var jobType = Type.GetType(JobType, throwOnError: false);
        if (jobType is null)
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
            .UsingJobData(JobDataMap ?? new JobDataMap())
            .Build();

        return (jobDetail, null);
    }

    public static JobDetailDto Create(IJobDetail jobDetail)
    {
        ArgumentNullException.ThrowIfNull(jobDetail);

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