using Quartz.Matchers;
using Quartz.Util;

namespace Quartz.HttpClient;

internal static class Extensions
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

        if (string.IsNullOrWhiteSpace(matcher.CompareToValue))
        {
            return "";
        }

        if (matcher.CompareWithOperator.Equals(StringOperator.Equality))
        {
            return $"nameEquals={matcher.CompareToValue}";
        }

        if (matcher.CompareWithOperator.Equals(StringOperator.StartsWith))
        {
            return $"nameStartsWith={matcher.CompareToValue}";
        }

        if (matcher.CompareWithOperator.Equals(StringOperator.EndsWith))
        {
            return $"nameEndsWith={matcher.CompareToValue}";
        }

        if (matcher.CompareWithOperator.Equals(StringOperator.Contains))
        {
            return $"nameContains={matcher.CompareToValue}";
        }

        return "";
    }
}