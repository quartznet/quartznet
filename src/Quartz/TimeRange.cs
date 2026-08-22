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

using System.Runtime.InteropServices;

namespace Quartz;

/// <summary>
/// A span of the day, from one time of day to another.
/// </summary>
/// <remarks>
/// This is what <see cref="Quartz.Impl.Calendar.DailyCalendar" /> excludes or includes. It is a plain
/// pair: whether a range has to start before it ends, and how precise its bounds may be, is the
/// question of whoever is using it, and <c>DailyCalendar</c> answers both for itself.
/// </remarks>
/// <param name="Start">The time of day the range starts at.</param>
/// <param name="End">The time of day the range ends at.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TimeRange(TimeOnly Start, TimeOnly End)
{
    /// <summary>
    /// Reads a <c>(start, end)</c> pair as a range, so a range can be written as a tuple literal.
    /// </summary>
    public static implicit operator TimeRange((TimeOnly Start, TimeOnly End) value) => new(value.Start, value.End);

    /// <inheritdoc />
    public override string ToString() => $"{Start:HH:mm:ss.fff} - {End:HH:mm:ss.fff}";
}
