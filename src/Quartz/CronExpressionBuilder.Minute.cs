using System;
using System.Collections.Generic;
using System.Linq;

namespace Quartz
{
	public partial class CronExpressionBuilder
	{
		/// <summary>
		/// Returns a CronExpressionBuilder with the Minute set as a specific value
		/// </summary>
		/// <param name="minute">Value to set as a minute</param>
		/// <returns>CronExpressionBuilder with the minute set</returns>
		public CronExpressionBuilder Minute(int minute)
		{
			DateBuilder.ValidateMinute(minute);
			MinuteArgument = minute.ToString();
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Minute set as a list of values
		/// </summary>
		/// <param name="minutes">Values to set as minutes</param>
		/// <returns>CronExpressionBuilder with the minute set</returns>
		public CronExpressionBuilder Minute(IEnumerable<int> minutes)
		{
			if (minutes == null || minutes.Count() == 0)
			{
				throw new ArgumentException("Invalid list of minute candidates.");
			}
			foreach (var minute in minutes)
			{
				DateBuilder.ValidateMinute(minute);
			}
			MinuteArgument = String.Join(",", minutes.Select(x => x.ToString()));
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Minute set as an incremental list of values
		/// </summary>
		/// <param name="startAt">Value to set as the first minute</param>
		/// <param name="incrementBy">Value to set as the increment</param>
		/// <returns>CronExpressionBuilder with the minute set</returns>
		public CronExpressionBuilder MinuteIncrements(int startAt, int incrementBy)
		{
			DateBuilder.ValidateMinute(startAt);
			MinuteArgument = string.Format("{0}/{1}", startAt, incrementBy);
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Minute set as a range of values
		/// </summary>
		/// <param name="startAt">Value to set as the first minute</param>
		/// <param name="endAt">Value to set as the last minute</param>
		/// <returns>CronExpressionBuilder with the minute set</returns>
		public CronExpressionBuilder MinuteRange(int startAt, int endAt)
		{
			DateBuilder.ValidateMinute(startAt);
			DateBuilder.ValidateMinute(endAt);
			MinuteArgument = string.Format("{0}-{1}", startAt, endAt);
			return this;
		}
	}
}
