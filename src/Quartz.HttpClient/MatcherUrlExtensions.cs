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

internal static class MatcherUrlExtensions
{
    public static string ToUrlParameters<T>(this GroupMatcher<T> matcher) where T : Key<T>
    {
        ArgumentNullException.ThrowIfNull(matcher);

        if (string.IsNullOrWhiteSpace(matcher.CompareToValue))
        {
            return "";
        }

        if (matcher.CompareWithOperator.Equals(StringOperator.Equality))
        {
            return $"groupEquals={matcher.CompareToValue}";
        }

        if (matcher.CompareWithOperator.Equals(StringOperator.StartsWith))
        {
            return $"groupStartsWith={matcher.CompareToValue}";
        }

        if (matcher.CompareWithOperator.Equals(StringOperator.EndsWith))
        {
            return $"groupEndsWith={matcher.CompareToValue}";
        }

        if (matcher.CompareWithOperator.Equals(StringOperator.Contains))
        {
            return $"groupContains={matcher.CompareToValue}";
        }

        return "";
    }

    public static string ToUrlParameters<T>(this NameMatcher<T> matcher) where T : Key<T>
    {
        ArgumentNullException.ThrowIfNull(matcher);

        return NameUrlParameters(matcher.CompareWithOperator, matcher.CompareToValue);
    }

    public static string ToUrlParameters(this CalendarNameMatcher matcher)
    {
        ArgumentNullException.ThrowIfNull(matcher);

        return NameUrlParameters(matcher.CompareWithOperator, matcher.CompareToValue);
    }

    private static string NameUrlParameters(StringOperator compareWith, string compareToValue)
    {
        if (string.IsNullOrWhiteSpace(compareToValue))
        {
            return "";
        }

        if (compareWith.Equals(StringOperator.Equality))
        {
            return $"nameEquals={compareToValue}";
        }

        if (compareWith.Equals(StringOperator.StartsWith))
        {
            return $"nameStartsWith={compareToValue}";
        }

        if (compareWith.Equals(StringOperator.EndsWith))
        {
            return $"nameEndsWith={compareToValue}";
        }

        if (compareWith.Equals(StringOperator.Contains))
        {
            return $"nameContains={compareToValue}";
        }

        return "";
    }
}