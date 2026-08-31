namespace Quartz;

/// <summary>
/// A Comparator that compares trigger's next fire times, or in other words,
/// sorts them according to earliest next fire time.  If the fire times are
/// the same, then the triggers are sorted according to priority (highest
/// value first), if the priorities are the same, then they are sorted
/// by key.
/// </summary>
/// <remarks>
/// <para>
/// The key is the tie-break and stays the key, even though a store holding many triggers on one fire
/// time - one cron expression scheduled many times, which is the ordinary shape - then compares names
/// at every level of its <see cref="SortedSet{T}" />. #3542 asked whether the cached key hash could
/// stand in for the name there. It cannot: <c>TriggerTimeComparatorBenchmark</c>'s sorted-insert cases
/// put an insert into a 50,000-trigger set at 174 ns when the fire times are equal against 73 ns when
/// they differ, and a hash-first tie-break at 238 ns - slower than the comparison it was meant to
/// save. Ordering by hash scatters keys that the ordinal order keeps adjacent, so the tree walk it
/// lengthens costs more than the string comparison it skips.
/// </para>
/// <para>
/// It would also stop the order being the same order twice. A string's hash is seeded per process, so
/// the same triggers would sort one way in one run of a scheduler and another way in the next, while
/// "same fire time, same priority, then by key" is a property the acquisition and misfire tests - and
/// anyone reading a batch of triggers acquired at one instant - are written against.
/// </para>
/// </remarks>
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

        var t1 = trig1.NextFireTimeUtc;
        var t2 = trig2.NextFireTimeUtc;

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