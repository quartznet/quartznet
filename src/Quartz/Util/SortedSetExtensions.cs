using System;
using System.Collections.Generic;

namespace Quartz.Util;

internal static class SortedSetExtensions
{
    // ReSharper disable once UnusedMember.Global -- introduced in v4
    internal static bool TryGetMinValueStartingFrom(this SortedSet<int> set, DateTimeOffset start, bool allowValueBeforeStartDay, out int minimumDay)
    {
        minimumDay = 0;

        if (set.Count == 0)
        {
            return false;
        }
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

    internal static bool TryGetMinValueStartingFrom(this SortedSet<int> set, int start, out int min)
    {
        min = set.Count > 0 ? set.Min : 0;

        if (set.Count == 0 || set.Max < start)
        {
            return false;
        }

        if (set.Contains(start))
        {
            min = start;
            return true;
        }

        if (set.Min >= start)
        {
            min = set.Min;
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