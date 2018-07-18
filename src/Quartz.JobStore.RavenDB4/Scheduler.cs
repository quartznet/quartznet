using System;
using System.Collections.Generic;

namespace Quartz.Impl.RavenDB
{
    internal class Scheduler
    {
        public Scheduler()
        {
            Calendars = new Dictionary<string, ICalendar>();
            PausedTriggerGroups = new HashSet<string>();
            LastCheckinTime = DateTimeOffset.MinValue;
            CheckinInterval = DateTimeOffset.MinValue;
        }

        public string InstanceName { get; set; }
        public DateTimeOffset LastCheckinTime { get; private set; }
        public DateTimeOffset CheckinInterval { get; private set; }
        public SchedulerState State { get; set; }
        public Dictionary<string, ICalendar> Calendars { get; private set; }
        public HashSet<string> PausedTriggerGroups { get; private set; }
    }
}