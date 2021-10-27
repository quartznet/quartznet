using NUnit.Framework;
using Quartz.Spi;
using System;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class TriggerTimeComparatorTest
    {
        private TriggerKey _triggerKeyA;
        private TriggerKey _triggerKeyB;
        private MutableTrigger _triggerAPrio1NextFireTimeMinValueInstance1;
        private MutableTrigger _triggerAPrio1NextFireTimeMinValueInstance2;
        private MutableTrigger _triggerAPrio1NextFireTimeMaxValue;
        private MutableTrigger _triggerAPrio1NextFireTimeNullInstance1;
        private MutableTrigger _triggerAPrio1NextFireTimeNullInstance2;
        private MutableTrigger _triggerBPrio1NextFireTimeNull;
        private MutableTrigger _triggerBPrio2NextFireTimeNull;
        private MutableTrigger _triggerBPrio1NextFireTimeMinValue;
        private MutableTrigger _triggerBPrio2NextFireTimeMinValue;
        private TriggerTimeComparator _comparer;

        public TriggerTimeComparatorTest()
        {
            _triggerKeyA = new TriggerKey("A");
            _triggerKeyB = new TriggerKey("B");

            _triggerAPrio1NextFireTimeMinValueInstance1 = new MutableTrigger(_triggerKeyA, JobKey.Create("B"), 1, DateTimeOffset.MinValue);
            _triggerAPrio1NextFireTimeMinValueInstance2 = new MutableTrigger(_triggerKeyA, JobKey.Create("B"), 1, DateTimeOffset.MinValue);
            _triggerAPrio1NextFireTimeMaxValue = new MutableTrigger(_triggerKeyA, JobKey.Create("B"), 1, DateTimeOffset.MaxValue);
            _triggerAPrio1NextFireTimeNullInstance1 = new MutableTrigger(_triggerKeyA, JobKey.Create("B"), 1, null);
            _triggerAPrio1NextFireTimeNullInstance2 = new MutableTrigger(_triggerKeyA, JobKey.Create("B"), 1, null);
            _triggerBPrio1NextFireTimeNull = new MutableTrigger(_triggerKeyB, JobKey.Create("B"), 1, null);
            _triggerBPrio2NextFireTimeNull = new MutableTrigger(_triggerKeyB, JobKey.Create("B"), 2, null);
            _triggerBPrio1NextFireTimeMinValue = new MutableTrigger(_triggerKeyB, JobKey.Create("B"), 1, DateTimeOffset.MinValue);
            _triggerBPrio2NextFireTimeMinValue = new MutableTrigger(_triggerKeyB, JobKey.Create("B"), 2, DateTimeOffset.MinValue);

            _comparer = new TriggerTimeComparator();
        }

        [Test]
        public void Compare_ReferenceEquality()
        {
            var actual = _comparer.Compare(_triggerAPrio1NextFireTimeMaxValue, _triggerAPrio1NextFireTimeMaxValue);
            Assert.AreEqual(0, actual);
        }

        [Test]
        public void Compare_NextFireTimeOfTrigger2IsNull()
        {
            var actual = _comparer.Compare(_triggerAPrio1NextFireTimeMinValueInstance1, _triggerAPrio1NextFireTimeNullInstance1);
            Assert.AreEqual(-1, actual);
        }

        [Test]
        public void Compare_NextFireTimeOfTrigger2IsLess()
        {
            var actual = _comparer.Compare(_triggerAPrio1NextFireTimeMaxValue, _triggerAPrio1NextFireTimeMinValueInstance1);
            Assert.AreEqual(1, actual);
        }

        [Test]
        public void Compare_NextFireTimeOfTrigger2IsGreater()
        {
            var actual = _comparer.Compare(_triggerAPrio1NextFireTimeMinValueInstance1, _triggerAPrio1NextFireTimeMaxValue);
            Assert.AreEqual(-1, actual);
        }

        [Test]
        public void Compare_NextFireTimeIsEqual_PriorityOfTrigger2IsLess()
        {
            var actual = _comparer.Compare(_triggerBPrio2NextFireTimeMinValue, _triggerBPrio1NextFireTimeMinValue);
            Assert.AreEqual(-1, actual);
        }

        [Test]
        public void Compare_NextFireTimeIsEqual_PriorityOfTrigger2IsGreater()
        {
            var actual = _comparer.Compare(_triggerBPrio1NextFireTimeMinValue, _triggerBPrio2NextFireTimeMinValue);
            Assert.AreEqual(1, actual);
        }

        [Test]
        public void Compare_NextFireTimeIsEqual_PriorityIsEqual_KeyIsEqual()
        {
            var actual = _comparer.Compare(_triggerAPrio1NextFireTimeMinValueInstance1, _triggerAPrio1NextFireTimeMinValueInstance2);
            Assert.AreEqual(0, actual);
        }

        [Test]
        public void Compare_NextFireTimeIsEqual_PriorityIsEqual_KeyOfTrigger2IsGreater()
        {
            var actual = _comparer.Compare(_triggerAPrio1NextFireTimeMinValueInstance1, _triggerBPrio1NextFireTimeMinValue);
            Assert.AreEqual(-1, actual);
        }

        [Test]
        public void Compare_NextFireTimeIsEqual_PriorityIsEqual_KeyOfTrigger2IsLess()
        {
            var actual = _comparer.Compare(_triggerBPrio1NextFireTimeMinValue, _triggerAPrio1NextFireTimeMinValueInstance1);
            Assert.AreEqual(1, actual);
        }


        [Test]
        public void Compare_NextFireTimeIsNull_PriorityOfTrigger2IsLess()
        {
            var actual = _comparer.Compare(_triggerBPrio2NextFireTimeNull, _triggerBPrio1NextFireTimeNull);
            Assert.AreEqual(-1, actual);
        }

        [Test]
        public void Compare_NextFireTimeIsNull_PriorityOfTrigger2IsGreater()
        {
            var actual = _comparer.Compare(_triggerBPrio1NextFireTimeNull, _triggerBPrio2NextFireTimeNull);
            Assert.AreEqual(1, actual);
        }

        [Test]
        public void Compare_NextFireTimeIsNull_PriorityIsEqual_KeyIsEqual()
        {
            var actual = _comparer.Compare(_triggerAPrio1NextFireTimeNullInstance1, _triggerAPrio1NextFireTimeNullInstance2);
            Assert.AreEqual(0, actual);
        }

        public void Compare_NextFireTimeIsNull_PriorityIsEqual_KeyOfTrigger2IsLess()
        {
            var actual = _comparer.Compare(_triggerBPrio1NextFireTimeNull, _triggerAPrio1NextFireTimeNullInstance1);
            Assert.AreEqual(0, actual);
        }

        public void Compare_NextFireTimeIsNull_PriorityIsEqual_KeyOfTrigger2IsGreater()
        {
            var actual = _comparer.Compare(_triggerAPrio1NextFireTimeNullInstance1, _triggerBPrio1NextFireTimeNull);
            Assert.AreEqual(0, actual);
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
            public string Description { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public string CalendarName { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public JobDataMap JobDataMap { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int Priority { get; set; }
            public DateTimeOffset StartTimeUtc { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public DateTimeOffset? EndTimeUtc { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public int MisfireInstruction { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
            public DateTimeOffset? FinalFireTimeUtc => throw new NotImplementedException();
            public bool HasMillisecondPrecision => throw new NotImplementedException();

            public string FireInstanceId { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public ITrigger Clone()
            {
                throw new NotImplementedException();
            }

            public int CompareTo(ITrigger other)
            {
                throw new NotImplementedException();
            }

            public DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar calendar)
            {
                throw new NotImplementedException();
            }

            public SchedulerInstruction ExecutionComplete(IJobExecutionContext context, JobExecutionException result)
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
}
