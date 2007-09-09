using System;
using System.Text;

namespace Quartz.Impl.Calendar
{
	/// <summary>
	/// This implementation of the Calendar excludes the set of times expressed by a
	/// given CronExpression. 
	/// </summary>
	/// <remarks>
	/// For example, you could use this calendar to exclude all but business hours (8AM - 5PM) every 
	/// day using the expression &quot;* * 0-7,18-23 ? * *&quot;. 
	/// <p>
	/// It is important to remember that the cron expression here describes a set of
	/// times to be <i>excluded</i> from firing. Whereas the cron expression in 
	/// CronTrigger describes a set of times that can
	/// be <i>included</i> for firing. Thus, if a <see cref="CronTrigger" /> has a 
	/// given cron expression and is associated with a <see cref="CronCalendar" /> with
	/// the <i>same</i> expression, the calendar will exclude all the times the 
	/// trigger includes, and they will cancel each other out.
	/// </p>
	/// </remarks>
	/// <author>Aaron Craven</author>
    [Serializable]
	public class CronCalendar : BaseCalendar
	{
		CronExpression cronExpression;

		/// <summary>
		/// Initializes a new instance of the <see cref="CronCalendar"/> class.
		/// </summary>
		/// <param name="expression">a String representation of the desired cron expression</param>
		public CronCalendar(string expression)
		{
			cronExpression = new CronExpression(expression);
		}

		/// <summary>
		/// Create a <see cref="CronCalendar" /> with the given cron expression and 
		/// <see cref="BaseCalendar" />. 
		/// </summary>
		/// <param name="baseCalendar">
		/// the base calendar for this calendar instance 
		/// see BaseCalendar for more information on base
		/// calendar functionality
		/// </param>
		/// <param name="expression">a String representation of the desired cron expression</param>
		public CronCalendar( ICalendar baseCalendar,
		                    string expression) : base(baseCalendar)
		{
            cronExpression = new CronExpression(expression);
		}

        /// <summary>
        /// Create a <see cref="CronCalendar" /> with the given cron expression and 
        /// <see cref="BaseCalendar" />. 
        /// </summary>
        /// <param name="baseCalendar">
        /// the base calendar for this calendar instance 
        /// see BaseCalendar for more information on base
        /// calendar functionality
        /// </param>
        /// <param name="expression">a String representation of the desired cron expression</param>
        public CronCalendar(ICalendar baseCalendar,
                            string expression,
                            TimeZone timeZone)
            : base(baseCalendar, timeZone)
        {
            cronExpression = new CronExpression(expression);
        }


	    public override TimeZone TimeZone
	    {
	        get { return cronExpression.TimeZone; }
            set { cronExpression.TimeZone = value; }
	    }

	    /// <summary>
		/// Determine whether the given time  is 'included' by the
		/// Calendar.
		/// </summary>
		/// <param name="timeUtc">the time to test</param>
		/// <returns>a boolean indicating whether the specified time is 'included' by the CronCalendar</returns>
		public override bool IsTimeIncluded(DateTime timeUtc)
		{
			if ((GetBaseCalendar() != null) &&
			    (GetBaseCalendar().IsTimeIncluded(timeUtc) == false))
			{
				return false;
			}

			return (!(cronExpression.IsSatisfiedBy(timeUtc)));
		}

		/// <summary>
		/// Determine the next time that is 'included' by the
		/// Calendar after the given time. Return the original value if timeStamp is
		/// included. Return 0 if all days are excluded.
		/// </summary>
		/// <param name="timeUtc"></param>
		/// <returns></returns>
		public override DateTime GetNextIncludedTimeUtc(DateTime timeUtc)
		{
			DateTime nextIncludedTime = timeUtc.AddMilliseconds(1); //plus on millisecond

			while (!IsTimeIncluded(nextIncludedTime))
			{
				//If the time is in a range excluded by this calendar, we can
				// move to the end of the excluded time range and continue testing
				// from there. Otherwise, if nextIncludedTime is excluded by the
				// baseCalendar, ask it the next time it includes and begin testing
				// from there. Failing this, add one millisecond and continue
				// testing.
				if (cronExpression.IsSatisfiedBy(nextIncludedTime))
				{
					nextIncludedTime =
						cronExpression.GetNextValidTimeAfter(
							nextIncludedTime).Value;
				}
				else if ((GetBaseCalendar() != null) &&
				         (!GetBaseCalendar().IsTimeIncluded(nextIncludedTime)))
				{
					nextIncludedTime =
						GetBaseCalendar().GetNextIncludedTimeUtc(nextIncludedTime);
				}
				else
				{
					nextIncludedTime = nextIncludedTime.AddMilliseconds(1);
				}
			}

			return nextIncludedTime;
		}



		/// <summary>
		/// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
		/// </summary>
		/// <returns>
		/// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
		/// </returns>
		public override string ToString()
		{
			StringBuilder buffer = new StringBuilder();
			buffer.Append("base calendar: [");
			if (GetBaseCalendar() != null)
			{
				buffer.Append(GetBaseCalendar().ToString());
			}
			else
			{
				buffer.Append("null");
			}
			buffer.Append("], excluded cron expression: '");
			buffer.Append(cronExpression);
			buffer.Append("'");
			return buffer.ToString();
		}

		/// <summary>
		///  Returns the object representation of the cron expression that defines the
		/// dates and times this calendar excludes.
		/// </summary>
		public CronExpression CronExpression
		{
			get { return cronExpression; }
			set
			{
				if (value == null)
				{
					throw new ArgumentException("expression cannot be null");
				}

				cronExpression = value;
			}
		}


		/// <summary>
		/// Sets the cron expression for the calendar to a new value.
		/// </summary>
		/// <param name="expression">The expression.</param>
		public void SetCronExpressionString(string expression)
		{
			CronExpression newExp = new CronExpression(expression);
			cronExpression = newExp;
		}


        public override int GetHashCode()
        {
            int baseHash = 0;
            if (GetBaseCalendar() != null)
                baseHash = GetBaseCalendar().GetHashCode();

            return CronExpression.GetHashCode() + 5 * baseHash;
        }

        public bool Equals(CronCalendar obj)
        {
            if (obj == null)
                return false;
            bool baseEqual = GetBaseCalendar() != null ?
                             GetBaseCalendar().Equals(obj.GetBaseCalendar()) : true;
            
            return baseEqual && (CronExpression.Equals(obj.CronExpression));
        }


        public override bool Equals(object obj)
        {
            if ((obj == null) || !(obj is CronCalendar))
                return false;
            else
                return Equals((CronCalendar)obj);

        }

	}
}
