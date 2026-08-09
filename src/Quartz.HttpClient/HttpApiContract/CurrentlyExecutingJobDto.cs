#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using Quartz.Impl;
using Quartz.Extensibility;

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
            calendar: Calendar,
            jobIsRecovering: Recovering,
            fireTimeUtc: FireTime,
            scheduledFireTimeUtc: ScheduledFireTime,
            previousFireTimeUtc: PreviousFireTime,
            nextFireTimeUtc: NextFireTime
        );

        var result = new JobExecutionContextImpl(scheduler, triggerFiredBundle, job: null!);
        return (result, null);
    }
}