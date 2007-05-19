using System;
using System.Text;

namespace Quartz.Impl.Calendar
{
	/// <summary>
	/// This implementation of the Calendar excludes (or includes - see below) a 
	/// specified time range each day. For example, you could use this calendar to 
	/// exclude business hours (8AM - 5PM) every day. Each <CODE>DailyCalendar</CODE>
	/// only allows a single time range to be specified, and that time range may not
	///* cross daily boundaries (i.e. you cannot specify a time range from 8PM - 5AM).
	/// If the property <CODE>invertTimeRange</CODE> is <CODE>false</CODE> (default), 
	/// the time range defines a range of times in which triggers are not allowed to
	///* fire. If <CODE>invertTimeRange</CODE> is <CODE>true</CODE>, the time range
	/// is inverted &ndash; that is, all times <I>outside</I> the defined time range
	/// are excluded.
	/// <P>
	/// Note when using <CODE>DailyCalendar</CODE>, it behaves on the same principals
	/// as, for example, WeeklyCalendar defines a set of days that are
	/// excluded <I>every week</I>. Likewise, <CODE>DailyCalendar</CODE> defines a 
	/// set of times that are excluded <I>every day</I>.
	/// </summary>
	/// <author>Mike Funk</author>
	/// <author>Aaron Craven</author>
	public class DailyCalendar : BaseCalendar 
	{
								  
		private static readonly string invalidHourOfDay = "Invalid hour of day: ";
		private static readonly string invalidMinute = "Invalid minute: ";
		private static readonly string invalidSecond = "Invalid second: ";
		private static readonly string invalidMillis = "Invalid millis: ";
		private static readonly string invalidTimeRange = "Invalid time range: ";
		private static readonly string separator = " - ";
		private static readonly long oneMillis = 1;
		private static readonly char colon = ':';

		private string name;
		private int rangeStartingHourOfDay;
		private int rangeStartingMinute;
		private int rangeStartingSecond;
		private int rangeStartingMillis;
		private int rangeEndingHourOfDay;
		private int rangeEndingMinute;
		private int rangeEndingSecond;
		private int rangeEndingMillis;
    
		private bool invertTimeRange = false;

		/**
		 * Create a <CODE>DailyCalendar</CODE> with a time range defined by the
		 * specified strings and no <CODE>baseCalendar</CODE>. 
		 * <CODE>rangeStartingTime</CODE> and <CODE>rangeEndingTime</CODE>
		 * must be in the format &quot;HH:MM[:SS[:mmm]]&quot; where:
		 * <UL><LI>HH is the hour of the specified time. The hour should be
		 *         specified using military (24-hour) time and must be in the range
		 *         0 to 23.</LI>
		 *     <LI>MM is the minute of the specified time and must be in the range
		 *         0 to 59.</LI>
		 *     <LI>SS is the second of the specified time and must be in the range
		 *         0 to 59.</LI>
		 *     <LI>mmm is the millisecond of the specified time and must be in the
		 *         range 0 to 999.</LI>
		 *     <LI>items enclosed in brackets ('[', ']') are optional.</LI>
		 *     <LI>The time range starting time must be before the time range ending
		 *         time. Note this means that a time range may not cross daily 
		 *         boundaries (10PM - 2AM)</LI>  
		 * </UL>
		 *  
		 * @param name              the name for the <CODE>DailyCalendar</CODE>
		 * @param rangeStartingTime a String representing the starting time for the
		 *                          time range
		 * @param rangeEndingTime   a String representing the ending time for the
		 *                          the time range
		 */
		public DailyCalendar(string name,
			string rangeStartingTime,
			string rangeEndingTime) : base()
		{
			this.name = name;
			SetTimeRange(rangeStartingTime, rangeEndingTime);
		}

