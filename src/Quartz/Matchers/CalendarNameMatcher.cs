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

using Quartz.Util;

namespace Quartz;

/// <summary>
/// Matches a calendar by name, for <see cref="CalendarQuery.Name" />.
/// </summary>
/// <remarks>
/// Jobs and triggers are identified by a <see cref="Key{T}" /> and so are matched by
/// <see cref="NameMatcher{TKey}" />, which is written against that key. A calendar is identified by
/// a bare string — it has no group and no key type — so it gets a matcher of its own rather than a
/// key type invented to satisfy a generic constraint. The four factories and the wire spellings
/// they map to are the same as the key matchers'. There is no "any name" factory, because
/// <see cref="CalendarQuery.Name" /> is nullable and null already means every name.
/// </remarks>
public sealed class CalendarNameMatcher : IEquatable<CalendarNameMatcher>
{
    private CalendarNameMatcher(string compareTo, StringOperator compareWith)
    {
        if (compareTo is null)
        {
            Throw.ArgumentNullException(nameof(compareTo), "CompareTo value cannot be null!");
        }

        CompareToValue = compareTo;
        CompareWithOperator = compareWith;
    }

    /// <summary>
    /// The string the calendar name is compared against.
    /// </summary>
    public string CompareToValue { get; }

    /// <summary>
    /// How the calendar name is compared against <see cref="CompareToValue" />.
    /// </summary>
    public StringOperator CompareWithOperator { get; }

    /// <summary>
    /// Matches calendar names equaling the given string.
    /// </summary>
    public static CalendarNameMatcher NameEquals(string compareTo) => new CalendarNameMatcher(compareTo, StringOperator.Equality);

    /// <summary>
    /// Matches calendar names starting with the given string.
    /// </summary>
    public static CalendarNameMatcher NameStartsWith(string compareTo) => new CalendarNameMatcher(compareTo, StringOperator.StartsWith);

    /// <summary>
    /// Matches calendar names ending with the given string.
    /// </summary>
    public static CalendarNameMatcher NameEndsWith(string compareTo) => new CalendarNameMatcher(compareTo, StringOperator.EndsWith);

    /// <summary>
    /// Matches calendar names containing the given string.
    /// </summary>
    public static CalendarNameMatcher NameContains(string compareTo) => new CalendarNameMatcher(compareTo, StringOperator.Contains);

    /// <summary>
    /// Whether the given calendar name matches.
    /// </summary>
    public bool IsMatch(string calendarName) => CompareWithOperator.Evaluate(calendarName, CompareToValue);

    public bool Equals(CalendarNameMatcher? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
               && CompareToValue == other.CompareToValue
               && CompareWithOperator.Equals(other.CompareWithOperator);
    }

    public override bool Equals(object? obj) => Equals(obj as CalendarNameMatcher);

    public override int GetHashCode() => HashCode.Combine(CompareToValue, CompareWithOperator);

    public override string ToString() => $"{CompareWithOperator}({CompareToValue})";
}
