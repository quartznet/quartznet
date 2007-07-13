using System;
using System.Collections;

using Nullables;
/* 
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

namespace Quartz
{
	/// <summary>
	/// Convenience and utility methods for simplifying the construction and
	/// configuration of <see cref="Trigger" />s.
	/// </summary>
	/// <remarks>
	/// Please submit suggestions for additional convenience methods to either the
	/// Quartz user forum or the developer's mail list at
	/// <a href="http://www.sourceforge.net/projects/quartz">source forge</a>.
    /// </remarks>
	/// <seealso cref="CronTrigger" />
	/// <seealso cref="SimpleTrigger" />
	/// <author>James House</author>
	public class TriggerUtils
	{
		/// <summary>
		/// Constant indicating last day of month.
		/// </summary>
		public const int LAST_DAY_OF_MONTH = -1;

		/// <summary>
		/// Milliseconds in minute.
		/// </summary>
		public const long MILLISECONDS_IN_MINUTE = 60 * 1000;

		/// <summary>
		/// Milliseconds in hour.
		/// </summary>
		public const long MILLISECONDS_IN_HOUR = 60 * 60 * 1000;
		
		/// <summary>
		/// Seconds in day.
		/// </summary>
		public const long SECONDS_IN_DAY = 24 * 60 * 60;

		/// <summary>
		/// Milliseconds in day.
		/// </summary>
		public const long MILLISECONDS_IN_DAY = SECONDS_IN_DAY * 1000;


        /// <summary>
        /// Initializes a new instance of the <see cref="TriggerUtils"/> class.
        /// </summary>
	    private TriggerUtils()
	    {
	    }

		private static void ValidateHour(int hour)
		{
			if (hour < 0 || hour > 23)
			{
				throw new ArgumentException("Invalid hour (must be >= 0 and <= 23).");
			}
		}

		private static void ValidateMinute(int minute)
		{
			if (minute < 0 || minute > 59)
			{
				throw new ArgumentException("Invalid minute (must be >= 0 and <= 59).");
			}
		}

		private static void ValidateSecond(int second)
		{
			if (second < 0 || second > 59)
			{
				throw new ArgumentException("Invalid second (must be >= 0 and <= 59).");
			}
		}

		private static void ValidateDayOfMonth(int day)
		{
			if ((day < 1 || day > 31) && day != LAST_DAY_OF_MONTH)
			{
				throw new ArgumentException("Invalid day of month.");
			}
		}

		private static void ValidateMonth(int month)
		{
			if (month < 1 || month > 12)
			{
				throw new ArgumentException("Invalid month (must be >= 1 and <= 12.");
			}
		}

		private static void ValidateYear(int year)
		{
			if (year < 1970 || year > 2099)
			{
				throw new ArgumentException("Invalid year (must be >= 1970 and <= 2099.");
			}
		}

		/// <summary>
		/// Set the given <see cref="Trigger" />'s name to the given value, and its
		/// group to the default group (<see cref="Scheduler_Fields.DEFAULT_GROUP" />).
		/// </summary>
		/// <param name="trig">the tigger to change name to</param>
		/// <param name="name">the new trigger name</param>
		public static void SetTriggerIdentity(Trigger trig, string name)
		{
			SetTriggerIdentity(trig, name, Scheduler_Fields.DEFAULT_GROUP);
		}

		/// <summary>
		/// Set the given <see cref="Trigger" />'s name to the given value, and its
		/// group to the given group.
		/// </summary>
		/// <param name="trig">the tigger to change name to</param>
		/// <param name="name">the new trigger name</param>
		/// <param name="group">the new trigger group</param>
		public static void SetTriggerIdentity(Trigger trig, string name, string group)
		{
			trig.Name = name;
			trig.Group = group;
		}

		/// <summary>
		/// Make a trigger that will fire every day at the given time.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="hour">the hour (0-23) upon which to fire</param>
		/// <param name="minute">the minute (0-59) upon which to fire</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeDailyTrigger(int hour, int minute)
		{
			ValidateHour(hour);
			ValidateMinute(minute);

			CronTrigger trig = new CronTrigger();

			try
			{
				trig.CronExpressionString = string.Format("0 {0} {1} ? * *", minute, hour);
			}
			catch (Exception)
			{
				return null; /* never happens... */
			}

			trig.StartTime = DateTime.Now;

			return trig;
		}

		/// <summary>
		/// Make a trigger that will fire every day at the given time.
		/// <p>
		/// The generated trigger will not have its group or end-time set.
		/// The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="trigName">the trigger's name</param>
		/// <param name="hour">the hour (0-23) upon which to fire</param>
		/// <param name="minute">the minute (0-59) upon which to fire</param>
		/// <returns>the newly created trigger</returns>
		public static Trigger MakeDailyTrigger(string trigName, int hour, int minute)
		{
			Trigger trig = MakeDailyTrigger(hour, minute);
			trig.Name = trigName;
			return trig;
		}

		/// <summary>
		/// Make a trigger that will fire every week at the given day and time.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="dayOfWeek">(1-7) the day of week upon which to fire</param>
		/// <param name="hour">the hour (0-23) upon which to fire</param>
		/// <param name="minute">the minute (0-59) upon which to fire</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeWeeklyTrigger(DayOfWeek dayOfWeek, int hour, int minute)
		{
			ValidateHour(hour);
			ValidateMinute(minute);

			CronTrigger trig = new CronTrigger();

			try
			{
				trig.CronExpressionString = string.Format("0 {0} {1} ? * {2}", minute, hour, ((int) dayOfWeek + 1));
			}
			catch (Exception)
			{
				return null; /* never happens... */
			}

			trig.StartTime = DateTime.Now;

			return trig;
		}

		/// <summary>
		/// Make a trigger that will fire every week at the given day and time.
		/// <p>
		/// The generated trigger will not have its group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="trigName">the trigger's name</param>
		/// <param name="dayOfWeek">the day of week upon which to fire</param>
		/// <param name="hour">the hour (0-23) upon which to fire</param>
		/// <param name="minute">the minute (0-59) upon which to fire</param>
		/// <returns>the newly created trigger</returns>
		public static Trigger MakeWeeklyTrigger(string trigName, DayOfWeek dayOfWeek, int hour, int minute)
		{
			Trigger trig = MakeWeeklyTrigger(dayOfWeek, hour, minute);
			trig.Name = trigName;
			return trig;
		}


		/// <summary>
		/// Make a trigger that will fire every month at the given day and time.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// <p>
		/// If the day of the month specified does not occur in a given month, a
		/// firing will not occur that month. (i.e. if dayOfMonth is specified as
		/// 31, no firing will occur in the months of the year with fewer than 31
		/// days).
		/// </p>
		/// </summary>
		/// <param name="dayOfMonth">(1-31, or -1) the day of week upon which to fire</param>
		/// <param name="hour">the hour (0-23) upon which to fire</param>
		/// <param name="minute">the minute (0-59) upon which to fire</param>
		/// <returns>the newly created trigger</returns>
		public static Trigger MakeMonthlyTrigger(int dayOfMonth, int hour, int minute)
		{
			ValidateDayOfMonth(dayOfMonth);
			ValidateHour(hour);
			ValidateMinute(minute);

			CronTrigger trig = new CronTrigger();

			try
			{
				if (dayOfMonth != LAST_DAY_OF_MONTH)
				{
					trig.CronExpressionString = string.Format("0 {0} {1} {2} * ?", minute, hour, dayOfMonth);
				}
				else
				{
					trig.CronExpressionString = string.Format("0 {0} {1} L * ?", minute, hour);
				}
			}
			catch (Exception)
			{
				return null; /* never happens... */
			}

			trig.StartTime = DateTime.Now;

			return trig;
		}

		/// <summary>
		/// Make a trigger that will fire every month at the given day and time.
		/// <p>
		/// The generated trigger will not have its group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// <p>
		/// If the day of the month specified does not occur in a given month, a
		/// firing will not occur that month. (i.e. if dayOfMonth is specified as
		/// 31, no firing will occur in the months of the year with fewer than 31
		/// days).
		/// </p>
		/// </summary>
		/// <param name="trigName">the trigger's name</param>
		/// <param name="dayOfMonth">(1-31, or -1) the day of week upon which to fire</param>
		/// <param name="hour">the hour (0-23) upon which to fire</param>
		/// <param name="minute">the minute (0-59) upon which to fire</param>
		/// <returns>the newly created trigger</returns>
		public static Trigger MakeMonthlyTrigger(string trigName, int dayOfMonth, int hour, int minute)
		{
			Trigger trig = MakeMonthlyTrigger(dayOfMonth, hour, minute);
			trig.Name = trigName;
			return trig;
		}


		/// <summary>
		/// Make a trigger that will fire <param name="repeatCount" /> times, waiting
		/// <param name="repeatInterval" /> milliseconds between each fire.
		/// </summary>
		/// <remarks>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
        /// </remarks>
		/// <returns>the newly created trigger</returns>
		public static Trigger MakeImmediateTrigger(int repeatCount, long repeatInterval)
		{
			SimpleTrigger trig = new SimpleTrigger();
			trig.StartTime = DateTime.Now;
			trig.RepeatCount = repeatCount;
			trig.RepeatInterval = repeatInterval;
			return trig;
		}

		/// <summary>
		/// Make a trigger that will fire <param name="repeatCount" /> times, waiting
		/// <param name="repeatInterval" /> milliseconds between each fire.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="trigName">the trigger's name</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeImmediateTrigger(string trigName, int repeatCount, long repeatInterval)
		{
			Trigger trig = MakeImmediateTrigger(repeatCount, repeatInterval);
			trig.Name = trigName;
			return trig;
		}

		/// <summary>
		/// Make a trigger that will fire every second, indefinitely.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <returns>the new trigger</returns>
		public static Trigger MakeSecondlyTrigger()
		{
			return MakeSecondlyTrigger(1, SimpleTrigger.REPEAT_INDEFINITELY);
		}

		/// <summary>
		/// Make a trigger that will fire every second, indefinitely.
		/// <p>
		/// The generated trigger will not have its group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="trigName">the trigger's name</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeSecondlyTrigger(string trigName)
		{
			return MakeSecondlyTrigger(trigName, 1, SimpleTrigger.REPEAT_INDEFINITELY);
		}


		/// <summary>
		/// Make a trigger that will fire every N seconds, indefinitely.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="intervalInSeconds">the number of seconds between firings</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeSecondlyTrigger(int intervalInSeconds)
		{
			return MakeSecondlyTrigger(intervalInSeconds, SimpleTrigger.REPEAT_INDEFINITELY);
		}

		/// <summary>
		/// Make a trigger that will fire every N seconds, with the given number of
		/// repeats.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="intervalInSeconds">the number of seconds between firings</param>
		/// <param name="repeatCount">the number of times to repeat the firing</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeSecondlyTrigger(int intervalInSeconds, int repeatCount)
		{
			SimpleTrigger trig = new SimpleTrigger();

			trig.RepeatInterval = intervalInSeconds*1000L;
			trig.RepeatCount = repeatCount;
            trig.StartTime = new DateTime();

			return trig;
		}

		/// <summary>
		/// Make a trigger that will fire every N seconds, with the given number of
		/// repeats.
		/// <p>
		/// The generated trigger will not have its group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="trigName">the trigger's name</param>
		/// <param name="intervalInSeconds">the number of seconds between firings</param>
		/// <param name="repeatCount">the number of times to repeat the firing</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeSecondlyTrigger(string trigName, int intervalInSeconds, int repeatCount)
		{
			Trigger trig = MakeSecondlyTrigger(intervalInSeconds, repeatCount);
			trig.Name = trigName;
			return trig;
		}

		/// <summary>
		/// Make a trigger that will fire every minute, indefinitely.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <returns>the new trigger</returns>
		public static Trigger MakeMinutelyTrigger()
		{
			return MakeMinutelyTrigger(1, SimpleTrigger.REPEAT_INDEFINITELY);
		}

		/// <summary>
		/// Make a trigger that will fire every minute, indefinitely.
		/// <p>
		/// The generated trigger will not have its group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="trigName">the trigger's name</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeMinutelyTrigger(string trigName)
		{
			return MakeMinutelyTrigger(trigName, 1, SimpleTrigger.REPEAT_INDEFINITELY);
		}

		/// <summary>
		/// Make a trigger that will fire every N minutes, indefinitely.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="intervalInMinutes">the number of minutes between firings</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeMinutelyTrigger(int intervalInMinutes)
		{
			return MakeMinutelyTrigger(intervalInMinutes, SimpleTrigger.REPEAT_INDEFINITELY);
		}

		/// <summary>
		/// Make a trigger that will fire every N minutes, with the given number of
		/// repeats.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="intervalInMinutes">the number of minutes between firings</param>
		/// <param name="repeatCount">the number of times to repeat the firing</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeMinutelyTrigger(int intervalInMinutes, int repeatCount)
		{
			SimpleTrigger trig = new SimpleTrigger();
			trig.RepeatInterval = intervalInMinutes*MILLISECONDS_IN_MINUTE;
			trig.RepeatCount = repeatCount;
			trig.StartTime = DateTime.Now;

			return trig;
		}

		/// <summary>
		/// Make a trigger that will fire every N minutes, with the given number of
		/// repeats.
		/// <p>
		/// The generated trigger will not have its group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="trigName">the trigger's name</param>
		/// <param name="intervalInMinutes">the number of minutes between firings</param>
		/// <param name="repeatCount">the number of times to repeat the firing</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeMinutelyTrigger(string trigName, int intervalInMinutes, int repeatCount)
		{
			Trigger trig = MakeMinutelyTrigger(intervalInMinutes, repeatCount);
			trig.Name = trigName;
			return trig;
		}

		/// <summary>
		/// Make a trigger that will fire every hour, indefinitely.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <returns>the new trigger</returns>
		public static Trigger MakeHourlyTrigger()
		{
			return MakeHourlyTrigger(1, SimpleTrigger.REPEAT_INDEFINITELY);
		}

		/// <summary>
		/// Make a trigger that will fire every hour, indefinitely.
		/// <p>
		/// The generated trigger will not have its group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="trigName">the trigger's name</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeHourlyTrigger(string trigName)
		{
			return MakeHourlyTrigger(trigName, 1, SimpleTrigger.REPEAT_INDEFINITELY);
		}

		/// <summary>
		/// Make a trigger that will fire every N hours, indefinitely.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="intervalInHours">the number of hours between firings</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeHourlyTrigger(int intervalInHours)
		{
			return MakeHourlyTrigger(intervalInHours, SimpleTrigger.REPEAT_INDEFINITELY);
		}

		/// <summary>
		/// Make a trigger that will fire every N hours, with the given number of
		/// repeats.
		/// <p>
		/// The generated trigger will not have its name, group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="intervalInHours">the number of hours between firings</param>
		/// <param name="repeatCount">the number of times to repeat the firing</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeHourlyTrigger(int intervalInHours, int repeatCount)
		{
			SimpleTrigger trig = new SimpleTrigger();

			trig.RepeatInterval = intervalInHours*MILLISECONDS_IN_HOUR;
			trig.RepeatCount = repeatCount;
			trig.StartTime = DateTime.Now;

			return trig;
		}

		/// <summary>
		/// Make a trigger that will fire every N hours, with the given number of
		/// repeats.
		/// <p>
		/// The generated trigger will not have its group,
		/// or end-time set.  The Start time defaults to 'now'.
		/// </p>
		/// </summary>
		/// <param name="trigName">the trigger's name</param>
		/// <param name="intervalInHours">the number of hours between firings</param>
		/// <param name="repeatCount">the number of times to repeat the firing</param>
		/// <returns>the new trigger</returns>
		public static Trigger MakeHourlyTrigger(string trigName, int intervalInHours, int repeatCount)
		{
			Trigger trig = MakeHourlyTrigger(intervalInHours, repeatCount);
			trig.Name = trigName;
			return trig;
		}

		/// <summary>
		/// Returns a date that is rounded to the next even hour above the given
		/// date.
		/// <p>
		/// For example an input date with a time of 08:13:54 would result in a date
		/// with the time of 09:00:00. If the date's time is in the 23rd hour, the
		/// date's 'day' will be promoted, and the time will be set to 00:00:00.
		/// </p>
		/// </summary>
		/// <param name="date">the Date to round, if <see langword="null" /> the current time will
		/// be used</param>
		/// <returns>the new rounded date</returns>
		public static DateTime GetEvenHourDate(NullableDateTime date)
		{
			if (!date.HasValue)
			{
				date = DateTime.Now;
			}
			DateTime d = date.Value.AddHours(1);
			return new DateTime(d.Year, d.Month, d.Day, d.Hour, 0, 0);
		}

		/// <summary>
		/// Returns a date that is rounded to the previous even hour below the given
		/// date.
		/// <p>
		/// For example an input date with a time of 08:13:54 would result in a date
		/// with the time of 08:00:00.
		/// </p>
		/// </summary>
		/// <param name="date">the Date to round, if <see langword="null" /> the current time will
		/// be used</param>
		/// <returns>the new rounded date</returns>
		public static DateTime GetEvenHourDateBefore(NullableDateTime date)
		{
			if (!date.HasValue)
			{
				date = DateTime.Now;
			}
			return new DateTime(date.Value.Year, date.Value.Month, date.Value.Day, date.Value.Hour, 0, 0);
		}

		/// <summary>
		/// Returns a date that is rounded to the next even minute above the given
		/// date.
		/// <p>
		/// For example an input date with a time of 08:13:54 would result in a date
		/// with the time of 08:14:00. If the date's time is in the 59th minute,
		/// then the hour (and possibly the day) will be promoted.
		/// </p>
		/// </summary>
		/// <param name="date">The Date to round, if <see langword="null" /> the current time will  be used</param>
		/// <returns>The new rounded date</returns>
		public static DateTime GetEvenMinuteDate(NullableDateTime date)
		{
			if (date.HasValue)
			{
				date = DateTime.Now;
			}

			DateTime d = date.Value;
			d = d.AddMinutes(1);
			return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
		}

		/// <summary>
		/// Returns a date that is rounded to the previous even minute below the
		/// given date.
		/// <p>
		/// For example an input date with a time of 08:13:54 would result in a date
		/// with the time of 08:13:00.
		/// </p>
		/// </summary>
		/// <param name="date">the Date to round, if <see langword="null" /> the current time will
		/// be used</param>
		/// <returns>the new rounded date</returns>
		public static DateTime GetEvenMinuteDateBefore(NullableDateTime date)
		{
			if (!date.HasValue)
			{
				date = DateTime.Now;
			}

			DateTime d = date.Value;
			return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
		}

		/// <summary>
		/// Returns a date that is rounded to the next even second above the given
		/// date.
		/// </summary>
		/// <param name="date">the Date to round, if <see langword="null" /> the current time will
		/// be used</param>
		/// <returns>the new rounded date</returns>
		public static DateTime GetEvenSecondDate(NullableDateTime date)
		{
			if (!date.HasValue)
			{
				date = DateTime.Now;
			}
			DateTime d = date.Value;
			d = d.AddSeconds(1);
			return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second);
		}

		/// <summary>
		/// Returns a date that is rounded to the previous even second below the
		/// given date.
		/// <p>
		/// For example an input date with a time of 08:13:54.341 would result in a
		/// date with the time of 08:13:00.000.
		/// </p>
		/// </summary>
		/// <param name="date">the Date to round, if <see langword="null" /> the current time will
		/// be used</param>
		/// <returns>the new rounded date</returns>
		public static DateTime GetEvenSecondDateBefore(NullableDateTime date)
		{
			if (!date.HasValue)
			{
				date = DateTime.Now;
			}
			DateTime d = date.Value;
			return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second);
		}

		/// <summary>
		/// Returns a date that is rounded to the next even multiple of the given
		/// minute.
		/// <p>
		/// For example an input date with a time of 08:13:54, and an input
		/// minute-base of 5 would result in a date with the time of 08:15:00. The
		/// same input date with an input minute-base of 10 would result in a date
		/// with the time of 08:20:00. But a date with the time 08:53:31 and an
		/// input minute-base of 45 would result in 09:00:00, because the even-hour
		/// is the next 'base' for 45-minute intervals.
		/// </p>
		/// 
		/// <p>
		/// More examples: <table>
		/// <tr>
		/// <th>Input Time</th>
		/// <th>Minute-Base</th>
		/// <th>Result Time</th>
		/// </tr>
		/// <tr>
		/// <td>11:16:41</td>
		/// <td>20</td>
		/// <td>11:20:00</td>
		/// </tr>
		/// <tr>
		/// <td>11:36:41</td>
		/// <td>20</td>
		/// <td>11:40:00</td>
		/// </tr>
		/// <tr>
		/// <td>11:46:41</td>
		/// <td>20</td>
		/// <td>12:00:00</td>
		/// </tr>
		/// <tr>
		/// <td>11:26:41</td>
		/// <td>30</td>
		/// <td>11:30:00</td>
		/// </tr>
		/// <tr>
		/// <td>11:36:41</td>
		/// <td>30</td>
		/// <td>12:00:00</td>
		/// </tr>
		/// <tr>
		/// <td>11:16:41</td>
		/// <td>17</td>
		/// <td>11:17:00</td>
		/// </tr>
		/// <tr>
		/// <td>11:17:41</td>
		/// <td>17</td>
		/// <td>11:34:00</td>
		/// </tr>
		/// <tr>
		/// <td>11:52:41</td>
		/// <td>17</td>
		/// <td>12:00:00</td>
		/// </tr>
		/// <tr>
		/// <td>11:52:41</td>
		/// <td>5</td>
		/// <td>11:55:00</td>
		/// </tr>
		/// <tr>
		/// <td>11:57:41</td>
		/// <td>5</td>
		/// <td>12:00:00</td>
		/// </tr>
		/// <tr>
		/// <td>11:17:41</td>
		/// <td>0</td>
		/// <td>12:00:00</td>
		/// </tr>
		/// <tr>
		/// <td>11:17:41</td>
		/// <td>1</td>
		/// <td>11:08:00</td>
		/// </tr>
		/// </table>
		/// </p>
		/// 
		/// </summary>
		/// <param name="date">
		/// the Date to round, if <see langword="null" /> the current time will
		/// be used
		/// </param>
		/// <param name="minuteBase">
		/// the base-minute to set the time on
		/// </param>
		/// <returns> the new rounded date</returns>
		public static DateTime GetNextGivenMinuteDate(NullableDateTime date, int minuteBase)
		{
			if (minuteBase < 0 || minuteBase > 59)
			{
				throw new ArgumentException("minuteBase must be >=0 and <= 59");
			}

			if (!date.HasValue)
			{
				date = DateTime.Now;
			}
			DateTime d = date.Value;

			if (minuteBase == 0)
			{
				d = d.AddHours(1);
				return new DateTime(d.Year, d.Month, d.Day, d.Hour, 0, 0);
			}

			int minute = d.Minute;
			int arItr = minute/minuteBase;
			int nextMinuteOccurance = minuteBase*(arItr + 1);

			if (nextMinuteOccurance < 60)
			{
				return new DateTime(d.Year, d.Month, d.Day, d.Hour, nextMinuteOccurance, 0);
			}
			else
			{
				d = d.AddHours(1);
				return new DateTime(d.Year, d.Month, d.Day, d.Hour, 0, 0);
			}
		}

		/// <summary>
		/// Returns a date that is rounded to the next even multiple of the given
		/// minute.
		/// <p>
		/// The rules for calculating the second are the same as those for
		/// calculating the minute in the method
		/// <see cref="GetNextGivenMinuteDate" />.
		/// </p>
		/// </summary>
		/// <param name="date">The date.</param>
		/// <param name="secondBase">The second base.</param>
		/// <returns></returns>
		public static DateTime GetNextGivenSecondDate(NullableDateTime date, int secondBase)
		{
			if (secondBase < 0 || secondBase > 59)
			{
				throw new ArgumentException("secondBase must be >=0 and <= 59");
			}

			if (!date.HasValue)
			{
				date = DateTime.Now;
			}

			DateTime d = date.Value;

			if (secondBase == 0)
			{
				d = d.AddMinutes(1);
				return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
			}

			int second = d.Second;
			int arItr = second/secondBase;
			int nextSecondOccurance = secondBase*(arItr + 1);

			if (nextSecondOccurance < 60)
			{
				return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, nextSecondOccurance);
			}
			else
			{
				d = d.AddMinutes(1);
				return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
			}
		}

		/// <summary>
		/// Get a <see cref="DateTime" /> object that represents the given time, on
		/// today's date.
		/// </summary>
		/// <param name="second">The value (0-59) to give the seconds field of the date</param>
		/// <param name="minute">The value (0-59) to give the minutes field of the date</param>
		/// <param name="hour">The value (0-23) to give the hours field of the date</param>
		/// <returns>the new date</returns>
		public static DateTime GetDateOf(int second, int minute, int hour)
		{
			ValidateSecond(second);
			ValidateMinute(minute);
			ValidateHour(hour);

			DateTime now = DateTime.Now;
			return new DateTime(now.Year, now.Month, now.Day, hour, minute, second);
		}

		/// <summary>
		/// Get a <see cref="DateTime" /> object that represents the given time, on the
		/// given date.
		/// </summary>
		/// <param name="second">The value (0-59) to give the seconds field of the date</param>
		/// <param name="minute">The value (0-59) to give the minutes field of the date</param>
		/// <param name="hour">The value (0-23) to give the hours field of the date</param>
		/// <param name="dayOfMonth">The value (1-31) to give the day of month field of the date</param>
		/// <param name="month">The value (1-12) to give the month field of the date</param>
		/// <returns>the new date</returns>
		public static DateTime GetDateOf(int second, int minute, int hour, int dayOfMonth, int month)
		{
			ValidateSecond(second);
			ValidateMinute(minute);
			ValidateHour(hour);
			ValidateDayOfMonth(dayOfMonth);
			ValidateMonth(month);

			return new DateTime(DateTime.Now.Year, month, dayOfMonth, hour, minute, second);
		}

		/// <summary>
		/// Get a <see cref="DateTime" /> object that represents the given time, on the
		/// given date.
		/// </summary>
		/// <param name="second">The value (0-59) to give the seconds field of the date</param>
		/// <param name="minute">The value (0-59) to give the minutes field of the date</param>
		/// <param name="hour">The value (0-23) to give the hours field of the date</param>
		/// <param name="dayOfMonth">The value (1-31) to give the day of month field of the date</param>
		/// <param name="month">The value (1-12) to give the month field of the date</param>
		/// <param name="year">The value (1970-2099) to give the year field of the date</param>
		/// <returns>the new date</returns>
		public static DateTime GetDateOf(int second, int minute, int hour, int dayOfMonth, int month, int year)
		{
			ValidateSecond(second);
			ValidateMinute(minute);
			ValidateHour(hour);
			ValidateDayOfMonth(dayOfMonth);
			ValidateMonth(month);
			ValidateYear(year);

			return new DateTime(year, month, dayOfMonth, hour, minute, second);
		}

		/// <summary>
		/// Returns a list of Dates that are the next fire times of a
		/// <see cref="Trigger" />.
		/// The input trigger will be cloned before any work is done, so you need
		/// not worry about its state being altered by this method.
		/// </summary>
		/// <param name="trigg">The trigger upon which to do the work</param>
		/// <param name="cal">The calendar to apply to the trigger's schedule</param>
		/// <param name="numTimes">The number of next fire times to produce</param>
		/// <returns>List of java.util.Date objects</returns>
		public static IList ComputeFireTimes(Trigger trigg, ICalendar cal, int numTimes)
		{
			ArrayList lst = new ArrayList();

			Trigger t = (Trigger) trigg.Clone();

			if (t.GetNextFireTime() == null || !t.GetNextFireTime().HasValue)
			{
				t.ComputeFirstFireTime(cal);
			}

			for (int i = 0; i < numTimes; i++)
			{
				NullableDateTime d = t.GetNextFireTime();
				if (d != null && d.HasValue)
				{
					lst.Add(d);
					t.Triggered(cal);
				}
				else
				{
					break;
				}
			}

			return ArrayList.ReadOnly(new ArrayList(lst));
		}

		/// <summary>
		/// Returns a list of Dates that are the next fire times of a  <see cref="Trigger" />
		/// that fall within the given date range. The input trigger will be cloned
		/// before any work is done, so you need not worry about its state being
		/// altered by this method.
		/// <p>
		/// NOTE: if this is a trigger that has previously fired within the given
		/// date range, then firings which have already occured will not be listed
		/// in the output List.
		/// </p>
		/// </summary>
		/// <param name="trigg">The trigger upon which to do the work</param>
		/// <param name="cal">The calendar to apply to the trigger's schedule</param>
		/// <param name="from">The starting date at which to find fire times</param>
		/// <param name="to">The ending date at which to stop finding fire times</param>
		/// <returns>List of java.util.Date objects</returns>
		public static IList ComputeFireTimesBetween(Trigger trigg, ICalendar cal, DateTime from, DateTime to)
		{
			ArrayList lst = new ArrayList();

			Trigger t = (Trigger) trigg.Clone();

			if (t.GetNextFireTime() == null || !t.GetNextFireTime().HasValue)
			{
				t.StartTime = from;
				t.EndTime = to;
				t.ComputeFirstFireTime(cal);
			}

			// TODO: this method could be more efficient by using logic specific
			//        to the type of trigger ...
			while (true)
			{
				NullableDateTime d = t.GetNextFireTime();
				if (d != null && d.HasValue)
				{
					if ((d.Value < from))
					{
						t.Triggered(cal);
						continue;
					}
					if ((d.Value > to))
					{
						break;
					}
					lst.Add(d);
					t.Triggered(cal);
				}
				else
				{
					break;
				}
			}
			return ArrayList.ReadOnly(new ArrayList(lst));
		}

		/// <summary>
		/// Translate a date and time from a users timezone to the another
		/// (probably server) timezone to assist in creating a simple trigger with
		/// the right date and time.
		/// </summary>
		/// <param name="date">the date to translate</param>
		/// <param name="src">the original time-zone</param>
		/// <param name="dest">the destination time-zone</param>
		/// <returns>the translated date</returns>
		public static DateTime TranslateTime(DateTime date, TimeZone src, TimeZone dest)
		{
			DateTime newDate = DateTime.Now;
 			int offset = (GetOffset(date, dest) - GetOffset(date, src));

			newDate = newDate.AddMilliseconds(-1*offset);

			return newDate;
		}

		/// <summary>
		/// Gets the offset from UT for the given date in the given timezone,
		/// taking into account daylight savings.
		/// </summary>
		/// <param name="date">the date (in milliseconds) that is the base for the offset</param>
		/// <param name="tz">the time-zone to calculate to offset to</param>
		/// <returns>the offset</returns>
		public static int GetOffset(DateTime date, TimeZone tz)
		{
			// TODO
			if (tz.IsDaylightSavingTime(date))
			{
				return 0; // TODO tz.getRawOffset() + getDSTSavings(tz);
			}

			return 0; // TODO tz.getRawOffset();
		}

        /// <summary>
        /// This functions determines if the TimeZone uses daylight saving time
        /// </summary>
        /// <param name="timezone">TimeZone instance to validate</param>
        /// <returns>True or false depending if daylight savings time is used</returns>
        public static bool UseDaylightTime(TimeZone timezone)
        {
            return (timezone.DaylightName != "");
        }
	}
}
