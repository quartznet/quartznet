using System;
using System.Collections.Generic;
using System.Linq;

namespace Quartz
{
	public partial class CronExpressionBuilder
	{
		/// <summary>
		/// Returns a CronExpressionBuilder with the DayOfMonth set as a specific value
		/// </summary>
		/// <param name="day">Value to set as a day</param>
		/// <returns>CronExpressionBuilder with the day set</returns>
		public CronExpressionBuilder DayOfMonth(int day)
		{
			DateBuilder.ValidateDayOfMonth(day);
			DayOfMonthArgument = day.ToString();
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the DayOfMonth set as a list of values
		/// </summary>
		/// <param name="days">Values to set as days</param>
		/// <returns>CronExpressionBuilder with the day set</returns>
		public CronExpressionBuilder DayOfMonth(IEnumerable<int> days)
		{
			if (days == null || days.Count() == 0)
			{
				throw new ArgumentException("Invalid list of day candidates.");
			}
			foreach (var day in days)
			{
				DateBuilder.ValidateDayOfMonth(day);
			}
			DayOfMonthArgument = String.Join(",", days.Select(x => x.ToString()));

			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the DayOfMonth set as an incremental list of values
		/// </summary>
		/// <param name="startAt">Value to set as the first day</param>
		/// <param name="incrementBy">Value to set as the increment</param>
		/// <returns>CronExpressionBuilder with the day set</returns>
		public CronExpressionBuilder DayOfMonthIncrements(int startAt, int incrementBy)
		{
			DateBuilder.ValidateDayOfMonth(startAt);
			DayOfMonthArgument = string.Format("{0}/{1}", startAt, incrementBy);
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the DayOfMonth set as a range of values
		/// </summary>
		/// <param name="startAt">Value to set as the first day</param>
		/// <param name="endAt">Value to set as the last day</param>
		/// <returns>CronExpressionBuilder with the day set</returns>
		public CronExpressionBuilder DayOfMonthRange(int startAt, int endAt)
		{
			DateBuilder.ValidateDayOfMonth(startAt);
			DateBuilder.ValidateDayOfMonth(endAt);
			DayOfMonthArgument = string.Format("{0}-{1}", startAt, endAt);
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the DayOfMonth set as the last day of the month.
		/// </summary>
		/// <returns>CronExpressionBuilder with the day set to L</returns>
		public CronExpressionBuilder LastDayOfMonth()
		{
			DayOfMonthArgument = "L";
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the DayOfMonth set as the nearest weekday to day
		/// </summary>
		/// <param name="day">Value to set as the day</param>
		/// <returns>CronExpressionBuilder with the day set</returns>
		public CronExpressionBuilder NearestWeekDayOfMonth(int day)
		{
			DateBuilder.ValidateDayOfMonth(day);
			DayOfMonthArgument = string.Format("{0}W", day);
			return this;
		}
	}
}
