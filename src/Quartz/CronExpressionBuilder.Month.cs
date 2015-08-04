using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Quartz
{
	partial class CronExpressionBuilder
	{
		/// <summary>
		/// Returns a CronExpressionBuilder with the Month set as a specific value
		/// </summary>
		/// <param name="month">Value to set as a month</param>
		/// <returns>CronExpressionBuilder with the month set</returns>
		public CronExpressionBuilder Month(int month)
		{
			DateBuilder.ValidateMonth(month);
			MonthArgument = month.ToString();
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Month set as a list of values
		/// </summary>
		/// <param name="months">Values to set as months</param>
		/// <returns>CronExpressionBuilder with the month set</returns>
		public CronExpressionBuilder Month(IEnumerable<int> months)
		{
			if (months == null || months.Count() == 0)
			{
				throw new ArgumentException("Invalid list of month candidates.");
			}
			foreach (var month in months)
			{
				DateBuilder.ValidateMonth(month);
			}
			MonthArgument = String.Join(",", months.Select(x => x.ToString()));
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Month set as an incremental list of values
		/// </summary>
		/// <param name="startMonth">Value to set as the first month</param>
		/// <param name="increment">Value to set as the increment</param>
		/// <returns>CronExpressionBuilder with the hour set</returns>
		public CronExpressionBuilder MonthIncrements(int startMonth, int increment)
		{
			DateBuilder.ValidateMonth(startMonth);
			MonthArgument = string.Format("{0}/{1}", startMonth, increment);
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Month set as a range of values
		/// </summary>
		/// <param name="startMonth">Value to set as the first month</param>
		/// <param name="endMonth">Value to set as the last month</param>
		/// <returns>CronExpressionBuilder with the hour set</returns>
		public CronExpressionBuilder MonthRange(int startMonth, int endMonth)
		{
			DateBuilder.ValidateMonth(startMonth);
			DateBuilder.ValidateMonth(endMonth);
			MonthArgument = string.Format("{0}-{1}", startMonth, endMonth);
			return this;
		}
	}
}
