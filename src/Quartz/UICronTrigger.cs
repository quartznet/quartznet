/* 
* Copyright 2004-2005 the original author or authors. 
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
using System;
using System.Collections;
using System.Globalization;
using System.Text;

using Nullables;

using Quartz.Collection;

namespace Quartz
{
	/// <summary>
	/// A concrete <code>Trigger</code> that is used to fire a <code>Job</code>
	/// at given moments in time, defined with Unix 'cron-like' definitions.
	/// <p>
	/// What you should know about this particular trigger is that it is based on
	/// CronTrigger, but the the functionality to build the sets from a
	/// string are unused. Whereas CronTrigger would essentially deserialize by
	/// rebuilding the TreeSets from the cronExpression, this class does not have a
	/// cronExpression, and de/serializes the TreeSets in their entirety. This is
	/// because the TreeSets map directly to the Struts user interface for set
	/// selection, and no effort is made to write an interpreter to map them back
	/// and forth between legacy UN*X cron expressions that CronTrigger uses.
	/// </p>
	/// <p>
	/// The method I use with this trigger is to instantiate it, then put it in the
	/// ActionForm of a Struts bean, then let Struts manipulate it directly through
	/// BeanUtils. You are by no means required to do that, but to fully understand
	/// the concepts here, at least until there is better documentation, you should
	/// understand how it works within that context first so you can write the
	/// appropriate code that Struts does for you for free. I'll try to explain that
	/// here.
	/// </p>
	/// <p>
	/// Struts JSP tags allow the user to use Apache BeanUtils to reference
	/// components of beans by path. This is to say that a bean <code>Foo</code>
	/// that has an accessor method <code>Bar getBar()</code> and given <code>
	/// Bar</code>
	/// has a primitive type <code>String</code> as a field named <code>splat</code>,
	/// one can set the field to <i>"new string value"</i> as follows:
	/// </p>
	/// <code>
	/// // create a new Foo with contained reference to a new Bar
	/// Foo fooBean = new Foo();
	/// fooBean.setBar(new Bar());
	/// // set the splat string in the Bar bean from Foo
	/// BeanUtils.setProperty(fooBean, "bar.splat", "new string value");
	/// </code>
	/// <p>
	/// In turn, Struts JSP tags use the bean addressing provided by BeanUtils to
	/// address accessor methods within the bean graph that is rooted with the
	/// ActionForm that is put into the Action context.
	/// </p>
	/// <p>
	/// Finally, having all this allows you to put direct selection lists on the
	/// screen of the UI, then map them directly into the UICronTrigger bean. Given
	/// a ActionForm bean that was set up to contain a <code>UICronTrigger</code>
	/// in a field called <code>trigger</code>, the following HTML code will
	/// completely create your UI in Struts:
	/// </p>
	/// 
	/// <code>
	/// <tr class="listRowUnshaded">
	/// <td width="80">Date</td>
	/// <td align="right">
	/// <html:select property="trigger.daysOfWeek" size="5" multiple="true">
	/// <html:options property="trigger.daysOfWeekValues" labelProperty="trigger.daysOfWeekLabels"/>
	/// </html:select>
	/// <html:select property="trigger.daysOfMonth" size="5" multiple="true">
	/// <html:options property="trigger.daysOfMonthValues" labelProperty="trigger.daysOfMonthLabels"/>
	/// </html:select>
	/// <html:select property="trigger.months" size="5" multiple="true">
	/// <html:options property="trigger.monthsValues" labelProperty="trigger.monthsLabels"/>
	/// </html:select>
	/// <html:select property="trigger.years" size="5" multiple="true">
	/// <html:options property="trigger.yearsValues" labelProperty="trigger.yearsLabels"/>
	/// </html:select>
	/// </td>
	/// </tr>
	/// <tr class="listRowShaded">
	/// <td width="80">Time</td>
	/// <td colspan="2" align="right">
	/// <html:select property="trigger.hours" size="5" multiple="true">
	/// <html:options property="trigger.hoursValues" labelProperty="trigger.hoursLabels"/>
	/// </html:select>
	/// <html:select property="trigger.minutes" size="5" multiple="true">
	/// <html:options property="trigger.minutesValues" labelProperty="trigger.minutesLabels"/>
	/// </html:select>
	/// </td>
	/// </tr>
	/// </code>
	/// <p>
	/// So if you don't want to use Struts, what you have to do is take the
	/// information that was submitted on the form in the HTML select ranges,
	/// iterate each of them, and add the values to the appropriate sets in the
	/// fields of this class. Make sense?
	/// </p>
	/// 
	/// Note that this is not as versatile as the standard CronTrigger. There are
	/// tricks with "last day of month" and repeating sets that need to be manually
	/// selected, and sets that can happen for date ranges much longer than we can
	/// reasonably map with direct selection in a UI.
	/// 
	/// </summary>
	/// <seealso cref="org.quartz.CronTrigger">
	/// </seealso>
	/// <seealso cref="Trigger">
	/// </seealso>
	/// <seealso cref="SimpleTrigger">
	/// 
	/// </seealso>
	/// <author>  Brian Topping
	/// </author>
	/// <author>  based on code by Sharada Jambula, James House, Mads Henderson
	/// </author>
	/// <deprecated>
	/// </deprecated>
	[Serializable]
	public class UICronTrigger : Trigger
	{
		/// <summary>
		/// Gets or sets the time zone for which the <code>cronExpression</code> of
		/// this <code>CronTrigger</code> will be resolved.
		/// </summary>
		public virtual TimeZone TimeZone
		{
			get { return timeZone; }

			set { timeZone = value; }
		}

		/// <summary>
		/// Returns the final time at which the <code>CronTrigger</code> will
		/// fire.
		/// <p>
		/// Note that the return time *may* be in the past. and the date returned is
		/// not validated against org.quartz.calendar
		/// </p>
		/// </summary>
		public override NullableDateTime FinalFireTime
		{
			get
			{
				if (EndTime.HasValue)
				{
					return GetTimeBefore( /*endTime*/);
				}
				else
				{
					return NullableDateTime.Default;
				}
			}
		}

		public virtual string ExpressionSummary
		{
			get
			{
				StringBuilder buf = new StringBuilder();

				buf.Append("seconds: ");
				buf.Append(getExpressionSetSummary(seconds));
				buf.Append("\n");
				buf.Append("minutes: ");
				buf.Append(getExpressionSetSummary(minutes));
				buf.Append("\n");
				buf.Append("hours: ");
				buf.Append(getExpressionSetSummary(hours));
				buf.Append("\n");
				buf.Append("daysOfMonth: ");
				buf.Append(getExpressionSetSummary(daysOfMonth));
				buf.Append("\n");
				buf.Append("months: ");
				buf.Append(getExpressionSetSummary(months));
				buf.Append("\n");
				buf.Append("daysOfWeek: ");
				buf.Append(getExpressionSetSummary(daysOfWeek));
				buf.Append("\n");
				buf.Append("lastdayOfWeek: ");
				buf.Append(lastdayOfWeek);
				buf.Append("\n");
				buf.Append("lastdayOfMonth: ");
				buf.Append(lastdayOfMonth);
				buf.Append("\n");
				buf.Append("calendardayOfWeek: ");
				buf.Append(calendardayOfWeek);
				buf.Append("\n");
				buf.Append("calendardayOfMonth: ");
				buf.Append(calendardayOfMonth);
				buf.Append("\n");
				buf.Append("years: ");
				buf.Append(getExpressionSetSummary(years));
				buf.Append("\n");

				return buf.ToString();
			}
		}

		public virtual bool LeapYear
		{
			get
			{
				return DateTime.IsLeapYear(DateTime.Now.Year);
			}
		}

		public virtual Int32[] SecondsValues
		{
			get
			{
				Int32[] list = new Int32[60];
				for (int i = 0; i < 60; i++)
				{
					list[i] = i;
				}

				return list;
			}
		}

		public virtual Int32[] SecondsLabels
		{
			get { return SecondsValues; }
		}

		public virtual Int32[] Seconds
		{
			get
			{
				Int32[] list = new Int32[seconds.Count];
				if (seconds != null)
				{
					int i = 0;
					//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
					for (IEnumerator it = seconds.GetEnumerator(); it.MoveNext(); i++)
					{
						//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
						list[i] = (Int32) it.Current;
					}
				}
				return list;
			}

			set
			{
				if (seconds != null)
				{
					seconds.Clear();
				}
				else
				{
					//UPGRADE_TODO: Class 'java.util.TreeSet' was converted to 'TreeSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilTreeSet_3"'
					seconds = new TreeSet();
				}

				for (int i = 0; i < value.Length; i++)
				{
					seconds.Add(value[i]);
				}
			}
		}

		public virtual Int32[] MinutesValues
		{
			get
			{
				Int32[] list = new Int32[60];
				for (int i = 0; i < 60; i++)
				{
					list[i] = i;
				}

				return list;
			}
		}

		public virtual Int32[] MinutesLabels
		{
			get { return MinutesValues; }
		}

		public virtual Int32[] Minutes
		{
			get
			{
				Int32[] list = new Int32[minutes.Count];
				if (minutes != null)
				{
					int i = 0;
					//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
					for (IEnumerator it = minutes.GetEnumerator(); it.MoveNext(); i++)
					{
						//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
						list[i] = (Int32) it.Current;
					}
				}
				return list;
			}

			set
			{
				if (minutes != null)
				{
					minutes.Clear();
				}
				else
				{
					//UPGRADE_TODO: Class 'java.util.TreeSet' was converted to 'TreeSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilTreeSet_3"'
					minutes = new TreeSet();
				}

				for (int i = 0; i < value.Length; i++)
				{
					minutes.Add(value[i]);
				}
			}
		}

		public virtual Int32[] HoursValues
		{
			get
			{
				Int32[] list = new Int32[24];
				for (int i = 0; i < 24; i++)
				{
					list[i] = i;
				}

				return list;
			}
		}

		public virtual string[] HoursLabels
		{
			get
			{
				string[] vals =
					new string[]
						{
							"12AM (Midnight)", "1AM", "2AM", "3AM", "4AM", "5AM", "6AM", "7AM", "8AM", "9AM", "10AM", "11AM", "12PM (Noon)",
							"1PM", "2PM", "3PM", "4PM", "5PM", "6PM", "7PM", "8PM", "9PM", "10PM", "11PM"
						};
				return vals;
			}
		}

		public virtual Int32[] Hours
		{
			get
			{
				Int32[] list = new Int32[hours.Count];
				if (hours != null)
				{
					int i = 0;
					//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
					for (IEnumerator it = hours.GetEnumerator(); it.MoveNext(); i++)
					{
						//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
						list[i] = (Int32) it.Current;
					}
				}
				return list;
			}

			set
			{
				if (hours != null)
				{
					hours.Clear();
				}
				else
				{
					//UPGRADE_TODO: Class 'java.util.TreeSet' was converted to 'TreeSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilTreeSet_3"'
					hours = new TreeSet();
				}

				for (int i = 0; i < value.Length; i++)
				{
					hours.Add(value[i]);
				}
			}
		}

		public virtual Int32[] DaysOfMonthValues
		{
			get
			{
				Int32[] list = new Int32[31];
				for (int i = 0; i < 31; i++)
				{
					list[i] = i + 1;
				}

				return list;
			}
		}

		public virtual Int32[] DaysOfMonthLabels
		{
			get { return DaysOfMonthValues; }
		}

		public virtual Int32[] DaysOfMonth
		{
			get
			{
				Int32[] list = new Int32[daysOfMonth.Count];
				if (daysOfMonth != null)
				{
					int i = 0;
					//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
					for (IEnumerator it = daysOfMonth.GetEnumerator(); it.MoveNext(); i++)
					{
						//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
						list[i] = (Int32) it.Current;
					}
				}
				return list;
			}

			set
			{
				if (daysOfMonth != null)
				{
					daysOfMonth.Clear();
				}
				else
				{
					//UPGRADE_TODO: Class 'java.util.TreeSet' was converted to 'TreeSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilTreeSet_3"'
					daysOfMonth = new TreeSet();
				}

				for (int i = 0; i < value.Length; i++)
				{
					daysOfMonth.Add(value[i]);
				}
				daysOfWeek.Clear();
				daysOfWeek.Add(NO_SPEC);
			}
		}

		public virtual Int32[] MonthsValues
		{
			get
			{
				Int32[] list = new Int32[12];
				for (int i = 0; i < 12; i++)
				{
					list[i] = i + 1;
				}

				return list;
			}
		}

		public virtual string[] MonthsLabels
		{
			get
			{
				string[] vals =
					new string[]
						{
							"January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November",
							"December"
						};
				return vals;
			}
		}

		public virtual Int32[] Months
		{
			get
			{
				Int32[] list = new Int32[months.Count];
				if (months != null)
				{
					int i = 0;
					//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
					for (IEnumerator it = months.GetEnumerator(); it.MoveNext(); i++)
					{
						//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
						list[i] = (Int32) it.Current;
					}
				}
				return list;
			}

			set
			{
				if (months != null)
				{
					months.Clear();
				}
				else
				{
					//UPGRADE_TODO: Class 'java.util.TreeSet' was converted to 'TreeSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilTreeSet_3"'
					months = new TreeSet();
				}

				for (int i = 0; i < value.Length; i++)
				{
					months.Add(value[i]);
				}
			}
		}

		public virtual string[] DaysOfWeekLabels
		{
			get
			{
				string[] list = new string[] {"Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"};
				return list;
			}
		}

		public virtual Int32[] DaysOfWeekValues
		{
			get
			{
				Int32[] list = new Int32[7];
				for (int i = 0; i < 7; i++)
				{
					list[i] = i + 1;
				}
				return list;
			}
		}

		public virtual Int32[] DaysOfWeek
		{
			get
			{
				Int32[] list = new Int32[daysOfWeek.Count];
				if (daysOfWeek != null)
				{
					int i = 0;
					//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
					for (IEnumerator it = daysOfWeek.GetEnumerator(); it.MoveNext(); i++)
					{
						//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
						list[i] = (Int32) it.Current;
					}
				}
				return list;
			}

			set
			{
				if (daysOfWeek != null)
				{
					daysOfWeek.Clear();
				}
				else
				{
					//UPGRADE_TODO: Class 'java.util.TreeSet' was converted to 'TreeSetSupport' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilTreeSet_3"'
					daysOfWeek = new TreeSet();
				}

				for (int i = 0; i < value.Length; i++)
				{
					daysOfWeek.Add(value[i]);
				}

				daysOfMonth.Clear();
				daysOfMonth.Add(NO_SPEC);
			}
		}

		public virtual Int32[] YearsValues
		{
			get
			{
				Int32[] list = new Int32[20];
				int year = DateTime.Now.Year;
				for (int i = 0; i < 20; i++)
				{
					list[i] = i + year;
				}

				return list;
			}
		}

		public virtual Int32[] YearsLabels
		{
			get { return YearsValues; }
		}

		public virtual Int32[] Years
		{
			get
			{
				Int32[] list = new Int32[years.Count];
				if (years != null)
				{
					int i = 0;
					//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
					for (IEnumerator it = years.GetEnumerator(); it.MoveNext(); i++)
					{
						//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
						list[i] = (Int32) it.Current;
					}
				}
				return list;
			}

			set
			{
				if (years != null)
				{
					years.Clear();
				}
				else
				{
					years = new TreeSet();
				}

				for (int i = 0; i < value.Length; i++)
				{
					years.Add(value[i]);
				}
			}
		}

		/// <summary>
		/// Tells whether this Trigger instance can handle events
		/// in millisecond precision.
		/// </summary>
		/// <value></value>
		public override bool HasMillisecondPrecision
		{
			get { return false; }
		}

		/// <summary> <p>
		/// Instructs the <code>{@link Scheduler}</code> that upon a mis-fire
		/// situation, the <code>{@link org.quartz.CronTrigger}</code> wants to be
		/// fired now by <code>Scheduler</code>.
		/// </p>
		/// </summary>
		public const int MISFIRE_INSTRUCTION_FIRE_ONCE_NOW = 1;

		/// <summary> <p>
		/// Instructs the <code>{@link Scheduler}</code> that upon a mis-fire
		/// situation, the <code>{@link org.quartz.CronTrigger}</code> wants to
		/// have it's next-fire-time updated to the next time in the schedule after
		/// the current time, but it does not to be fired now.
		/// </p>
		/// </summary>
		public const int MISFIRE_INSTRUCTION_DO_NOTHING = 2;

		private const int ALL_SPEC_INT = 99; // '*'
		private const int NO_SPEC_INT = 98; // '?'
		private static readonly int ALL_SPEC = ALL_SPEC_INT;
		private static readonly int NO_SPEC = NO_SPEC_INT;
		private static IDictionary monthMap = new Hashtable(20);
		private static IDictionary dayMap = new Hashtable(60);
		private NullableDateTime nextFireTime = null;
		private TimeZone timeZone = null;
		private NullableDateTime previousFireTime = null;

		private TreeSet seconds = null;
		private TreeSet minutes = null;
		private TreeSet hours = null;
		private TreeSet daysOfMonth = null;
		private TreeSet months = null;
		private TreeSet daysOfWeek = null;
		private TreeSet years = null;

		[NonSerialized] 
		private bool lastdayOfWeek = false;
		[NonSerialized] 
		private int nthdayOfWeek = 0;
		[NonSerialized] 
		private bool lastdayOfMonth = false;
		[NonSerialized] 
		private bool calendardayOfWeek = false;
		[NonSerialized] 
		private bool calendardayOfMonth = false;

		public void Reset()
		{
			seconds = new TreeSet();
			minutes = new TreeSet();
			hours = new TreeSet();
			daysOfMonth = new TreeSet();
			months = new TreeSet();
			daysOfWeek = new TreeSet();
			years = new TreeSet();

			// we always fire on the minute
			seconds.Add(0);

			minutes.Add(ALL_SPEC);
			for (int i = 0; i < 60; i++)
			{
				minutes.Add(i);
			}

			hours.Add(ALL_SPEC);
			for (int i = 0; i < 24; i++)
			{
				hours.Add(i);
			}

			daysOfMonth.Add(ALL_SPEC);
			for (int i = 1; i <= 31; i++)
			{
				daysOfMonth.Add(i);
			}

			months.Add(ALL_SPEC);
			for (int i = 1; i <= 12; i++)
			{
				months.Add(i);
			}

			daysOfWeek.Add(NO_SPEC);

			years.Add(ALL_SPEC);
			for (int i = 1970; i <= 2099; i++)
			{
				years.Add(i);
			}

			StartTime = DateTime.Now;
			TimeZone = TimeZone.CurrentTimeZone;
		}

		/// <summary> <p>
		/// Create a <code>CronTrigger</code> with no settings.
		/// </p>
		/// </summary>
		public UICronTrigger() : base()
		{
			Reset();
		}

		/// <summary> <p>
		/// Create a <code>CronTrigger</code> with the given name and group.
		/// </p>
		/// </summary>
		public UICronTrigger(string name, string group) : base(name, group)
		{
			Reset();
		}

		/// <summary> <p>
		/// Create a <code>CronTrigger</code> with the given name and group, and
		/// associated with the identified <code>{@link Job}</code>.
		/// </p>
		/// </summary>
		public UICronTrigger(string name, string group, string jobName, string jobGroup)
			: base(name, group, jobName, jobGroup)
		{
			Reset();
		}

		/// <summary> <p>
		/// Returns the next time at which the <code>CronTrigger</code> will fire.
		/// If the trigger will not fire again, <code>null</code> will be
		/// returned. The value returned is not guaranteed to be valid until after
		/// the <code>Trigger</code> has been added to the scheduler.
		/// </p>
		/// </summary>
		public override NullableDateTime GetNextFireTime()
		{
			return nextFireTime;
		}

		public override void UpdateAfterMisfire(ICalendar cal)
		{
			int instr = MisfireInstruction;

			if (instr == MISFIRE_INSTRUCTION_SMART_POLICY)
			{
				instr = MISFIRE_INSTRUCTION_DO_NOTHING;
			}

			if (instr == MISFIRE_INSTRUCTION_DO_NOTHING)
			{
				DateTime tempAux = DateTime.Now;

				NullableDateTime newFireTime = GetFireTimeAfter(tempAux);

				while (newFireTime != null && newFireTime.HasValue && cal != null && !cal.IsTimeIncluded(newFireTime.Value))
				{
					//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
					newFireTime = GetFireTimeAfter(newFireTime);
				}

				//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
				SetNextFireTime(newFireTime);
			}
			else if (instr == MISFIRE_INSTRUCTION_FIRE_ONCE_NOW)
			{
				DateTime tempAux2 = DateTime.Now;
				//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
				SetNextFireTime(tempAux2);
			}
		}

		public override NullableDateTime GetPreviousFireTime()
		{
			return previousFireTime;
		}

		/// <summary> <p>
		/// Set the previous time at which the <code>SimpleTrigger</code> fired.
		/// </p>
		/// 
		/// <p>
		/// <b>This method should not be invoked by client code.</b>
		/// </p>
		/// </summary>
		public void SetPreviousFireTime(NullableDateTime previousFire)
		{
			previousFireTime = previousFire;
		}

		/// <summary> <p>
		/// Sets the next time at which the <code>CronTrigger</code> will fire. If
		/// the trigger will not fire again, <code>null</code> will be returned.
		/// </p>
		/// </summary>
		public virtual void SetNextFireTime(NullableDateTime nextFire)
		{
			nextFireTime = nextFire;
		}

		/// <summary> <p>
		/// Returns the next time at which the <code>CronTrigger</code> will fire,
		/// after the given time. If the trigger will not fire after the given time,
		/// <code>null</code> will be returned.
		/// </p>
		/// 
		/// <p>
		/// Note that the date returned is NOT validated against the related
		/// org.quartz.Calendar (if any)
		/// </p>
		/// </summary>
		public override NullableDateTime GetFireTimeAfter(NullableDateTime afterTime)
		{
			if (!afterTime.HasValue)
			{
				afterTime = DateTime.Now;
			}

			if (StartTime > afterTime.Value)
			{
				afterTime = afterTime.Value.AddMilliseconds(1000);
			}

			NullableDateTime pot = GetTimeAfter(afterTime);
			if (EndTime.HasValue && pot != null && pot.HasValue && (pot.Value > EndTime.Value))
			{
				return NullableDateTime.Default;
			}

			return pot;
		}

		/// <summary> <p>
		/// Determines whether or not the <code>CronTrigger</code> will occur
		/// again.
		/// </p>
		/// </summary>
		public override bool MayFireAgain()
		{
			//UPGRADE_TODO: The 'DateTime' structure does not have an equivalent to NULL. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1291_3"'
			return (GetNextFireTime() != null);
		}

		protected override bool ValidateMisfireInstruction(int misfireInstruction)
		{
			if (misfireInstruction < MISFIRE_INSTRUCTION_SMART_POLICY)
			{
				return false;
			}

			if (misfireInstruction > MISFIRE_INSTRUCTION_DO_NOTHING)
			{
				return false;
			}

			return true;
		}

		/// <summary> <p>
		/// Updates the <code>CronTrigger</code>'s state based on the
		/// MISFIRE_INSTRUCTION_XXX that was selected when the <code>SimpleTrigger</code>
		/// was created.
		/// </p>
		/// 
		/// <p>
		/// If the misfire instruction is set to MISFIRE_INSTRUCTION_SMART_POLICY,
		/// then the following scheme will be used: <br>
		/// <ul>
		/// <li>The instruction will be interpreted as <code>MISFIRE_INSTRUCTION_DO_NOTHING</code>
		/// </ul>
		/// </p>
		/// </summary>
		public virtual void UpdateAfterMisfire()
		{
			int instr = MisfireInstruction;

			if (instr == MISFIRE_INSTRUCTION_SMART_POLICY)
			{
				instr = MISFIRE_INSTRUCTION_DO_NOTHING;
			}

			if (instr == MISFIRE_INSTRUCTION_DO_NOTHING)
			{
				DateTime tempAux = DateTime.Now;
				//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
				NullableDateTime tempAux2 = GetFireTimeAfter(tempAux);
				SetNextFireTime(tempAux2);
			}
			else if (instr == MISFIRE_INSTRUCTION_FIRE_ONCE_NOW)
			{
				DateTime tempAux3 = DateTime.Now;
				//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
				SetNextFireTime(tempAux3);
			}
		}

		/// <summary>
		/// Determines whether the date and time of the given DateTime
		/// instance falls on a scheduled fire-time of this trigger.
		/// <p>
		/// Note that the date returned is NOT validated against the related
		/// ICalendar (if any)
		/// </p>
		/// </summary>
		public virtual bool WillFireOn(DateTime test)
		{
			if ((seconds.Contains(test.Second) || seconds.Contains(ALL_SPEC)) &&
			    (minutes.Contains(test.Minute) || minutes.Contains(ALL_SPEC)) && (hours.Contains(test.Hour) || hours.Contains(ALL_SPEC)) &&
			    (daysOfMonth.Contains(test.Day) || daysOfMonth.Contains(ALL_SPEC)) &&
			    (months.Contains(test.Month) || months.Contains(ALL_SPEC)))
			{
				return true;
			}

			return false;
		}

		/// <summary> <p>
		/// Called after the <code>{@link Scheduler}</code> has executed the
		/// <code>{@link Job}</code> associated with the <code>Trigger</code> in
		/// order to get the final instruction code from the trigger.
		/// </p>
		/// 
		/// </summary>
		/// <param name="">context
		/// is the <code>JobExecutionContext</code> that was used by the
		/// <code>Job</code>'s<code>Execute(xx)</code> method.
		/// </param>
		/// <param name="">result
		/// is the <code>JobExecutionException</code> thrown by the
		/// <code>Job</code>, if any (may be null).
		/// </param>
		/// <returns> one of the Trigger.INSTRUCTION_XXX constants.
		/// 
		/// </returns>
		/// <seealso cref="INSTRUCTION_NOOP">
		/// </seealso>
		/// <seealso cref="INSTRUCTION_RE_EXECUTE_JOB">
		/// </seealso>
		/// <seealso cref="INSTRUCTION_DELETE_TRIGGER">
		/// </seealso>
		/// <seealso cref="INSTRUCTION_SET_TRIGGER_COMPLETE">
		/// </seealso>
		/// <seealso cref="Triggered(Calendar)">
		/// </seealso>
		public override int ExecutionComplete(JobExecutionContext context, JobExecutionException result)
		{
			if (result != null && result.RefireImmediately())
			{
				return INSTRUCTION_RE_EXECUTE_JOB;
			}

			if (result != null && result.RefireImmediately())
			{
				return INSTRUCTION_RE_EXECUTE_JOB;
			}

			if (result != null && result.unscheduleFiringTrigger())
			{
				return INSTRUCTION_SET_TRIGGER_COMPLETE;
			}

			if (result != null && result.unscheduleAllTriggers())
			{
				return INSTRUCTION_SET_ALL_JOB_TRIGGERS_COMPLETE;
			}

			if (!MayFireAgain())
			{
				return INSTRUCTION_DELETE_TRIGGER;
			}

			return INSTRUCTION_NOOP;
		}

		/// <summary> <p>
		/// Called when the <code>{@link Scheduler}</code> has decided to 'fire'
		/// the trigger (Execute the associated <code>Job</code>), in order to
		/// give the <code>Trigger</code> a chance to update itself for its next
		/// triggering (if any).
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="JobExecutionException">
		/// </seealso>
		public override void Triggered(ICalendar cal)
		{
			previousFireTime = nextFireTime;
			//UPGRADE_NOTE: ref keyword was added to struct-type parameters. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1303_3"'
			nextFireTime = GetFireTimeAfter(nextFireTime);

			while (nextFireTime.HasValue && cal != null && !cal.IsTimeIncluded(nextFireTime.Value))
			{
				nextFireTime = GetFireTimeAfter(nextFireTime);
			}
		}

		/// <summary>  </summary>
		public override void UpdateWithNewCalendar(ICalendar calendar, long misfireThreshold)
		{
			nextFireTime = GetFireTimeAfter(previousFireTime);

			DateTime now = DateTime.Now;
			do
			{
				while (nextFireTime.HasValue && calendar != null &&
				       !calendar.IsTimeIncluded(nextFireTime.Value))
				{
					nextFireTime = GetFireTimeAfter(nextFireTime);
				}

				if (nextFireTime.HasValue && nextFireTime.Value < now)
				{
					long diff = (long) (now - nextFireTime.Value).TotalMilliseconds;
					if (diff >= misfireThreshold)
					{
						nextFireTime = GetFireTimeAfter(nextFireTime);
						continue;
					}
				}
			} while (false);
		}

		/// <summary> <p>
		/// Called by the scheduler at the time a <code>Trigger</code> is first
		/// added to the scheduler, in order to have the <code>Trigger</code>
		/// compute its first fire time, based on any associated calendar.
		/// </p>
		/// 
		/// <p>
		/// After this method has been called, <code>getNextFireTime()</code>
		/// should return a valid answer.
		/// </p>
		/// 
		/// </summary>
		/// <returns> the first time at which the <code>Trigger</code> will be fired
		/// by the scheduler, which is also the same value <code>getNextFireTime()</code>
		/// will return (until after the first firing of the <code>Trigger</code>).
		/// </p>
		/// </returns>
		public override NullableDateTime ComputeFirstFireTime(ICalendar cal)
		{
			DateTime tempAux = StartTime.AddMilliseconds(-1000);

			nextFireTime = GetFireTimeAfter(tempAux);

			while (nextFireTime.HasValue && cal != null && !cal.IsTimeIncluded(nextFireTime.Value))
			{
				nextFireTime = GetFireTimeAfter(nextFireTime);
			}

			return nextFireTime.Value;
		}

		private string getExpressionSetSummary(ISet set_Renamed)
		{
			if (set_Renamed.Contains(NO_SPEC))
			{
				return "?";
			}
			if (set_Renamed.Contains(ALL_SPEC))
			{
				return "*";
			}

			StringBuilder buf = new StringBuilder();

			IEnumerator itr = set_Renamed.GetEnumerator();
			bool first = true;
			//UPGRADE_TODO: Method 'java.util.Iterator.hasNext' was converted to 'System.Collections.IEnumerator.MoveNext' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratorhasNext_3"'
			while (itr.MoveNext())
			{
				//UPGRADE_TODO: Method 'java.util.Iterator.next' was converted to 'System.Collections.IEnumerator.Current' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilIteratornext_3"'
				Int32 iVal = (Int32) itr.Current;
				string val = iVal.ToString();
				if (!first)
				{
					buf.Append(",");
				}
				buf.Append(val);
				first = false;
			}

			return buf.ToString();
		}

		////////////////////////////////////////////////////////////////////////////
		//
		// Computation Functions
		//
		////////////////////////////////////////////////////////////////////////////
		private NullableDateTime GetTimeAfter(NullableDateTime afterTime)
		{
			// move ahead one second, since we're computing the time *after* the
			// given time
			afterTime = afterTime.Value.AddSeconds(1);
			
			// CronTrigger does not deal with milliseconds
		
			DateTime d = afterTime.Value;
			DateTime cl = new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, 0);

			bool gotOne = false;

			// loop until we've computed the next time, or we've past the endTime
			while (!gotOne)
			{
				if (EndTime.HasValue && (cl > EndTime.Value))
				{
					return NullableDateTime.Default;
				}

				ISortedSet st;
				int t;

				int sec = cl.Second;
				int min = cl.Minute;

				// get second.................................................
				st = seconds.TailSet(sec);
				if (st != null && st.Count != 0)
				{
					sec = ((int) st[0]);
				}
				else
				{
					sec = ((int) seconds[0]);
					min++;
					cl = new DateTime(cl.Year, cl.Month, cl.Day, cl.Hour, min, cl.Second);
				}
				cl = new DateTime(cl.Year, cl.Month, cl.Day, cl.Hour, cl.Minute, sec);

				min = cl.Minute;
				int hr = cl.Hour;
				t = - 1;

				// get minute.................................................
				st = minutes.TailSet(min);
				if (st != null && st.Count != 0)
				{
					t = min;
					min = ((int) st[0]);
				}
				else
				{
					min = ((int) minutes[0]);
					hr++;
				}
				if (min != t)
				{
					cl = new DateTime(cl.Year, cl.Month, cl.Day, hr, min, 0);
					continue;
				}
				cl = new DateTime(cl.Year, cl.Month, cl.Day, cl.Hour, min, cl.Second);
				hr = cl.Hour;
				int day = cl.Day;
				t = - 1;

				// get hour...................................................
				st = hours.TailSet(hr);
				if (st != null && st.Count != 0)
				{
					t = hr;
					hr = ((int) st[0]);
				}
				else
				{
					hr = ((int) hours[0]);
					day++;
				}
				if (hr != t)
				{
					cl = new DateTime(cl.Year, cl.Month, day, hr, 0, 0);
					continue;
				}
				cl = new DateTime(cl.Year, cl.Month, cl.Day, hr, cl.Minute, cl.Second);

				day = cl.Day;
				int mon = cl.Month ;
				t = - 1;

				// get day...................................................
				bool dayOfMSpec = !daysOfMonth.Contains(NO_SPEC);
				bool dayOfWSpec = !daysOfWeek.Contains(NO_SPEC);
				if (dayOfMSpec && !dayOfWSpec)
				{
					// get day only by day of month
					// rule
					st = daysOfMonth.TailSet(day);
					if (lastdayOfMonth)
					{
						t = day;
						day = GetLastDayOfMonth(mon);
					}
					else if (st != null && st.Count != 0)
					{
						t = day;
						day = (int) st[0];
					}
					else
					{
						day = (int) daysOfMonth[0];
						mon++;
					}
					if (day != t)
					{
						cl = new DateTime(cl.Year, mon, day, 0, 0, 0);
						continue;
					}
				}
				else if (dayOfWSpec && !dayOfMSpec)
				{
					// get day only by day of
					// week rule
					if (lastdayOfWeek)
					{
						// are we looking for the last XXX day of
						// the month?
						int dow = ((int) daysOfWeek[0]); // desired
						// d-o-w
						int cDow = (int) cl.DayOfWeek;
						// current d-o-w
						int daysToAdd = 0;
						if (cDow < dow)
						{
							daysToAdd = dow - cDow;
						}
						if (cDow > dow)
						{
							daysToAdd = dow + (7 - cDow);
						}

						int lDay = GetLastDayOfMonth(mon);

						if (day + daysToAdd > lDay)
						{
							// did we already miss the last one?
							cl = new DateTime(cl.Year, mon + 1, 1, 0, 0, 0);
							continue;
						}

						// find date of last occurance of this day in this month...
						while ((day + daysToAdd + 7) <= lDay)
						{
							daysToAdd += 7;
						}

						day += daysToAdd;
					}
					else if (nthdayOfWeek != 0)
					{
						// are we looking for the Nth
						// XXX day in the month?
						int dow = ((int) daysOfWeek[0]); // desired
						// d-o-w
						int cDow = (int) cl.DayOfWeek;
						// current d-o-w
						int daysToAdd = 0;
						if (cDow < dow)
						{
							daysToAdd = dow - cDow;
						}
						else if (cDow > dow)
						{
							daysToAdd = dow + (7 - cDow);
						}

						day += daysToAdd;
						int weekOfMonth = day/7;
						if (day%7 > 0)
						{
							weekOfMonth++;
						}

						daysToAdd = (nthdayOfWeek - weekOfMonth)*7;
						day += daysToAdd;
						if (daysToAdd < 0 || day > GetLastDayOfMonth(mon))
						{
							cl = new DateTime(cl.Year, mon + 1, 1, 0, 0, 0);
							 // we are promoting the month
							continue;
						}
					}
					else
					{
						//UPGRADE_TODO: Method 'java.util.Calendar.get' was converted to 'SupportClass.CalendarManager.Get' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javautilCalendarget_int_3"'
						int cDow = (int) cl.DayOfWeek;
						// current d-o-w
						int dow = ((int) daysOfWeek[0]); // desired
						// d-o-w
						st = daysOfWeek.TailSet(cDow);
						if (st != null && st.Count > 0)
						{
							dow = ((int) st[0]);
						}

						int daysToAdd = 0;
						if (cDow < dow)
						{
							daysToAdd = dow - cDow;
						}
						if (cDow > dow)
						{
							daysToAdd = dow + (7 - cDow);
						}

						int lDay = GetLastDayOfMonth(mon);

						if (day + daysToAdd > lDay)
						{
							// will we pass the end of the month?
							cl = new DateTime(cl.Year, mon + 1, 1, 0, 0, 0);
							continue;
						}
						else if (daysToAdd > 0)
						{
							// are we swithing days?
							cl = new DateTime(cl.Year, mon, day + daysToAdd, 0, 0, 0);
							continue;
						}
					}
				}
				else
				{
					// dayOfWSpec && !dayOfMSpec
					throw new NotSupportedException("Support for specifying both a day-of-week AND a day-of-month parameter is not implemented."); // TODO:
				}
				cl = new DateTime(d.Year, d.Month, day, d.Hour, d.Minute, d.Second, d.Millisecond);

				mon = cl.Month + 1;
				// '+ 1' because calendar is 0-based for this field, and we are 1-based
				int year = cl.Year;
				t = - 1;

				// get month...................................................
				st = months.TailSet(mon);
				if (st != null && st.Count != 0)
				{
					t = mon;
					mon = ((Int32) st[0]);
				}
				else
				{
					mon = ((Int32) months[0]);
					year++;
				}
				if (mon != t)
				{
					cl = new DateTime(year, mon, 1, 0, 0, 0, 0);
					continue;
				}
				
				cl = new DateTime(d.Year, mon, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond);

				year = cl.Year;
				t = - 1;

				// get year...................................................
				st = years.TailSet(year);
				if (st != null && st.Count != 0)
				{
					t = year;
					year = ((Int32) st[0]);
				}
				else
				{
					return NullableDateTime.Default;
				} // ran out of years...

				if (year != t)
				{
					cl = new DateTime(year, mon, 1, 0, 0, 0, d.Millisecond);	
					continue;
				}
				cl = new DateTime(year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond);
				gotOne = true;
			} // while( !done )

			return cl;
		}

		private NullableDateTime GetTimeBefore( /*NullableDateTime endTime*/)
			// TODO: implement
		{
			return NullableDateTime.Default;
		}

		public virtual int GetLastDayOfMonth(int monthNum)
		{
			return DateTime.DaysInMonth(DateTime.Now.Year, monthNum);
		}

		[STAThread]
		public static void Main(string[] argv)
		{
			/*
			CronTrigger ct = new CronTrigger("a", "a");
			try
			{
				ct.CronExpressionString = "0 * * * * ? *";
			}
				//UPGRADE_TODO: Class 'java.text.ParseException' was converted to 'System.FormatException' which has a different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1073_javatextParseException_3"'
			catch (FormatException)
			{
				// log.error("caught an exception", e);
			}
			ct.StartTime = DateTime.Now;
			ct.TimeZone = TimeZone.CurrentTimeZone;
			Console.Out.WriteLine(ct.ExpressionSummary);
			ct.ComputeFirstFireTime(null);

			UICronTrigger uict = new UICronTrigger("a", "a");
			Int32[] set_Renamed = new Int32[1];
			set_Renamed[0] = 1;
			uict.Seconds = set_Renamed;
			Console.Out.WriteLine(ct.ExpressionSummary);
			uict.ComputeFirstFireTime(null);
			*/
		}

		static UICronTrigger()
		{
			{
				monthMap["JAN"] = 0;
				monthMap["FEB"] = 1;
				monthMap["MAR"] = 2;
				monthMap["APR"] = 3;
				monthMap["MAY"] = 4;
				monthMap["JUN"] = 5;
				monthMap["JUL"] = 6;
				monthMap["AUG"] = 7;
				monthMap["SEP"] = 8;
				monthMap["OCT"] = 9;
				monthMap["NOV"] = 10;
				monthMap["DEC"] = 11;

				dayMap["SUN"] = 1;
				dayMap["MON"] = 2;
				dayMap["TUE"] = 3;
				dayMap["WED"] = 4;
				dayMap["THU"] = 5;
				dayMap["FRI"] = 6;
				dayMap["SAT"] = 7;
			}
		}
	}
}