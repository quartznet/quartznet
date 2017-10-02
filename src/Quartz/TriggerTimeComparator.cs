using System;
using System.Collections.Generic;

namespace Quartz
{
    /// <summary>
    /// A Comparator that compares trigger's next fire times, or in other words,
    /// sorts them according to earliest next fire time.  If the fire times are
    /// the same, then the triggers are sorted according to priority (highest
    /// value first), if the priorities are the same, then they are sorted
    /// by key.
    /// </summary>
    [Serializable]
    public class TriggerTimeComparator : IComparer<ITrigger>
    {
        public int Compare(ITrigger trig1, ITrigger trig2)
        {
            DateTimeOffset? t1 = trig1.GetNextFireTimeUtc();
            DateTimeOffset? t2 = trig2.GetNextFireTimeUtc();

            if (t1 != null || t2 != null)
            {
                if (t1 == null)
                {
                    return 1;
                }

                if (t2 == null)
                {
                    return -1;
                }

                if (t1 < t2)
                {
                    return -1;
                }

                if (t1 > t2)
                {
                    return 1;
                }
            }

            int comp = trig2.Priority - trig1.Priority;
            if (comp != 0)
            {
                return comp;
            }

            return trig1.Key.CompareTo(trig2.Key);
        }
    }
}