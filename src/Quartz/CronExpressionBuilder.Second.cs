using System;
using System.Collections.Generic;
using System.Linq;

namespace Quartz
{
	public partial class CronExpressionBuilder
	{
		/// <summary>
		/// Returns a CronExpressionBuilder with the Second set as a specific value
		/// </summary>
		/// <param name="second">Value to set as a second</param>
		/// <returns>CronExpressionBuilder with the second set</returns>
		public CronExpressionBuilder Second(int second)
		{
			DateBuilder.ValidateSecond(second);
			SecondArgument = second.ToString();
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Second set as a list of values
		/// </summary>
		/// <param name="seconds">Values to set as seconds</param>
		/// <returns>CronExpressionBuilder with the second set</returns>
		public CronExpressionBuilder Second(IEnumerable<int> seconds)
		{
			if (seconds == null || seconds.Count() == 0)
			{
				throw new ArgumentException("Invalid list of second candidates.");
			}
			foreach (var second in seconds)
			{
				DateBuilder.ValidateSecond(second);
			}
			SecondArgument = String.Join(",", seconds.Select(x => x.ToString()));
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Second set as an incremental list of values
		/// </summary>
		/// <param name="startAt">Value to set as the first second</param>
		/// <param name="incrementBy">Value to set as the increment</param>
		/// <returns>CronExpressionBuilder with the second set</returns>
		public CronExpressionBuilder SecondIncrements(int startAt, int incrementBy)
		{
			DateBuilder.ValidateSecond(startAt);
			SecondArgument = string.Format("{0}/{1}", startAt, incrementBy);
			return this;
		}

		/// <summary>
		/// Returns a CronExpressionBuilder with the Second set as a range of values
		/// </summary>
		/// <param name="startAt">Value to set as the first second</param>
		/// <param name="endAt">Value to set as the last second</param>
		/// <returns>CronExpressionBuilder with the second set</returns>
		public CronExpressionBuilder SecondRange(int startAt, int endAt)
		{
			DateBuilder.ValidateSecond(startAt);
			DateBuilder.ValidateSecond(endAt);
			SecondArgument = string.Format("{0}-{1}", startAt, endAt);
			return this;
		}
	}
}