		/**
		 * Create a <CODE>DailyCalendar</CODE> with a time range defined by the
		 * specified strings and the specified <CODE>baseCalendar</CODE>. 
		 * <CODE>rangeStartingTime</CODE> and <CODE>rangeEndingTime</CODE>
		 * must be in the format &quot;HH:MM[:SS[:mmm]]&quot; where:
		 * <UL><LI>HH is the hour of the specified time. The hour should be
		 *         specified using military (24-hour) time and must be in the range
		 *         0 to 23.</LI>
		 *     <LI>MM is the minute of the specified time and must be in the range
		 *         0 to 59.</LI>
		 *     <LI>SS is the second of the specified time and must be in the range
		 *         0 to 59.</LI>
		 *     <LI>mmm is the millisecond of the specified time and must be in the
		 *         range 0 to 999.</LI>
		 *     <LI>items enclosed in brackets ('[', ']') are optional.</LI>
		 *     <LI>The time range starting time must be before the time range ending
		 *         time. Note this means that a time range may not cross daily 
		 *         boundaries (10PM - 2AM)</LI>  
		 * </UL>
		 * 
		 * @param name              the name for the <CODE>DailyCalendar</CODE>
		 * @param baseCalendar      the base calendar for this calendar instance
		 *                          &ndash; see {@link BaseCalendar} for more
		 *                          information on base calendar functionality
		 * @param rangeStartingTime a String representing the starting time for the
		 *                          time range
		 * @param rangeEndingTime   a String representing the ending time for the
		 *                          time range
		 */
		public DailyCalendar(string name,
			ICalendar baseCalendar,
			string rangeStartingTime,
			string rangeEndingTime) : base(baseCalendar)
		{
			this.name = name;
			SetTimeRange(rangeStartingTime, rangeEndingTime);
		}

		/**
		 * Create a <CODE>DailyCalendar</CODE> with a time range defined by the
		 * specified values and no <CODE>baseCalendar</CODE>. Values are subject to
		 * the following validations:
		 * <UL><LI>Hours must be in the range 0-23 and are expressed using military
		 *         (24-hour) time.</LI>
		 *     <LI>Minutes must be in the range 0-59</LI>
		 *     <LI>Seconds must be in the range 0-59</LI>
		 *     <LI>Milliseconds must be in the range 0-999</LI>
		 *     <LI>The time range starting time must be before the time range ending
		 *         time. Note this means that a time range may not cross daily 
		 *         boundaries (10PM - 2AM)</LI>  
		 * </UL>
		 * 
		 * @param name                   the name for the <CODE>DailyCalendar</CODE>
		 * @param rangeStartingHourOfDay the hour of the start of the time range
		 * @param rangeStartingMinute    the minute of the start of the time range
		 * @param rangeStartingSecond    the second of the start of the time range
		 * @param rangeStartingMillis    the millisecond of the start of the time 
		 *                               range
		 * @param rangeEndingHourOfDay   the hour of the end of the time range
		 * @param rangeEndingMinute      the minute of the end of the time range
		 * @param rangeEndingSecond      the second of the end of the time range
		 * @param rangeEndingMillis      the millisecond of the start of the time 
		 *                               range
		 */
		public DailyCalendar(string name,
			int rangeStartingHourOfDay,
			int rangeStartingMinute,
			int rangeStartingSecond,
			int rangeStartingMillis,
			int rangeEndingHourOfDay,
			int rangeEndingMinute,
			int rangeEndingSecond,
			int rangeEndingMillis) : base()
		{
			this.name = name;
			SetTimeRange(rangeStartingHourOfDay,
				rangeStartingMinute,
				rangeStartingSecond,
				rangeStartingMillis,
				rangeEndingHourOfDay,
				rangeEndingMinute,
				rangeEndingSecond,
				rangeEndingMillis);
		}
    
