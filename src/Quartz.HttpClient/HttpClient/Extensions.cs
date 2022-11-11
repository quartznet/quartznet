using Quartz.Impl.Matchers;
using Quartz.Util;

namespace Quartz.HttpClient;

internal static class Extensions
{
    public static string ToUrlParameters<T>(this GroupMatcher<T> groupMatcher) where T : Key<T>
    {
        if (groupMatcher == null || string.IsNullOrWhiteSpace(groupMatcher.CompareToValue))
        {
            return "";
        }

        if (groupMatcher.CompareWithOperator.Equals(StringOperator.Equality))
        {
            return $"groupEquals={groupMatcher.CompareToValue}";
        }

        if (groupMatcher.CompareWithOperator.Equals(StringOperator.StartsWith))
        {
            return $"groupStartsWith={groupMatcher.CompareToValue}";
        }

        if (groupMatcher.CompareWithOperator.Equals(StringOperator.EndsWith))
        {
            return $"groupEndsWith={groupMatcher.CompareToValue}";
        }

        if (groupMatcher.CompareWithOperator.Equals(StringOperator.Contains))
        {
            return $"groupContains={groupMatcher.CompareToValue}";
        }

        return "";
    }
}