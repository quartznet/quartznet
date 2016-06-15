using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;

namespace Quartz
{
	public partial class CronExpressionBuilder
	{
		/// <summary>
		/// Returns a CronExpressionBuilder with the DayOfWeek set as a specific value
		/// </summary>
		/// <param name="dayOfWeek">Value to set as a day</param>
		/// <returns>CronExpressionBuilder with the DayOfWeek set</returns>
		public CronExpressionBuilder DayOfWeek(DayOfWeek dayOfWeek)
		{
			this.DayOfWeekArgument = ((int)dayOfWeek + 1).ToString();
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the DayOfWeek set as a list of values
		/// </summary>
		/// <param name="daysOfWeek">Values to set as days</param>
		/// <returns>CronExpressionBuilder with the DayOfWeek set</returns>
		public CronExpressionBuilder DayOfWeek(IEnumerable<DayOfWeek> daysOfWeek)
		{
			if (daysOfWeek == null || daysOfWeek.Count() == 0)
			{
				throw new ArgumentException("Invalid list of day-of-week candidates.");
			}
			this.DayOfWeekArgument = String.Join(",", daysOfWeek.Select(x => ((int)x + 1).ToString()));

			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the DayOfWeek set as an incremental list of values
		/// </summary>
		/// <param name="startAt">Value to set as the first day</param>
		/// <param name="incrementBy">Value to set as the increment</param>
		/// <returns>CronExpressionBuilder with the DayOfWeek set</returns>
		public CronExpressionBuilder DayOfWeekIncrements(System.DayOfWeek startAt, int incrementBy)
		{
			this.DayOfWeekArgument = string.Format("{0}/{1}", (int) startAt + 1, incrementBy);
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the DayOfWeek set as a range of values
		/// </summary>
		/// <param name="startAt">Value to set as the first day</param>
		/// <param name="endAt">Value to set as the last day</param>
		/// <returns>CronExpressionBuilder with the DayOfWeek set</returns>
		public CronExpressionBuilder DayOfWeekRange(DayOfWeek startAt, DayOfWeek endAt)
		{
			this.DayOfWeekArgument = string.Format("{0}-{1}", (int)startAt + 1, (int) endAt + 1);
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the DayOfWeek set to the range of Monday-Friday
		/// </summary>
		/// <returns>CronExpressionBuilder with the DayOfWeek set</returns>
		public CronExpressionBuilder Weekdays()
		{
			return this.DayOfWeekRange(System.DayOfWeek.Monday, System.DayOfWeek.Friday);
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the DayOfWeek set to the nth occurrence of day
		/// </summary>
		/// <param name="day">The day of the week</param>
		/// <param name="n">The nth occurrence</param>
		/// <returns>CronExpressionBuilder with the DayOfWeek set</returns>
		public CronExpressionBuilder NthDayOfWeekOfMonth(DayOfWeek day, int n)
		{
			this.DayOfWeekArgument = string.Format("{0}#{1}", (int) day + 1, n);
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the last DayOfWeek of month syntax
		/// </summary>
		/// <param name="day">The day of the week</param>
		/// <returns>CronExpressionBuilder with the DayOfWeek set</returns>
		public CronExpressionBuilder LastDayOfWeekInMonth(DayOfWeek day)
		{
			this.DayOfWeekArgument = string.Format("{0}L", (int) day+1);
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the last DayOfWeek syntax
		/// </summary>
		/// <returns>CronExpressionBuilder with the DayOfWeek set</returns>
		public CronExpressionBuilder LastDayOfWeek()
		{
			this.DayOfWeekArgument = "L";
			return this;
		}
	}
}
