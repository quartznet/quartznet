using System;

namespace Quartz
{
    /// <summary>
    /// The public interface for inspecting settings specific to a CronTrigger, 
    /// which is used to fire a <see cref="IJob" />
    /// at given moments in time, defined with Unix 'cron-like' schedule definitions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For those unfamiliar with "cron", this means being able to create a firing
    /// schedule such as: "At 8:00am every Monday through Friday" or "At 1:30am
    /// every last Friday of the month".
    /// </para>
    /// 
    /// <para>
    /// The format of a "Cron-Expression" string is documented on the 
    /// <see cref="CronExpression" /> class.
    /// </para>
    /// 
    /// <para>
    /// Here are some full examples: <br />
    /// <table cellspacing="8">
    /// <tr>
    /// <th align="left">Expression</th>
    /// <th align="left"> </th>
    /// <th align="left">Meaning</th>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 0 12 * * ?"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 12pm (noon) every day" /></td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 15 10 ? * *"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 10:15am every day" /></td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 15 10 * * ?"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 10:15am every day" /></td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 15 10 * * ? *"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 10:15am every day" /></td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 15 10 * * ? 2005"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 10:15am every day during the year 2005" />
    /// </td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 * 14 * * ?"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire every minute starting at 2pm and ending at 2:59pm, every day" />
    /// </td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 0/5 14 * * ?"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire every 5 minutes starting at 2pm and ending at 2:55pm, every day" />
    /// </td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 0/5 14,18 * * ?"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire every 5 minutes starting at 2pm and ending at 2:55pm, AND fire every 5 minutes starting at 6pm and ending at 6:55pm, every day" />
    /// </td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 0-5 14 * * ?"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire every minute starting at 2pm and ending at 2:05pm, every day" />
    /// </td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 10,44 14 ? 3 WED"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 2:10pm and at 2:44pm every Wednesday in the month of March." />
    /// </td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 15 10 ? * MON-FRI"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 10:15am every Monday, Tuesday, Wednesday, Thursday and Friday" />
    /// </td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 15 10 15 * ?"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 10:15am on the 15th day of every month" />
    /// </td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 15 10 L * ?"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 10:15am on the last day of every month" />
    /// </td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 15 10 ? * 6L"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 10:15am on the last Friday of every month" />
    /// </td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 15 10 ? * 6L"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 10:15am on the last Friday of every month" />
    /// </td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 15 10 ? * 6L 2002-2005"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 10:15am on every last Friday of every month during the years 2002, 2003, 2004 and 2005" />
    /// </td>
    /// </tr>
    /// <tr>
    /// <td align="left">"0 15 10 ? * 6#3"" /></td>
    /// <td align="left"> </td>
    /// <td align="left">Fire at 10:15am on the third Friday of every month" />
    /// </td>
    /// </tr>
    /// </table>
    /// </para>
    /// 
    /// <para>
    /// Pay attention to the effects of '?' and '*' in the day-of-week and
    /// day-of-month fields!
    /// </para>
    /// 
    /// <para>
    /// <b>NOTES:</b>
    /// <ul>
    /// <li>Support for specifying both a day-of-week and a day-of-month value is
    /// not complete (you'll need to use the '?' character in on of these fields).
    /// </li>
    /// <li>Be careful when setting fire times between mid-night and 1:00 AM -
    /// "daylight savings" can cause a skip or a repeat depending on whether the
    /// time moves back or jumps forward.</li>
    /// </ul>
    /// </para>
    /// </remarks>
    /// <seealso cref="ITrigger"/>
    /// <seealso cref="ISimpleTrigger"/>
    /// <author>Sharada Jambula</author>
    /// <author>James House</author>
    /// <author>Contributions from Mads Henderson</author>
    /// <author>Marko Lahma (.NET)</author>
    public interface ICronTrigger : ITrigger
    {
        /// <summary>
        /// Gets or sets the cron expression string.
        /// </summary>
        /// <value>The cron expression string.</value>
        string CronExpressionString { set; get; }

        /// <summary>
        /// Sets the time zone for which the <see cref="CronExpressionString" /> of this
        /// <see cref="ICronTrigger" /> will be resolved.
        /// </summary>
        /// <remarks>
        /// If <see cref="CronExpressionString" /> is set after this
        /// property, the TimeZone setting on the CronExpression will "win".  However
        /// if <see cref="CronExpressionString" /> is set after this property, the
        /// time zone applied by this method will remain in effect, since the 
        /// string cron expression does not carry a time zone!
        /// </remarks>
        /// <value>The time zone.</value>
        TimeZoneInfo TimeZone { get; set; }

        /// <summary>
        /// Gets the expression summary.
        /// </summary>
        /// <returns></returns>
        string GetExpressionSummary();
    }
}