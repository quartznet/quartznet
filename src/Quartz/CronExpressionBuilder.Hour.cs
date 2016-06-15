using System;
using System.Collections.Generic;
using System.Linq;

namespace Quartz
{
	public partial class CronExpressionBuilder
	{
		/// <summary>
		/// Returns a CronExpressionBuilder with the Hour set as a specific value
		/// </summary>
		/// <param name="hour">Value to set as a hour</param>
		/// <returns>CronExpressionBuilder with the hour set</returns>
		public CronExpressionBuilder Hour(int hour)
		{
			DateBuilder.ValidateHour(hour);
			HourArgument = hour.ToString();
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Hour set as a list of values
		/// </summary>
		/// <param name="hours">Values to set as hours</param>
		/// <returns>CronExpressionBuilder with the hour set</returns>
		public CronExpressionBuilder Hour(IEnumerable<int> hours)
		{
			if (hours == null || hours.Count() == 0)
			{
				throw new ArgumentException("Invalid list of hour candidates.");
			}
			foreach (var hour in hours)
			{
				DateBuilder.ValidateHour(hour);
			}
			HourArgument = String.Join(",", hours);

			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Hour set as an incremental list of values
		/// </summary>
		/// <param name="startAt">Value to set as the first hour</param>
		/// <param name="incrementBy">Value to set as the increment</param>
		/// <returns>CronExpressionBuilder with the hour set</returns>
		public CronExpressionBuilder HourIncrements(int startAt, int incrementBy)
		{
			DateBuilder.ValidateHour(startAt);
			HourArgument = string.Format("{0}/{1}", startAt, incrementBy);
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Hour set as a range of values
		/// </summary>
		/// <param name="startAt">Value to set as the first hour</param>
		/// <param name="endAt">Value to set as the last hour</param>
		/// <returns>CronExpressionBuilder with the hour set</returns>
		public CronExpressionBuilder HourRange(int startAt, int endAt)
		{
			DateBuilder.ValidateHour(startAt);
			DateBuilder.ValidateHour(endAt);
			HourArgument = string.Format("{0}-{1}", startAt, endAt);
			return this;
		}
	}
}
