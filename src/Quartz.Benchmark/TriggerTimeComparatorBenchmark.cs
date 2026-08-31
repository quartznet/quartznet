using BenchmarkDotNet.Attributes;
using Quartz.Extensibility;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class TriggerTimeComparatorBenchmark
{
    /// <summary>
    /// How many triggers the sorted-insert cases put in one set. The size <see cref="ScheduleJobBenchmark" />
    /// fills <c>RAMJobStore</c> to, so the tree is as deep here as it is there and a comparison is paid at
    /// as many levels.
    /// </summary>
    private const int SortedSetSize = 50_000;

    private readonly TriggerKey _triggerKeyA;
    private readonly TriggerKey _triggerKeyB;
    private readonly TriggerTimeComparator _comparerNew;
    private readonly TriggerTimeComparatorLegacy _comparerLegacy;
    private readonly TriggerTimeComparatorHashTieBreak _comparerHashTieBreak = new();
    private MutableTrigger[] _oneFireTime = [];
    private MutableTrigger[] _distinctFireTimes = [];
    private readonly MutableTrigger _triggerAPrio1NextFireTimeMinValue;
    private readonly MutableTrigger _triggerAPrio1NextFireTimeMaxValue;
    private readonly MutableTrigger _triggerAPrio1NextFireTimeNull;
    private readonly MutableTrigger _triggerBPrio1NextFireTimeNull;
    private readonly MutableTrigger _triggerBPrio2NextFireTimeNull;
    private readonly MutableTrigger _triggerBPrio1NextFireTimeMinValue;
    private readonly MutableTrigger _triggerBPrio2NextFireTimeMinValue;

    public TriggerTimeComparatorBenchmark()
    {
        _triggerKeyA = new TriggerKey("A");
        _triggerKeyB = new TriggerKey("B");

        _comparerNew = new TriggerTimeComparator();
        _comparerLegacy = new TriggerTimeComparatorLegacy();

        _triggerAPrio1NextFireTimeMinValue = new MutableTrigger(_triggerKeyA, new JobKey("B"), 1, DateTimeOffset.MinValue);
        _triggerAPrio1NextFireTimeMaxValue = new MutableTrigger(_triggerKeyA, new JobKey("B"), 1, DateTimeOffset.MaxValue);
        _triggerAPrio1NextFireTimeNull = new MutableTrigger(_triggerKeyA, new JobKey("B"), 1, null);
        _triggerBPrio1NextFireTimeNull = new MutableTrigger(_triggerKeyB, new JobKey("B"), 1, null);
        _triggerBPrio2NextFireTimeNull = new MutableTrigger(_triggerKeyB, new JobKey("B"), 2, null);
        _triggerBPrio1NextFireTimeMinValue = new MutableTrigger(_triggerKeyB, new JobKey("B"), 1, DateTimeOffset.MinValue);
        _triggerBPrio2NextFireTimeMinValue = new MutableTrigger(_triggerKeyB, new JobKey("B"), 2, DateTimeOffset.MinValue);
    }

    /// <summary>
    /// The trigger sets the sorted-insert cases fill from. Only those cases pay for building them, which
    /// is why this is a targeted setup rather than the constructor every case runs.
    /// </summary>
    [GlobalSetup(Targets = [nameof(SortedInsert_DistinctFireTimes), nameof(SortedInsert_OneFireTime), nameof(SortedInsert_OneFireTime_HashTieBreak)])]
    public void SetupSortedInsert()
    {
        DateTimeOffset fireAt = new DateTimeOffset(2026, 3, 16, 12, 0, 0, TimeSpan.Zero);
        JobKey jobKey = new JobKey("job", "bench");

        _oneFireTime = new MutableTrigger[SortedSetSize];
        _distinctFireTimes = new MutableTrigger[SortedSetSize];
        for (int i = 0; i < SortedSetSize; i++)
        {
            _oneFireTime[i] = new MutableTrigger(new TriggerKey("cron-trigger-" + i, "bench"), jobKey, 5, fireAt);
            _distinctFireTimes[i] = new MutableTrigger(new TriggerKey("simple-trigger-" + i, "bench"), jobKey, 5, fireAt.AddMilliseconds(i));
        }
    }

    /// <summary>
    /// The comparison a store pays when the triggers it holds fire at different times: it is decided on
    /// the fire time and the tie-break never runs.
    /// </summary>
    [Benchmark(OperationsPerInvoke = SortedSetSize)]
    public int SortedInsert_DistinctFireTimes()
    {
        return Fill(_distinctFireTimes, _comparerNew);
    }

    /// <summary>
    /// The same insert when every trigger shares one fire time - one cron expression scheduled many
    /// times, which is the ordinary shape - so every level of the tree is decided on the key.
    /// </summary>
    [Benchmark(OperationsPerInvoke = SortedSetSize)]
    public int SortedInsert_OneFireTime()
    {
        return Fill(_oneFireTime, _comparerNew);
    }

    /// <summary>
    /// The alternative #3542 proposed for that tie-break, kept as the measurement that refused it.
    /// </summary>
    [Benchmark(OperationsPerInvoke = SortedSetSize)]
    public int SortedInsert_OneFireTime_HashTieBreak()
    {
        return Fill(_oneFireTime, _comparerHashTieBreak);
    }

    private static int Fill(MutableTrigger[] triggers, IComparer<ITrigger> comparer)
    {
        SortedSet<ITrigger> set = new SortedSet<ITrigger>(comparer);
        foreach (MutableTrigger trigger in triggers)
        {
            set.Add(trigger);
        }

        return set.Count;
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_ReferenceEquality_New()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerNew.Compare(_triggerAPrio1NextFireTimeMaxValue, _triggerAPrio1NextFireTimeMaxValue);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeOfOtherIsNull_New()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerNew.Compare(_triggerAPrio1NextFireTimeMinValue, _triggerAPrio1NextFireTimeNull);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeOfOtherIsLess_New()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerNew.Compare(_triggerAPrio1NextFireTimeMaxValue, _triggerAPrio1NextFireTimeMinValue);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeOfOtherIsGreater_New()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerNew.Compare(_triggerAPrio1NextFireTimeMinValue, _triggerAPrio1NextFireTimeMaxValue);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeIsEqual_PriorityOfOtherIsLess_New()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerNew.Compare(_triggerBPrio2NextFireTimeMinValue, _triggerBPrio1NextFireTimeMinValue);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeIsEqual_PriorityOfOtherIsGreater_New()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerNew.Compare(_triggerBPrio1NextFireTimeMinValue, _triggerBPrio2NextFireTimeMinValue);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeIsEqual_PriorityIsEqual_New()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerNew.Compare(_triggerAPrio1NextFireTimeMinValue, _triggerBPrio1NextFireTimeMinValue);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeIsNull_PriorityOfOtherIsLess_New()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerNew.Compare(_triggerBPrio2NextFireTimeNull, _triggerBPrio1NextFireTimeNull);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeIsNull_PriorityOfOtherIsGreater_New()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerNew.Compare(_triggerBPrio1NextFireTimeNull, _triggerBPrio2NextFireTimeNull);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeIsNull_PriorityIsEqual_New()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerNew.Compare(_triggerAPrio1NextFireTimeNull, _triggerBPrio1NextFireTimeNull);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_ReferenceEquality_Old()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerLegacy.Compare(_triggerAPrio1NextFireTimeMaxValue, _triggerAPrio1NextFireTimeMaxValue);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeOfOtherIsNull_Old()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerLegacy.Compare(_triggerAPrio1NextFireTimeMinValue, _triggerAPrio1NextFireTimeNull);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeOfOtherIsLess_Old()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerLegacy.Compare(_triggerAPrio1NextFireTimeMaxValue, _triggerAPrio1NextFireTimeMinValue);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeOfOtherIsGreater_Old()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerLegacy.Compare(_triggerAPrio1NextFireTimeMinValue, _triggerAPrio1NextFireTimeMaxValue);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeIsEqual_PriorityOfOtherIsLess_Old()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerLegacy.Compare(_triggerBPrio2NextFireTimeMinValue, _triggerBPrio1NextFireTimeMinValue);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeIsEqual_PriorityOfOtherIsGreater_Old()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerLegacy.Compare(_triggerBPrio1NextFireTimeMinValue, _triggerBPrio2NextFireTimeMinValue);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeIsEqual_PriorityIsEqual_Old()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerLegacy.Compare(_triggerAPrio1NextFireTimeMinValue, _triggerBPrio1NextFireTimeMinValue);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeIsNull_PriorityOfOtherIsLess_Old()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerLegacy.Compare(_triggerBPrio2NextFireTimeNull, _triggerBPrio1NextFireTimeNull);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeIsNull_PriorityOfOtherIsGreater_Old()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerLegacy.Compare(_triggerBPrio1NextFireTimeNull, _triggerBPrio2NextFireTimeNull);
        }
    }

    [Benchmark(OperationsPerInvoke = 300_000)]
    public void CompareTo_NextFireTimeIsNull_PriorityIsEqual_Old()
    {
        for (var i = 0; i < 300_000; i++)
        {
            _comparerLegacy.Compare(_triggerAPrio1NextFireTimeNull, _triggerBPrio1NextFireTimeNull);
        }
    }

    /// <summary>
    /// The shipped comparison with a cached-hash tie-break in front of the key comparison, which is what
    /// #3542 proposed to keep equal fire times off the trigger name at every level of the set.
    /// </summary>
    /// <remarks>
    /// It is here as the measurement rather than as a candidate: the sorted-insert cases put it well
    /// behind the shipped comparator. A hash order scatters keys the ordinal order keeps adjacent, so the
    /// walk it lengthens costs more than the string comparison it skips - and a string's hash is seeded
    /// per process, so the order would also stop being the same order from one run to the next.
    /// </remarks>
    public sealed class TriggerTimeComparatorHashTieBreak : IComparer<ITrigger>
    {
        public int Compare(ITrigger? trig1, ITrigger? trig2)
        {
            if (ReferenceEquals(trig1, trig2))
            {
                return 0;
            }

            DateTimeOffset? t1 = trig1!.NextFireTimeUtc;
            DateTimeOffset? t2 = trig2!.NextFireTimeUtc;

            int result = t1.GetValueOrDefault().CompareTo(t2.GetValueOrDefault());
            if (result != 0)
            {
                return result;
            }

            int comp = trig2.Priority - trig1.Priority;
            if (comp != 0)
            {
                return comp;
            }

            TriggerKey key1 = trig1.Key;
            TriggerKey key2 = trig2.Key;

            int hash1 = key1.GetHashCode();
            int hash2 = key2.GetHashCode();
            if (hash1 != hash2)
            {
                return hash1 < hash2 ? -1 : 1;
            }

            return key1.CompareTo(key2);
        }
    }

    [Serializable]
    public class TriggerTimeComparatorLegacy : IComparer<ITrigger>
    {
        public int Compare(ITrigger? trig1, ITrigger? trig2)
        {
            if (trig1 is null && trig2 is null)
            {
                return 0;
            }

            var t1 = trig1!.NextFireTimeUtc;
            var t2 = trig2!.NextFireTimeUtc;

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

    private sealed class MutableTrigger : IMutableTrigger
    {
        private readonly DateTimeOffset? _nextFireTimeUtc;

        public MutableTrigger(TriggerKey key, JobKey jobKey, int priority, DateTimeOffset? nextFireTimeUtc)
        {
            Key = key;
            JobKey = jobKey;
            Priority = priority;
            _nextFireTimeUtc = nextFireTimeUtc;
        }

        public TriggerKey Key { get; set; }
        public JobKey JobKey { get; set; }
        public string? Description { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string? ExecutionGroup { get => null; set { } }
        public PreferredNode PreferredNode { get => PreferredNode.None; set { } }
        public string? CalendarName { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public JobDataMap JobDataMap { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public int Priority { get; set; }
        public DateTimeOffset StartTimeUtc { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public DateTimeOffset? EndTimeUtc { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public int MisfireInstructionCode { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public DateTimeOffset? FinalFireTimeUtc => throw new NotImplementedException();
        public bool HasMillisecondPrecision => throw new NotImplementedException();

        public ITrigger Clone()
        {
            throw new NotImplementedException();
        }

        public int CompareTo(ITrigger? other)
        {
            throw new NotImplementedException();
        }

        public DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime)
        {
            throw new NotImplementedException();
        }

        public bool MayFireAgain => throw new NotImplementedException();

        public DateTimeOffset? NextFireTimeUtc
        {
            get => _nextFireTimeUtc;
            set => throw new NotImplementedException();
        }

        public DateTimeOffset? PreviousFireTimeUtc
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public IScheduleBuilder GetScheduleBuilder()
        {
            throw new NotImplementedException();
        }

        public TriggerBuilder<IJob> GetTriggerBuilder()
        {
            throw new NotImplementedException();
        }
    }
}