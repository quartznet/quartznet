using Quartz.Impl;
using Quartz.Spi;

namespace Quartz.HttpApiContract;

// When updating this, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.CurrentlyExecutingJobDto
internal record CurrentlyExecutingJobDto(
    JobDetailDto JobDetail,
    ITrigger Trigger,
    ICalendar? Calendar,
    bool Recovering,
    DateTimeOffset FireTime,
    DateTimeOffset? ScheduledFireTime,
    DateTimeOffset? PreviousFireTime,
    DateTimeOffset? NextFireTime
)
{
    public static CurrentlyExecutingJobDto Create(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CurrentlyExecutingJobDto(
            JobDetail: JobDetailDto.Create(context.JobDetail),
            Trigger: context.Trigger,
            Calendar: context.Calendar,
            Recovering: context.Recovering,
            FireTime: context.FireTimeUtc,
            ScheduledFireTime: context.ScheduledFireTimeUtc,
            PreviousFireTime: context.PreviousFireTimeUtc,
            NextFireTime: context.NextFireTimeUtc
        );
    }

    public (IJobExecutionContext? Context, string? ErrorReason) AsIJobExecutionContext(IScheduler scheduler)
    {
        var (jobDetail, errorReason) = JobDetail.AsIJobDetail();
        if (jobDetail is null)
        {
            return (null, errorReason);
        }

        var triggerFiredBundle = new TriggerFiredBundle(
            job: jobDetail,
            trigger: (IOperableTrigger) Trigger,
            cal: Calendar,
            jobIsRecovering: Recovering,
            fireTimeUtc: FireTime,
            scheduledFireTimeUtc: ScheduledFireTime,
            prevFireTimeUtc: PreviousFireTime,
            nextFireTimeUtc: NextFireTime
        );

        var result = new JobExecutionContextImpl(scheduler, triggerFiredBundle, job: null!);
        return (result, null);
    }
}