		/**
		 * Create a <CODE>DailyCalendar</CODE> with a time range defined by the
		 * specified values and the specified <CODE>baseCalendar</CODE>. Values are
		 * subject to the following validations:
		 * <UL><LI>Hours must be in the range 0-23 and are expressed using military
		 *         (24-hour) time.</LI>
		 *     <LI>Minutes must be in the range 0-59</LI>
		 *     <LI>Seconds must be in the range 0-59</LI>
		 *     <LI>Milliseconds must be in the range 0-999</LI>
		 *     <LI>The time range starting time must be before the time range ending
		 *         time. Note this means that a time range may not cross daily
		 *         boundaries (10PM - 2AM)</LI>  
		 * </UL> 
		 * 
		 * @param name                      the name for the 
		 *                                  <CODE>DailyCalendar</CODE>
		 * @param baseCalendar              the base calendar for this calendar
		 *                                  instance &ndash; see 
		 *                                  {@link BaseCalendar} for more 
		 *                                  information on base calendar 
		 *                                  functionality
		 * @param rangeStartingHourOfDay the hour of the start of the time range
		 * @param rangeStartingMinute    the minute of the start of the time range
		 * @param rangeStartingSecond    the second of the start of the time range
		 * @param rangeStartingMillis    the millisecond of the start of the time 
		 *                               range
		 * @param rangeEndingHourOfDay   the hour of the end of the time range
		 * @param rangeEndingMinute      the minute of the end of the time range
		 * @param rangeEndingSecond      the second of the end of the time range
		 * @param rangeEndingMillis      the millisecond of the start of the time 
		 *                               range
		 */
		public DailyCalendar(string name,
			ICalendar baseCalendar,
			int rangeStartingHourOfDay,
			int rangeStartingMinute,
			int rangeStartingSecond,
			int rangeStartingMillis,
			int rangeEndingHourOfDay,
			int rangeEndingMinute,
			int rangeEndingSecond,
			int rangeEndingMillis) : base(baseCalendar)
		{

			this.name = name;
			SetTimeRange(rangeStartingHourOfDay,
				rangeStartingMinute,
				rangeStartingSecond,
				rangeStartingMillis,
				rangeEndingHourOfDay,
				rangeEndingMinute,
				rangeEndingSecond,
				rangeEndingMillis);
		}

		/**
		 * Create a <CODE>DailyCalendar</CODE> with a time range defined by the
		 * specified <CODE>java.util.Calendar</CODE>s and no 
		 * <CODE>baseCalendar</CODE>. The Calendars are subject to the following
		 * considerations:
		 * <UL><LI>Only the time-of-day fields of the specified Calendars will be
		 *         used (the date fields will be ignored)</LI>
		 *     <LI>The starting time must be before the ending time of the defined
		 *         time range. Note this means that a time range may not cross
		 *         daily boundaries (10PM - 2AM). <I>(because only time fields are
		 *         are used, it is possible for two Calendars to represent a valid
		 *         time range and 
		 *         <CODE>rangeStartingCalendar.after(rangeEndingCalendar) == 
		 *         true</CODE>)</I></LI>  
		 * </UL> 
		 * 
		 * @param name                  the name for the <CODE>DailyCalendar</CODE>
		 * @param rangeStartingCalendar a java.util.Calendar representing the 
		 *                              starting time for the time range
		 * @param rangeEndingCalendar   a java.util.Calendar representing the ending
		 *                              time for the time range
		 */
		public DailyCalendar(string name,
			DateTime rangeStartingCalendar,
			DateTime rangeEndingCalendar) : base()
		{
			this.name = name;
			SetTimeRange(rangeStartingCalendar, rangeEndingCalendar);
		}

