using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Quartz
{
	partial class CronExpressionBuilder
	{
		/// <summary>
		/// Returns a CronExpressionBuilder with the Year set as a specific value
		/// </summary>
		/// <param name="year">Value to set as a year</param>
		/// <returns>CronExpressionBuilder with the year set</returns>
		public CronExpressionBuilder Year(int year)
		{
			DateBuilder.ValidateYear(year);
			YearArgument = year.ToString();
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Year set as a list of values
		/// </summary>
		/// <param name="years">Values to set as years</param>
		/// <returns>CronExpressionBuilder with the year set</returns>
		public CronExpressionBuilder Year(IEnumerable<int> years)
		{
			if (years == null || years.Count() == 0)
			{
				throw new ArgumentException("Invalid list of year candidates.");
			}
			foreach (var year in years)
			{
				DateBuilder.ValidateYear(year);
			}
			YearArgument = String.Join(",", years.Select(x => x.ToString()));
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Year set as an incremental list of values
		/// </summary>
		/// <param name="startYear">Value to set as the first year</param>
		/// <param name="increment">Value to set as the increment</param>
		/// <returns>CronExpressionBuilder with the hour set</returns>
		public CronExpressionBuilder YearIncrements(int startYear, int increment)
		{
			DateBuilder.ValidateYear(startYear);
			YearArgument = string.Format("{0}/{1}", startYear, increment);
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Year set as a range of values
		/// </summary>
		/// <param name="startYear">Value to set as the first year</param>
		/// <param name="endYear">Value to set as the last year</param>
		/// <returns>CronExpressionBuilder with the hour set</returns>
		public CronExpressionBuilder YearRange(int startYear, int endYear)
		{
			DateBuilder.ValidateYear(startYear);
			DateBuilder.ValidateYear(endYear);
			YearArgument = string.Format("{0}-{1}", startYear, endYear);
			return this;
		}
	}
}
