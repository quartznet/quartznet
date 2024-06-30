using Quartz.Impl.Matchers;
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
}