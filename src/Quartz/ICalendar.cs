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

using Quartz.Impl.Calendar;

namespace Quartz;

/// <summary>
/// An interface to be implemented by objects that define spaces of time during
/// which an associated <see cref="ITrigger" /> may (not) fire. Calendars
/// do not define actual fire times, but rather are used to limit a
/// <see cref="ITrigger" /> from firing on its normal schedule if necessary. Most
/// Calendars include all times by default and allow the user to specify times
/// to exclude.
/// </summary>
/// <remarks>
/// As such, it is often useful to think of Calendars as being used to <i>exclude</i> a block
/// of time - as opposed to <i>include</i> a block of time. (i.e. the
/// schedule &quot;fire every five minutes except on Sundays&quot; could be
/// implemented with a <see cref="ISimpleTrigger" /> and a
/// <see cref="WeeklyCalendar" /> which excludes Sundays)
/// <para>
/// An implementation of its own has two obligations. It must be properly cloneable, because the
/// scheduler hands callers <see cref="Clone" />s rather than the stored instance. And, to live in a
/// persistent store, it needs a
/// <see cref="Quartz.Serialization.SystemTextJson.Calendars.CalendarSerializer{TCalendar}" /> registered with
/// <c>AddCalendarSerializer</c> — <c>UseSystemTextJsonSerializer(json =&gt;
/// json.AddCalendarSerializer(new MyCalendarSerializer()))</c>. Without one, the first
/// <see cref="IScheduler.AddCalendar" /> that reaches the store fails while writing it, naming the
/// calendar's type. <c>[Serializable]</c> is what 3.x asked for and no longer stores anything: 4.x
/// has no <c>BinaryFormatter</c>.
/// </para>
/// </remarks>
/// <author>James House</author>
/// <author>Juergen Donnerstag</author>
/// <author>Marko Lahma (.NET)</author>
public interface ICalendar
{
    /// <summary>
    /// Gets or sets a description for the <see cref="ICalendar" /> instance - may be
    /// useful for remembering/displaying the purpose of the calendar, though
    /// the description has no meaning to Quartz.
    /// </summary>
    string? Description { get; set; }

    /// <summary>
    /// Set a new base calendar or remove the existing one.
    /// Get the base calendar.
    /// </summary>
    ICalendar? CalendarBase { get; set; }

    /// <summary>
    /// Determine whether the given UTC time  is 'included' by the
    /// Calendar.
    /// </summary>
    bool IsTimeIncluded(DateTimeOffset timeUtc);

    /// <summary>
    /// Determine the next UTC time that is 'included' by the
    /// Calendar after the given UTC time.
    /// </summary>
    DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc);

    /// <summary>
    /// Returns a copy of this calendar that can be changed without changing this one.
    /// </summary>
    /// <remarks>
    /// This is what stands between a caller's instance and the one a store holds: a store clones a
    /// calendar as it is added and again as it is read back, so an implementation that returned a
    /// shared object would let a caller edit stored scheduling data in place. The built-in calendars
    /// copy their exclusion sets and clone <see cref="CalendarBase" /> in turn.
    /// </remarks>
    ICalendar Clone();
}