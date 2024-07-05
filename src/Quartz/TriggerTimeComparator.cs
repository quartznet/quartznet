namespace Quartz;

/// <summary>
/// A Comparator that compares trigger's next fire times, or in other words,
/// sorts them according to earliest next fire time.  If the fire times are
/// the same, then the triggers are sorted according to priority (highest
/// value first), if the priorities are the same, then they are sorted
/// by key.
/// </summary>
[Serializable]
internal sealed class TriggerTimeComparator : IComparer<ITrigger>
{
    public int Compare(ITrigger? trig1, ITrigger? trig2)
    {
        if (ReferenceEquals(trig1, trig2))
        {
            return 0;
        }

        if (trig1 is null)
        {
            return 1;
        }

        if (trig2 is null)
        {
            return -1;
        }

        var t1 = trig1.GetNextFireTimeUtc();
        var t2 = trig2.GetNextFireTimeUtc();

        if (t1 is not null || t2 is not null)
        {
            if (t1 is null)
            {
                return 1;
            }

            if (t2 is null)
            {
                return -1;
            }

            // Use GetValueOrDefault() to avoid going through expensive Nullable<T>.Value.
            // In .NET 6.0, the JIT has been improved but since we also support other and
            // older CLRs...
            var result = t1.GetValueOrDefault().CompareTo(t2.GetValueOrDefault());
            if (result != 0)
            {
                return result;
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