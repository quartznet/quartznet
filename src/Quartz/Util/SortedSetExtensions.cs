namespace Quartz.Util;

internal static class SortedSetExtensions
{
    internal static bool TryGetMinValueStartingFrom(this SortedSet<int> set, DateTimeOffset start, bool allowValueBeforeStartDay, out int minimumDay)
    {
        minimumDay = set.Min;
        var startDay = start.Day;

        if (set.Contains(CronExpressionConstants.AllSpec) || set.Contains(startDay))
        {
            minimumDay = startDay;
            return true;
        }

        // In cases such as W modifier finding a match earlier than the month day.
        // If the flag allowValueBeforeStartDay is set and the minimum value is less than the start day, return the minimum value 
        if (allowValueBeforeStartDay && set.Min < startDay)
        {
            return true;
        }
        
        // If the set is empty or the maximum value is less than the start day, no suitable value is found
        if (set.Count == 0 || set.Max < startDay)
        {
            return false;
        }

        // If the minimum value is greater than or equal to the start day, return the minimum value
        if (set.Min >= startDay)
        {
            return true;
        }

        // slow path
        var view = set.GetViewBetween(startDay, int.MaxValue);
        if (view.Count > 0)
        {
            minimumDay = view.Min;
            return true;
        }

        return false;
    }
}