		/**
		 * Create a <CODE>DailyCalendar</CODE> with a time range defined by the
		 * specified <CODE>java.util.Calendar</CODE>s and the specified 
		 * <CODE>baseCalendar</CODE>. The Calendars are subject to the following
		 * considerations:
		 * <UL><LI>Only the time-of-day fields of the specified Calendars will be
		 *         used (the date fields will be ignored)</LI>
		 *     <LI>The starting time must be before the ending time of the defined
		 *         time range. Note this means that a time range may not cross
		 *         daily boundaries (10PM - 2AM). <I>(because only time fields are
		 *         are used, it is possible for two Calendars to represent a valid
		 *         time range and 
		 *         <CODE>rangeStartingCalendar.after(rangeEndingCalendar) == 
		 *         true</CODE>)</I></LI>  
		 * </UL> 
		 * 
		 * @param name                  the name for the <CODE>DailyCalendar</CODE>
		 * @param baseCalendar          the base calendar for this calendar instance
		 *                              &ndash; see {@link BaseCalendar} for more 
		 *                              information on base calendar functionality
		 * @param rangeStartingCalendar a java.util.Calendar representing the 
		 *                              starting time for the time range
		 * @param rangeEndingCalendar   a java.util.Calendar representing the ending
		 *                              time for the time range
		 */
		public DailyCalendar(string name,
			ICalendar baseCalendar,
			DateTime rangeStartingCalendar,
			DateTime rangeEndingCalendar) : base(baseCalendar)
		{
			
			this.name = name;
			SetTimeRange(rangeStartingCalendar, rangeEndingCalendar);
		}

		/**
		 * Create a <CODE>DailyCalendar</CODE> with a time range defined by the
		 * specified values and no <CODE>baseCalendar</CODE>. The values are 
		 * subject to the following considerations:
		 * <UL><LI>Only the time-of-day portion of the specified values will be
		 *         used</LI>
		 *     <LI>The starting time must be before the ending time of the defined
		 *         time range. Note this means that a time range may not cross
		 *         daily boundaries (10PM - 2AM). <I>(because only time value are
		 *         are used, it is possible for the two values to represent a valid
		 *         time range and <CODE>rangeStartingTime &gt; 
		 *         rangeEndingTime</CODE>)</I></LI>  
		 * </UL> 
		 * 
		 * @param name                      the name for the 
		 *                                  <CODE>DailyCalendar</CODE>
		 * @param rangeStartingTimeInMillis a long representing the starting time 
		 *                                  for the time range
		 * @param rangeEndingTimeInMillis   a long representing the ending time for
		 *                                  the time range
		 */
		public DailyCalendar(string name,
			long rangeStartingTimeInMillis,
			long rangeEndingTimeInMillis) : base()
		{
			this.name = name;
			SetTimeRange(rangeStartingTimeInMillis, 
				rangeEndingTimeInMillis);
		}

		/**
		 * Create a <CODE>DailyCalendar</CODE> with a time range defined by the
		 * specified values and the specified <CODE>baseCalendar</CODE>. The values
		 * are subject to the following considerations:
		 * <UL><LI>Only the time-of-day portion of the specified values will be
		 *         used</LI>
		 *     <LI>The starting time must be before the ending time of the defined
		 *         time range. Note this means that a time range may not cross
		 *         daily boundaries (10PM - 2AM). <I>(because only time value are
		 *         are used, it is possible for the two values to represent a valid
		 *         time range and <CODE>rangeStartingTime &gt; 
		 *         rangeEndingTime</CODE>)</I></LI>  
		 * </UL> 
		 * 
		 * @param name                      the name for the 
		 *                                  <CODE>DailyCalendar</CODE>
		 * @param baseCalendar              the base calendar for this calendar
		 *                                  instance &ndash; see {@link 
		 *                                  BaseCalendar} for more information on 
		 *                                  base calendar functionality
		 * @param rangeStartingTimeInMillis a long representing the starting time 
		 *                                  for the time range
		 * @param rangeEndingTimeInMillis   a long representing the ending time for
		 *                                  the time range
		 */
		public DailyCalendar(string name,
			ICalendar baseCalendar,
			long rangeStartingTimeInMillis,
			long rangeEndingTimeInMillis) : base(baseCalendar)
		{
			
			this.name = name;
			SetTimeRange(rangeStartingTimeInMillis,
				rangeEndingTimeInMillis);
		}

		/**
		 * Returns the name of the <CODE>DailyCalendar</CODE>
		 * 
		 * @return the name of the <CODE>DailyCalendar</CODE>
		 */
		public string getName() 
		{
			return name;
		}

