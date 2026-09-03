#region License
/* 
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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
#endregion

using System;
using System.Globalization;

namespace Quartz.Impl.AdoJobStore;

/// <summary> 
/// This class contains utility functions for use in all delegate classes.
/// </summary>
/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>Marko Lahma (.NET)</author>
public static class AdoJobStoreUtil
{
    /// <summary>
    /// Replace the table prefix in a query by replacing any occurrences of
    /// "{0}" with the table prefix.
    /// </summary>
    /// <param name="query">The unsubstituted query</param>
    /// <param name="tablePrefix">The table prefix</param>
    /// <param name="schedNameLiteral">the scheduler name - no longer required, scheduler name is now a sql parameter</param>
    /// <returns>The query, with proper table prefix substituted</returns>
    [Obsolete("Use overload with schedNameLiteral, scheduler name is now a sql parameter")]
    public static string ReplaceTablePrefix(string query, string tablePrefix, string schedNameLiteral)
    {
        return string.Format(CultureInfo.InvariantCulture, query, tablePrefix, schedNameLiteral);
    }

    /// <summary>
    /// Replace the table prefix in a query by replacing any occurrences of
    /// "{0}" with the table prefix, and "{1}" with the unqualified portion of
    /// the table prefix (everything after the last '.', or the whole prefix when
    /// the prefix has no schema qualifier).
    /// </summary>
    /// <remarks>
    /// The "{1}" placeholder is intended for identifiers that must not include
    /// a schema, such as MySQL index names in <c>FORCE INDEX</c> hints.
    /// </remarks>
    /// <param name="query">The unsubstituted query</param>
    /// <param name="tablePrefix">The table prefix</param>
    /// <returns>The query, with proper table prefix substituted</returns>
    public static string ReplaceTablePrefix(string query, string tablePrefix)
    {
        var lastDot = tablePrefix.LastIndexOf('.');
        var unqualifiedPrefix = lastDot >= 0 ? tablePrefix.Substring(lastDot + 1) : tablePrefix;
        return string.Format(CultureInfo.InvariantCulture, query, tablePrefix, unqualifiedPrefix);
    }

    /// <summary>
    /// Refuses a duration the schema cannot hold exactly, naming the trigger and the column it was
    /// going into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Duration columns hold whole milliseconds - see <c>StdAdoDelegate.GetDbTimeSpanValue</c>, which
    /// casts <c>TotalMilliseconds</c> to <c>long</c> - so a shorter interval used to be written as
    /// <c>0</c>. For a simple trigger that is not merely lossy: the trigger read back has a zero
    /// repeat interval, <c>SimpleTriggerImpl.GetFireTimeAfter</c> divides by it, and the resulting
    /// <see cref="DivideByZeroException" /> is caught and logged by the store - leaving the row in
    /// <c>ACQUIRED</c> for good, which is a job that stops running and says nothing (#3673).
    /// </para>
    /// <para>
    /// Refused here rather than rounded, because rounding half a millisecond to zero and rounding it
    /// to one are both schedules the caller did not ask for. The message names the column so that
    /// what to change the value to is not a guess.
    /// </para>
    /// </remarks>
    /// <param name="value">The duration about to be persisted.</param>
    /// <param name="column">The column it is going into, as the schema spells it.</param>
    /// <param name="triggerKey">The trigger that carries it.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="value" /> carries ticks below a whole millisecond.
    /// </exception>
    internal static void RequireStorableDuration(TimeSpan value, string column, TriggerKey triggerKey)
    {
        if (value.Ticks % TimeSpan.TicksPerMillisecond == 0)
        {
            return;
        }

        string message = FormattableString.Invariant(
            $"Trigger '{triggerKey}' has an interval of {value:c}, which a persistent job store cannot hold: {column} keeps whole milliseconds, so it would be stored as {(long) value.TotalMilliseconds} ms and read back as a different schedule. Round the interval to a whole number of milliseconds, or keep this trigger in the in-memory store.");

        throw new ArgumentException(message, nameof(value));
    }
}