using System;
using System.Collections.Generic;

using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.RavenDB
{
    internal class Trigger : IHasGroup
    {
        private Trigger()
        {
        }

        public Trigger(
            IOperableTrigger newTrigger,
            string schedulerInstanceName) : this()
        {
            newTrigger.Key.Validate();

            Id = newTrigger.Key.DocumentId(schedulerInstanceName);
            Name = newTrigger.Key.Name;
            Group = newTrigger.Key.Group;

            Scheduler = schedulerInstanceName;

            UpdateWith(newTrigger);
        }

        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Group { get; private set; }

        public string JobId { get; private set; }

        public string Scheduler { get; private set; }

        public InternalTriggerState State { get; internal set; }
        public string Description { get; private set; }
        public string CalendarName { get; private set; }
        public IDictionary<string, object> JobDataMap { get; private set; }
        public string FireInstanceId { get; set; }
        public int MisfireInstruction { get; private set; }
        public DateTimeOffset? FinalFireTimeUtc { get; private set; }
        public DateTimeOffset? EndTimeUtc { get; private set; }
        public DateTimeOffset StartTimeUtc { get; private set; }

        public DateTimeOffset? NextFireTimeUtc { get; set; }

        public DateTimeOffset? PreviousFireTimeUtc { get; set; }

        public int Priority { get; set; }
        public bool HasMillisecondPrecision { get; set; }

        public CronOptions Cron { get; private set; }
        public SimpleOptions Simp { get; private set; }
        public CalendarOptions Cal { get; private set; }
        public DailyTimeOptions Day { get; private set; }

        internal void UpdateWith(IOperableTrigger trigger)
        {
            trigger.JobKey.Validate();
            JobId = trigger.JobKey.DocumentId(Scheduler);

            State = InternalTriggerState.Waiting;
            Description = trigger.Description;
            CalendarName = trigger.CalendarName;
            JobDataMap = trigger.JobDataMap.WrappedMap;
            FinalFireTimeUtc = trigger.FinalFireTimeUtc;
            MisfireInstruction = trigger.MisfireInstruction;
            Priority = trigger.Priority;
            HasMillisecondPrecision = trigger.HasMillisecondPrecision;
            FireInstanceId = trigger.FireInstanceId;
            EndTimeUtc = trigger.EndTimeUtc;
            StartTimeUtc = trigger.StartTimeUtc;
            NextFireTimeUtc = trigger.GetNextFireTimeUtc();
            PreviousFireTimeUtc = trigger.GetPreviousFireTimeUtc();

            // Init trigger specific properties according to type of newTrigger.
            // If an option doesn't apply to the type of trigger it will stay null by default.

            if (trigger is CronTriggerImpl cronTrigger)
            {
                Cron = new CronOptions {CronExpression = cronTrigger.CronExpressionString, TimeZoneId = cronTrigger.TimeZone.Id};
            }
            else if (trigger is SimpleTriggerImpl simpleTrigger)
            {
                Simp = new SimpleOptions {RepeatCount = simpleTrigger.RepeatCount, RepeatInterval = simpleTrigger.RepeatInterval};
            }
            else if (trigger is CalendarIntervalTriggerImpl calendarIntervalTrigger)
            {
                Cal = new CalendarOptions
                {
                    RepeatIntervalUnit = calendarIntervalTrigger.RepeatIntervalUnit,
                    RepeatInterval = calendarIntervalTrigger.RepeatInterval,
                    TimesTriggered = calendarIntervalTrigger.TimesTriggered,
                    TimeZoneId = calendarIntervalTrigger.TimeZone.Id,
                    PreserveHourOfDayAcrossDaylightSavings = calendarIntervalTrigger.PreserveHourOfDayAcrossDaylightSavings,
                    SkipDayIfHourDoesNotExist = calendarIntervalTrigger.SkipDayIfHourDoesNotExist
                };
            }
            else if (trigger is DailyTimeIntervalTriggerImpl dailyTimeIntervalTrigger)
            {
                Day = new DailyTimeOptions
                {
                    RepeatCount = dailyTimeIntervalTrigger.RepeatCount,
                    RepeatIntervalUnit = dailyTimeIntervalTrigger.RepeatIntervalUnit,
                    RepeatInterval = dailyTimeIntervalTrigger.RepeatInterval,
                    StartTimeOfDay = dailyTimeIntervalTrigger.StartTimeOfDay,
                    EndTimeOfDay = dailyTimeIntervalTrigger.EndTimeOfDay,
                    DaysOfWeek = new HashSet<DayOfWeek>(dailyTimeIntervalTrigger.DaysOfWeek),
                    TimesTriggered = dailyTimeIntervalTrigger.TimesTriggered,
                    TimeZoneId = dailyTimeIntervalTrigger.TimeZone.Id
                };
            }
            else
            {
                throw new ArgumentException("unsupported trigger type");
            }
        }

        public class CronOptions
        {
            public string CronExpression { get; set; }
            public string TimeZoneId { get; set; }
        }

        public class SimpleOptions
        {
            public int RepeatCount { get; set; }
            public TimeSpan RepeatInterval { get; set; }
        }

        public class CalendarOptions
        {
            public IntervalUnit RepeatIntervalUnit { get; set; }
            public int RepeatInterval { get; set; }
            public int TimesTriggered { get; set; }
            public string TimeZoneId { get; set; }
            public bool PreserveHourOfDayAcrossDaylightSavings { get; set; }
            public bool SkipDayIfHourDoesNotExist { get; set; }
        }

        public class DailyTimeOptions
        {
            public int RepeatCount { get; set; }
            public IntervalUnit RepeatIntervalUnit { get; set; }
            public int RepeatInterval { get; set; }
            public TimeOfDay StartTimeOfDay { get; set; }
            public TimeOfDay EndTimeOfDay { get; set; }
            public HashSet<DayOfWeek> DaysOfWeek { get; set; }
            public int TimesTriggered { get; set; }
            public string TimeZoneId { get; set; }
        }

        public IOperableTrigger Deserialize()
        {
            var triggerBuilder = TriggerBuilder.Create()
                .WithIdentity(Name, Group)
                .WithDescription(Description)
                .ModifiedByCalendar(CalendarName)
                .WithPriority(Priority)
                .StartAt(StartTimeUtc)
                .EndAt(EndTimeUtc)
                .ForJob(JobId.JobKeyFromDocumentId())
                .UsingJobData(new JobDataMap(JobDataMap));

            if (Cron != null)
            {
                triggerBuilder = triggerBuilder.WithCronSchedule(Cron.CronExpression, builder =>
                {
                    builder
                        .InTimeZone(TimeZoneUtil.FindTimeZoneById(Cron.TimeZoneId));
                });
            }
            else if (Simp != null)
            {
                triggerBuilder = triggerBuilder.WithSimpleSchedule(builder =>
                {
                    builder
                        .WithInterval(Simp.RepeatInterval)
                        .WithRepeatCount(Simp.RepeatCount);
                });
            }
            else if (Cal != null)
            {
                triggerBuilder = triggerBuilder.WithCalendarIntervalSchedule(builder =>
                {
                    builder
                        .WithInterval(Cal.RepeatInterval, Cal.RepeatIntervalUnit)
                        .InTimeZone(TimeZoneUtil.FindTimeZoneById(Cal.TimeZoneId))
                        .PreserveHourOfDayAcrossDaylightSavings(Cal.PreserveHourOfDayAcrossDaylightSavings)
                        .SkipDayIfHourDoesNotExist(Cal.SkipDayIfHourDoesNotExist);
                });
            }
            else if (Day != null)
            {
                triggerBuilder = triggerBuilder.WithDailyTimeIntervalSchedule(builder =>
                {
                    builder
                        .WithRepeatCount(Day.RepeatCount)
                        .WithInterval(Day.RepeatInterval, Day.RepeatIntervalUnit)
                        .InTimeZone(TimeZoneUtil.FindTimeZoneById(Day.TimeZoneId))
                        .EndingDailyAt(Day.EndTimeOfDay)
                        .StartingDailyAt(Day.StartTimeOfDay)
                        .OnDaysOfTheWeek(Day.DaysOfWeek);
                });
            }

            var trigger = triggerBuilder.Build();

            var returnTrigger = (IOperableTrigger) trigger;
            returnTrigger.SetNextFireTimeUtc(NextFireTimeUtc);
            returnTrigger.SetPreviousFireTimeUtc(PreviousFireTimeUtc);
            returnTrigger.FireInstanceId = FireInstanceId;

            return returnTrigger;
        }

        public void UpdateFireTimes(ITrigger trig)
        {
            NextFireTimeUtc = trig.GetNextFireTimeUtc();
            PreviousFireTimeUtc = trig.GetPreviousFireTimeUtc();
        }
    }
}