		/**
		 * Determines whether the given time (in milliseconds) is 'included' by the
		 * <CODE>BaseCalendar</CODE>
		 * 
		 * @param timeInMillis the date/time to test
		 * @return a boolean indicating whether the specified time is 'included' by
		 *         the <CODE>BaseCalendar</CODE>
		 */
		public override bool IsTimeIncluded(DateTime time) 
		{        
			if ((GetBaseCalendar() != null) && 
				(GetBaseCalendar().IsTimeIncluded(time) == false)) 
			{
				return false;
			}
        
			DateTime startOfDayInMillis = GetStartOfDay(time);
			DateTime endOfDayInMillis = GetEndOfDay(time);
			DateTime timeRangeStartingTimeInMillis = 
				GetTimeRangeStartingTime(time);
			DateTime timeRangeEndingTimeInMillis = 
				GetTimeRangeEndingTime(time);
			if (!invertTimeRange) 
			{
				if ((time > startOfDayInMillis && 
					time < timeRangeStartingTimeInMillis) ||
					(time > timeRangeEndingTimeInMillis && 
					time < endOfDayInMillis)) 
				{
	        	
					return true;
				} 
				else 
				{
					return false;
				}
			} 
			else 
			{
				if ((time >= timeRangeStartingTimeInMillis) &&
					(time <= timeRangeEndingTimeInMillis)) 
				{
					return true;
				} 
				else 
				{
					return false;
				}
			}
		}

		/**
		 * Determines the next time included by the <CODE>DailyCalendar</CODE>
		 * after the specified time.
		 * 
		 * @param timeInMillis the initial date/time after which to find an 
		 *                     included time
		 * @return the time in milliseconds representing the next time included
		 *         after the specified time.
		 */
		public override DateTime GetNextIncludedTime(DateTime time) 
		{
			DateTime nextIncludedTime = time.AddMilliseconds(oneMillis);
        
			while (!IsTimeIncluded(nextIncludedTime)) 
			{
				if (!invertTimeRange) 
				{
					//If the time is in a range excluded by this calendar, we can
					// move to the end of the excluded time range and continue 
					// testing from there. Otherwise, if nextIncludedTime is 
					// excluded by the baseCalendar, ask it the next time it 
					// includes and begin testing from there. Failing this, add one
					// millisecond and continue testing.
					if ((nextIncludedTime >= 
						GetTimeRangeStartingTime(nextIncludedTime)) && 
						(nextIncludedTime <= 
						GetTimeRangeEndingTime(nextIncludedTime))) 
					{
	        		
						nextIncludedTime = 
							GetTimeRangeEndingTime(nextIncludedTime).AddMilliseconds(oneMillis);
					} 
					else if ((GetBaseCalendar() != null) && 
						(!GetBaseCalendar().IsTimeIncluded(nextIncludedTime)))
					{
						nextIncludedTime = 
							GetBaseCalendar().GetNextIncludedTime(nextIncludedTime);
					} 
					else 
					{
						nextIncludedTime = nextIncludedTime.AddMilliseconds(1);
					}
				} 
				else 
				{
					//If the time is in a range excluded by this calendar, we can
					// move to the end of the excluded time range and continue 
					// testing from there. Otherwise, if nextIncludedTime is 
					// excluded by the baseCalendar, ask it the next time it 
					// includes and begin testing from there. Failing this, add one
					// millisecond and continue testing.
					if (nextIncludedTime < 
						GetTimeRangeStartingTime(nextIncludedTime)) 
					{
						nextIncludedTime = 
							GetTimeRangeStartingTime(nextIncludedTime);
					} 
					else if (nextIncludedTime > 
						GetTimeRangeEndingTime(nextIncludedTime)) 
					{
						//(move to start of next day)
						nextIncludedTime = GetEndOfDay(nextIncludedTime);
						nextIncludedTime = nextIncludedTime.AddMilliseconds(1);
					} 
					else if ((GetBaseCalendar() != null) && 
						(!GetBaseCalendar().IsTimeIncluded(nextIncludedTime)))
					{
						nextIncludedTime = 
							GetBaseCalendar().GetNextIncludedTime(nextIncludedTime);
					} 
					else 
					{
						nextIncludedTime = nextIncludedTime.AddMilliseconds(1);
					}
				}
			}
        
			return nextIncludedTime;
		}

