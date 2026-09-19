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

using System.Collections.Generic;

namespace System;

/// <summary>
/// <see cref="DateOnly" />, which arrived in .NET 6, reduced to what the linked sources read.
/// </summary>
/// <remarks>
/// One internal member takes one — <c>TimeZones.StartOfLocalDay</c>, which the calendars call and no
/// diagnostic reaches. This is the date half of a <see cref="DateTime" /> and nothing else.
/// </remarks>
internal readonly struct DateOnly
{
    private readonly DateTime value;

    public DateOnly(int year, int month, int day) => value = new DateTime(year, month, day);

    private DateOnly(DateTime value) => this.value = value.Date;

    public int Year => value.Year;

    public int Month => value.Month;

    public int Day => value.Day;

    public static DateOnly FromDateTime(DateTime dateTime) => new DateOnly(dateTime);

    public DateTime ToDateTime(TimeOnly time) => value.Add(time.ToTimeSpan());
}

/// <summary>
/// <see cref="TimeOnly" />, which arrived in .NET 6, reduced to what the linked sources read.
/// </summary>
internal readonly struct TimeOnly
{
    private readonly TimeSpan value;

    private TimeOnly(TimeSpan value) => this.value = value;

    public static TimeOnly MinValue => new TimeOnly(TimeSpan.Zero);

    public TimeSpan ToTimeSpan() => value;
}

/// <summary>
/// <see cref="HashCode" />, which arrived in .NET Standard 2.1, reduced to the one overload the
/// linked sources call.
/// </summary>
/// <remarks>
/// <see cref="CronExpression.GetHashCode" /> is the only caller. Nothing in this assembly hashes an
/// expression for anything but a dictionary it never builds, so this need only be a hash — it is not
/// required to be, and is not, the same number the BCL's implementation produces.
/// </remarks>
internal readonly struct HashCode
{
    public static int Combine<T1, T2>(T1 value1, T2 value2)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + EqualityComparer<T1>.Default.GetHashCode(value1!);
            hash = hash * 31 + EqualityComparer<T2>.Default.GetHashCode(value2!);
            return hash;
        }
    }
}
