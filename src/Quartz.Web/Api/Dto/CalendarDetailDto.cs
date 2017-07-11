using System;
using System.Collections.Generic;
using System.Linq;
using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Web.Api.Dto
{
    public class CalendarDetailDto
    {
        protected CalendarDetailDto(ICalendar calendar)
        {
            CalendarType = calendar.GetType().AssemblyQualifiedNameWithoutVersion();
            Description = calendar.Description;
            if (calendar.CalendarBase != null)
            {
                CalendarBase = Create(calendar.CalendarBase);
            }
        }

        public string CalendarType { get; }
        public string Description { get; }
        public CalendarDetailDto CalendarBase { get; }

        public static CalendarDetailDto Create(ICalendar calendar)
        {
            var annualCalendar = calendar as AnnualCalendar;
            if (annualCalendar != null)
            {
                return new AnnualCalendarDto(annualCalendar);
            }

            var cronCalendar = calendar as CronCalendar;
            if (cronCalendar != null)
            {
                return new CronCalendarDto(cronCalendar);
            }

            var dailyCalendar = calendar as DailyCalendar;
            if (dailyCalendar != null)
            {
                return new DailyCalendarDto(dailyCalendar);
            }

            var holidayCalendar = calendar as HolidayCalendar;
            if (holidayCalendar != null)
            {
                return new HolidayCalendarDto(holidayCalendar);
            }

            var monthlyCalendar = calendar as MonthlyCalendar;
            if (monthlyCalendar != null)
            {
                return new MonthlyCalendarDto(monthlyCalendar);
            }

            var weeklyCalendar = calendar as WeeklyCalendar;
            if (weeklyCalendar != null)
            {
                return new WeeklyCalendarDto(weeklyCalendar);
            }

            return new CalendarDetailDto(calendar);
        }

        public class AnnualCalendarDto : CalendarDetailDto
        {
            public AnnualCalendarDto(AnnualCalendar calendar) : base(calendar)
            {
                DaysExcluded = calendar.DaysExcluded;
                TimeZone = new TimeZoneDto(calendar.TimeZone);
            }

            public ISet<DateTime> DaysExcluded { get; }
            public TimeZoneDto TimeZone { get; }
        }

        public class CronCalendarDto : CalendarDetailDto
        {
            public CronCalendarDto(CronCalendar calendar) : base(calendar)
            {
                CronExpression = calendar.CronExpression.CronExpressionString;
                TimeZone = new TimeZoneDto(calendar.TimeZone);
            }

            public string CronExpression { get; }
            public TimeZoneDto TimeZone { get; }
        }

        public class DailyCalendarDto : CalendarDetailDto
        {
            public DailyCalendarDto(DailyCalendar calendar) : base(calendar)
            {
                InvertTimeRange = calendar.InvertTimeRange;
                TimeZone = new TimeZoneDto(calendar.TimeZone);
            }

            public bool InvertTimeRange { get; }
            public TimeZoneDto TimeZone { get; }
        }

        public class HolidayCalendarDto : CalendarDetailDto
        {
            public HolidayCalendarDto(HolidayCalendar calendar) : base(calendar)
            {
                ExcludedDates = calendar.ExcludedDates.ToList();
                TimeZone = new TimeZoneDto(calendar.TimeZone);
            }

            public IReadOnlyList<DateTime> ExcludedDates { get; }
            public TimeZoneDto TimeZone { get; }
        }

        public class MonthlyCalendarDto : CalendarDetailDto
        {
            public MonthlyCalendarDto(MonthlyCalendar calendar) : base(calendar)
            {
                DaysExcluded = calendar.DaysExcluded.ToList();
                TimeZone = new TimeZoneDto(calendar.TimeZone);
            }

            public IReadOnlyList<bool> DaysExcluded { get; }
            public TimeZoneDto TimeZone { get; }
        }

        public class WeeklyCalendarDto : CalendarDetailDto
        {
            public WeeklyCalendarDto(WeeklyCalendar calendar) : base(calendar)
            {
                DaysExcluded = calendar.DaysExcluded.ToList();
                TimeZone = new TimeZoneDto(calendar.TimeZone);
            }

            public IReadOnlyList<bool> DaysExcluded { get; }
            public TimeZoneDto TimeZone { get; }
        }
    }
}