		/**
		 * Returns the start time of the time range (in milliseconds) of the day 
		 * specified in <CODE>timeInMillis</CODE>
		 * 
		 * @param timeInMillis a time containing the desired date for the starting
		 *                     time of the time range.
		 * @return a date/time (in milliseconds) representing the start time of the
		 *         time range for the specified date.
		 */
		public DateTime GetTimeRangeStartingTime(DateTime time) 
		{
			DateTime rangeStartingTime = new DateTime(time.Year, time.Month, time.Day, rangeStartingHourOfDay, rangeStartingMinute, rangeStartingSecond, rangeStartingMillis);
			return rangeStartingTime;
		}

		/**
		 * Returns the end time of the time range (in milliseconds) of the day
		 * specified in <CODE>timeInMillis</CODE>
		 * 
		 * @param timeInMillis a time containing the desired date for the ending
		 *                     time of the time range.
		 * @return a date/time (in milliseconds) representing the end time of the
		 *         time range for the specified date.
		 */
		public DateTime GetTimeRangeEndingTime(DateTime time) 
		{
			DateTime rangeEndingTime = new DateTime(time.Year, time.Month, time.Day, rangeStartingHourOfDay, rangeStartingMinute, rangeStartingSecond, rangeStartingMillis);
			return rangeEndingTime;
		}

		/**
		 * Indicates whether the time range represents an inverted time range (see
		 * class description).
		 * 
		 * @return a boolean indicating whether the time range is inverted
		 */

		public bool InvertTimeRange
		{
			get { return invertTimeRange; }
			set { invertTimeRange = value; }
		}

    
		/**
		 * Returns a string representing the properties of the 
		 * <CODE>DailyCalendar</CODE>
		 * 
		 * @return the properteis of the DailyCalendar in a String format
		 */
		public override string ToString() 
		{
			StringBuilder buffer = new StringBuilder();
			buffer.Append(getName());
			buffer.Append(": base calendar: [");
			if (GetBaseCalendar() != null) 
			{
				buffer.Append(GetBaseCalendar().ToString());
			} 
			else 
			{
				buffer.Append("null");
			}
			// TODO check leading zeroes
			buffer.Append("], time range: '");
			buffer.Append(rangeStartingHourOfDay);
			buffer.Append(":");
			buffer.Append(rangeStartingMinute);
			buffer.Append(":");
			buffer.Append(rangeStartingSecond);
			buffer.Append(":");
			// numberFormatter.setMinimumIntegerDigits(3);
			buffer.Append(rangeStartingMillis);
			// numberFormatter.setMinimumIntegerDigits(2);
			buffer.Append(" - ");
			buffer.Append(rangeEndingHourOfDay);
			buffer.Append(":");
			buffer.Append(rangeEndingMinute);
			buffer.Append(":");
			buffer.Append(rangeEndingSecond);
			buffer.Append(":");
			// numberFormatter.setMinimumIntegerDigits(3);
			buffer.Append(rangeEndingMillis);
			buffer.Append("', inverted: " + 
				invertTimeRange + "]");
			return buffer.ToString();
		}
    
