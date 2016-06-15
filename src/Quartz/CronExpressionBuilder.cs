using System;

namespace Quartz
{
	public partial class CronExpressionBuilder
	{
		private const string EveryValue = "*";
		private const string UnspecifiedValue = "?";

		private string _second;
		private string _minute;
		private string _hour;
		private string _dayOfMonth;
		private string _month;
		private string _year;
		private string _dayOfWeek;

		public string CronExpression
		{
			get
			{
				return string.Format("{0} {1} {2} {3} {4} {5}{6}",
					SecondArgument,
					MinuteArgument,
					HourArgument,
					DayOfMonthArgument,
					MonthArgument,
					DayOfWeekArgument,
					string.IsNullOrEmpty(YearArgument) ? string.Empty : string.Format(" {0}", YearArgument)
					);
			}
		}

		private string SecondArgument
		{
			get { return string.IsNullOrEmpty(_second) ? EveryValue : _second; }
			set
			{
				if (!string.IsNullOrEmpty(_second))
				{
					throw new ArgumentException("Second has already been configured.");
				}
				_second = value;
			}
		}

		private string MinuteArgument
		{
			get { return string.IsNullOrEmpty(_minute) ? EveryValue : _minute; }
			set
			{
				if (!string.IsNullOrEmpty(_minute))
				{
					throw new ArgumentException("Minute has already been configured.");
				}
				_minute = value;
			}
		}

		private string HourArgument
		{
			get { return string.IsNullOrEmpty(_hour) ? EveryValue : _hour; }
			set
			{
				if (!string.IsNullOrEmpty(_hour))
				{
					throw new ArgumentException("Hour has already been configured.");
				}
				_hour = value;
			}
		}

		private string DayOfMonthArgument
		{
			get { return string.IsNullOrEmpty(_dayOfMonth) ? UnspecifiedValue : _dayOfMonth; }
			set
			{
				if (!string.IsNullOrEmpty(_dayOfMonth))
				{
					throw new ArgumentException("Day of Month has already been configured.");
				}
				_dayOfMonth = value;
				_dayOfWeek = UnspecifiedValue;
			}
		}

		private string MonthArgument
		{
			get { return string.IsNullOrEmpty(_month) ? EveryValue : _month; }
			set
			{
				if (!string.IsNullOrEmpty(_month))
				{
					throw new ArgumentException("Month has already been configured.");
				}
				_month = value;
			}
		}

		private string DayOfWeekArgument
		{
			get { return string.IsNullOrEmpty(_dayOfWeek) ? EveryValue : _dayOfWeek; }
			set
			{
				if (!string.IsNullOrEmpty(_dayOfWeek))
				{
					throw new ArgumentException("Day of Week has already been configured.");
				}
				_dayOfWeek = value;
				_dayOfMonth = UnspecifiedValue;
			}
		}

		private string YearArgument
		{
			get { return string.IsNullOrEmpty(_year) ? string.Empty : _year; }
			set
			{
				if (!string.IsNullOrEmpty(_year))
				{
					throw new ArgumentException("Month has already been configured.");
				}
				_year = value;
			}
		}
	}
}
