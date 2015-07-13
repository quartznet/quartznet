using System;
using System.Collections.Generic;

using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Web.Api.Dto
{
    public class JobDetailDto
    {
        public JobDetailDto(IJobDetail jobDetail)
        {
            Durable = jobDetail.Durable;
            ConcurrentExecutionDisallowed = jobDetail.ConcurrentExecutionDisallowed;
            Description = jobDetail.Description;
            JobType = jobDetail.JobType.AssemblyQualifiedNameWithoutVersion();
            Name = jobDetail.Key.Name;
            Group = jobDetail.Key.Group;
            PersistJobDataAfterExecution = jobDetail.PersistJobDataAfterExecution;
            RequestsRecovery = jobDetail.RequestsRecovery;
        }

        public string Name { get; set; }
        public string Group { get; set; }
        public string JobType { get; set; }
        public string Description { get; set; }

        public bool Durable { get; set; }
        public bool RequestsRecovery { get; set; }
        public bool PersistJobDataAfterExecution { get; set; }
        public bool ConcurrentExecutionDisallowed { get; set; }
    }
    public class TriggerDetailDto
    {
        public TriggerDetailDto(ITrigger trigger, ICalendar calendar)
        {
            Description = trigger.Description;
            TriggerType = trigger.GetType().AssemblyQualifiedNameWithoutVersion();
            Name = trigger.Key.Name;
            Group = trigger.Key.Group;
            CalendarName = trigger.CalendarName;
            Priority = trigger.Priority;
            StartTimeUtc = trigger.StartTimeUtc;
            EndTimeUtc = trigger.EndTimeUtc;
            NextFireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, calendar, 10);
        }
        public string Name { get; set; }
        public string Group { get; set; }
        public string TriggerType { get; set; }
        public string Description { get; set; }

        public string CalendarName { get; set; }
        public int Priority { get; set; }
        public DateTimeOffset StartTimeUtc { get; set; }
        public DateTimeOffset? EndTimeUtc { get; set; }

        public IList<DateTimeOffset> NextFireTimes { get; set; }

    }
}