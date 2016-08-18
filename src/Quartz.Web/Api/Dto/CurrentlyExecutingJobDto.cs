using System;

namespace Quartz.Web.Api.Dto
{
    public class CurrentlyExecutingJobDto
    {
        public CurrentlyExecutingJobDto(IJobExecutionContext context)
        {
            FireInstanceId = context.FireInstanceId;
            FireTime = context.FireTimeUtc;
            Trigger = new KeyDto(context.Trigger.Key);
            Job = new KeyDto(context.JobDetail.Key);
            JobRunTime = context.JobRunTime;
            RefireCount = context.RefireCount;

            Recovering = context.Recovering;
            if (context.Recovering)
            {
                RecoveringTrigger = new KeyDto(context.RecoveringTriggerKey);
            }
        }

        public string FireInstanceId { get; private set; }
        public DateTimeOffset? FireTime { get; private set; }
        public KeyDto Trigger { get; private set; }
        public KeyDto Job { get; private set; }
        public TimeSpan JobRunTime { get; private set; }
        public int RefireCount { get; private set; }
        public KeyDto RecoveringTrigger { get; private set; }
        public bool Recovering { get; private set; }
    }
}