		/**
		 * Sets the time range for the <CODE>DailyCalendar</CODE> to the times 
		 * represented in the specified Strings. 
		 * 
		 * @param rangeStartingTimeString a String representing the start time of 
		 *                                the time range
		 * @param rangeEndingTimeString   a String representing the end time of the
		 *                                excluded time range
		 */
		private void SetTimeRange(string rangeStartingTimeString,
			string rangeEndingTimeString) 
		{
			string[] rangeStartingTime;
			int rangeStartingHourOfDay;
			int rangeStartingMinute;
			int rangeStartingSecond;
			int rangeStartingMillis;
    	
			string[] rangeEndingTime;
			int rangeEndingHourOfDay;
			int rangeEndingMinute;
			int rangeEndingSecond;
			int rangeEndingMillis;
    	
			rangeStartingTime = rangeStartingTimeString.Split(colon);
        
			if ((rangeStartingTime.Length < 2) || (rangeStartingTime.Length > 4)) 
			{
				throw new ArgumentException("Invalid time string '" + 
					rangeStartingTimeString + "'");
			}
        
			rangeStartingHourOfDay = Convert.ToInt32(rangeStartingTime[0]);
			rangeStartingMinute = Convert.ToInt32(rangeStartingTime[1]);
			if (rangeStartingTime.Length > 2) 
			{
				rangeStartingSecond = Convert.ToInt32(rangeStartingTime[2]);
			} 
			else 
			{
				rangeStartingSecond = 0;
			}
			if (rangeStartingTime.Length == 4) 
			{
				rangeStartingMillis = Convert.ToInt32(rangeStartingTime[3]);
			} 
			else 
			{
				rangeStartingMillis = 0;
			}
        
			rangeEndingTime = rangeEndingTimeString.Split(colon);

			if ((rangeEndingTime.Length < 2) || (rangeEndingTime.Length > 4)) 
			{
				throw new ArgumentException("Invalid time string '" + 
					rangeEndingTimeString + "'");
			}
        
			rangeEndingHourOfDay = Convert.ToInt32(rangeEndingTime[0]);
			rangeEndingMinute = Convert.ToInt32(rangeEndingTime[1]);
			if (rangeEndingTime.Length > 2) 
			{
				rangeEndingSecond = Convert.ToInt32(rangeEndingTime[2]);
			} 
			else 
			{
				rangeEndingSecond = 0;
			}
			if (rangeEndingTime.Length == 4) 
			{
				rangeEndingMillis = Convert.ToInt32(rangeEndingTime[3]);
			} 
			else 
			{
				rangeEndingMillis = 0;
			}
        
			SetTimeRange(rangeStartingHourOfDay,
				rangeStartingMinute,
				rangeStartingSecond,
				rangeStartingMillis,
				rangeEndingHourOfDay,
				rangeEndingMinute,
				rangeEndingSecond,
				rangeEndingMillis);
		}

		/**
		 * Sets the time range for the <CODE>DailyCalendar</CODE> to the times
		 * represented in the specified values.  
		 * 
		 * @param rangeStartingHourOfDay the hour of the start of the time range
		 * @param rangeStartingMinute    the minute of the start of the time range
		 * @param rangeStartingSecond    the second of the start of the time range
		 * @param rangeStartingMillis    the millisecond of the start of the time
		 *                               range
		 * @param rangeEndingHourOfDay   the hour of the end of the time range
		 * @param rangeEndingMinute      the minute of the end of the time range
		 * @param rangeEndingSecond      the second of the end of the time range
		 * @param rangeEndingMillis      the millisecond of the start of the time 
		 *                               range
		 */
		private void SetTimeRange(int rangeStartingHourOfDay,
			int rangeStartingMinute,
			int rangeStartingSecond,
			int rangeStartingMillis,
			int rangeEndingHourOfDay,
			int rangeEndingMinute,
			int rangeEndingSecond,
			int rangeEndingMillis) 
		{
			validate(rangeStartingHourOfDay,
				rangeStartingMinute,
				rangeStartingSecond,
				rangeStartingMillis);
        
			validate(rangeEndingHourOfDay,
				rangeEndingMinute,
				rangeEndingSecond,
				rangeEndingMillis);
        
			DateTime startCal = DateTime.Now;
			startCal = new DateTime(startCal.Year, startCal.Month, startCal.Day, rangeStartingHourOfDay, rangeStartingMinute, rangeStartingSecond, rangeStartingMillis);
        
			DateTime endCal = DateTime.Now;
			endCal = new DateTime(endCal.Year, endCal.Month, endCal.Day, rangeEndingHourOfDay, rangeEndingMinute, rangeEndingSecond, rangeEndingMillis);

        
			if (! (startCal < endCal)) 
			{
				throw new ArgumentException(invalidTimeRange +
					rangeStartingHourOfDay + ":" +
					rangeStartingMinute + ":" +
					rangeStartingSecond + ":" +
					rangeStartingMillis + separator +
					rangeEndingHourOfDay + ":" +
					rangeEndingMinute + ":" +
					rangeEndingSecond + ":" +
					rangeEndingMillis);
			}
        
			this.rangeStartingHourOfDay = rangeStartingHourOfDay;
			this.rangeStartingMinute = rangeStartingMinute;
			this.rangeStartingSecond = rangeStartingSecond;
			this.rangeStartingMillis = rangeStartingMillis;
			this.rangeEndingHourOfDay = rangeEndingHourOfDay;
			this.rangeEndingMinute = rangeEndingMinute;
			this.rangeEndingSecond = rangeEndingSecond;
			this.rangeEndingMillis = rangeEndingMillis;
		}
    
