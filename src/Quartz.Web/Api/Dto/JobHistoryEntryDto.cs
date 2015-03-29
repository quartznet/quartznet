using System;

namespace Quartz.Web.Api.Dto
{
    public class JobHistoryEntryDto
    {
        public string JobName { get; set; }
        public string JobGroup { get; set; }
        public string TriggerName { get; set; }
        public string TriggerGroup { get; set; }
        public DateTimeOffset FiredTime { get; set; }
        public DateTimeOffset ScheduledTime { get; set; }
        public TimeSpan RunTime { get; set; }
        public bool Error { get; set; }
        public string ErrorMessage { get; set; }
    }
}