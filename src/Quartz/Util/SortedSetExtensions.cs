namespace Quartz.Util;

internal static class SortedSetExtensions
{
    internal static bool TryGetMinValueStartingFrom(this SortedSet<int> set, int start, out int min)
    {
        min = set.Min;

        if (set.Contains(CronExpressionConstants.AllSpec) || set.Contains(start))
        {
            min = start;
            return true;
        }

        if (set.Count == 0 || set.Max < start)
        {
            return false;
        }

        if (set.Min >= start)
        {
            // value is contained and would be returned from view
            return true;
        }

        // slow path
        var view = set.GetViewBetween(start, int.MaxValue);
        if (view.Count > 0)
        {
            min = view.Min;
            return true;
        }

        return false;
    }
}