		/**
		 * Sets the time range for the <CODE>DailyCalendar</CODE> to the times
		 * represented in the specified <CODE>java.util.Calendar</CODE>s. 
		 * 
		 * @param rangeStartingCalendar a Calendar containing the start time for
		 *                              the <CODE>DailyCalendar</CODE>
		 * @param rangeEndingCalendar   a Calendar containing the end time for
		 *                              the <CODE>DailyCalendar</CODE>
		 */
		private void SetTimeRange(DateTime rangeStartingCalendar,
			DateTime rangeEndingCalendar) 
		{
			SetTimeRange(
				rangeStartingCalendar.Hour,
				rangeStartingCalendar.Minute,
				rangeStartingCalendar.Second,
				rangeStartingCalendar.Millisecond,
				rangeEndingCalendar.Hour,
				rangeEndingCalendar.Minute,
				rangeEndingCalendar.Second,
				rangeEndingCalendar.Millisecond);
		}
    
		/**
		 * Sets the time range for the <CODE>DailyCalendar</CODE> to the times
		 * represented in the specified values. 
		 * 
		 * @param rangeStartingTime the starting time (in milliseconds) for the
		 *                          time range
		 * @param rangeEndingTime   the ending time (in milliseconds) for the time
		 *                          range
		 */
		private void SetTimeRange(long rangeStartingTime, 
			long rangeEndingTime) 
		{
    	
			SetTimeRange(new DateTime(rangeStartingTime), new DateTime(rangeEndingTime));
		}
    
		/**
		 * Returns the start of the given day in milliseconds
		 * 
		 * @param timeInMillis a time containing the desired date for the 
		 *                     start-of-day time.
		 * @return the start of the given day in milliseconds
		 */
		private DateTime GetStartOfDay(DateTime time) 
		{
			return time.Date;
		}

		/**
		 * Returns the end of the given day in milliseconds
		 * 
		 * @param timeInMillis a time containing the desired date for the 
		 *                     end-of-day time.
		 * @return the end of the given day in milliseconds
		 */
		private DateTime GetEndOfDay(DateTime time) 
		{
			DateTime endOfDay = new DateTime(time.Year, time.Month, time.Day, 23, 59, 59, 999);
			return endOfDay;
		}
    
		/**
		 * Checks the specified values for validity as a set of time values.
		 * 
		 * @param hourOfDay the hour of the time to check (in military (24-hour)
		 *                  time)
		 * @param minute    the minute of the time to check
		 * @param second    the second of the time to check
		 * @param millis    the millisecond of the time to check
		 */
		private void validate(int hourOfDay, int minute, int second, int millis) 
		{
			if (hourOfDay < 0 || hourOfDay > 23) 
			{
				throw new ArgumentException(invalidHourOfDay + hourOfDay);
			}
			if (minute < 0 || minute > 59) 
			{
				throw new ArgumentException(invalidMinute + minute);
			}
			if (second < 0 || second > 59) 
			{
				throw new ArgumentException(invalidSecond + second);
			}
			if (millis < 0 || millis > 999) 
			{
				throw new ArgumentException(invalidMillis + millis);
			}
		}
	}
}
