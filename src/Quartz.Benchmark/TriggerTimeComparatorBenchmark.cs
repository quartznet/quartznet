using BenchmarkDotNet.Attributes;
using Quartz.Spi;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class TriggerTimeComparatorBenchmark
{
    private readonly TriggerKey _triggerKeyA;
    private readonly TriggerKey _triggerKeyB;
    private readonly TriggerTimeComparator _comparerNew;
    private readonly TriggerTimeComparatorLegacy _comparerLegacy;
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

        _triggerAPrio1NextFireTimeMinValue = new MutableTrigger(_triggerKeyA, JobKey.Create("B"), 1, DateTimeOffset.MinValue);
        _triggerAPrio1NextFireTimeMaxValue = new MutableTrigger(_triggerKeyA, JobKey.Create("B"), 1, DateTimeOffset.MaxValue);
        _triggerAPrio1NextFireTimeNull = new MutableTrigger(_triggerKeyA, JobKey.Create("B"), 1, null);
        _triggerBPrio1NextFireTimeNull = new MutableTrigger(_triggerKeyB, JobKey.Create("B"), 1, null);
        _triggerBPrio2NextFireTimeNull = new MutableTrigger(_triggerKeyB, JobKey.Create("B"), 2, null);
        _triggerBPrio1NextFireTimeMinValue = new MutableTrigger(_triggerKeyB, JobKey.Create("B"), 1, DateTimeOffset.MinValue);
        _triggerBPrio2NextFireTimeMinValue = new MutableTrigger(_triggerKeyB, JobKey.Create("B"), 2, DateTimeOffset.MinValue);
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

    [Serializable]
    public class TriggerTimeComparatorLegacy : IComparer<ITrigger>
    {
        public int Compare(ITrigger? trig1, ITrigger? trig2)
        {
            if (trig1 is null && trig2 is null)
            {
                return 0;
            }

            var t1 = trig1!.GetNextFireTimeUtc();
            var t2 = trig2!.GetNextFireTimeUtc();

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

    private class MutableTrigger : IMutableTrigger
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
        public string? CalendarName { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public JobDataMap JobDataMap { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public int Priority { get; set; }
        public DateTimeOffset StartTimeUtc { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public DateTimeOffset? EndTimeUtc { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public int MisfireInstruction { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
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

        public bool GetMayFireAgain()
        {
            throw new NotImplementedException();
        }

        public DateTimeOffset? GetNextFireTimeUtc()
        {
            return _nextFireTimeUtc;
        }

        public DateTimeOffset? GetPreviousFireTimeUtc()
        {
            throw new NotImplementedException();
        }

        public IScheduleBuilder GetScheduleBuilder()
        {
            throw new NotImplementedException();
        }

        public TriggerBuilder GetTriggerBuilder()
        {
            throw new NotImplementedException();
        }
    }
}