using System.Collections.Generic;

namespace Quartz.Util
{
    internal static class SortedSetExtensions
    {
        internal static SortedSet<int> TailSet(this SortedSet<int> set, int value)
        {
            return set.GetViewBetween(value, 9999999);
        }
    }
}