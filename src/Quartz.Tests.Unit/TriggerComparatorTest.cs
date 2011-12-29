using System.Collections.Generic;

using NUnit.Framework;

using Quartz.Spi;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class TriggerComparatorTest
    {
        [Test]
        public void TestTriggerSort()
        {
            // build trigger in expected sort order
            ITrigger t1 = TriggerBuilder.Create().WithIdentity("a").Build();
            ITrigger t2 = TriggerBuilder.Create().WithIdentity("b").Build();
            ITrigger t3 = TriggerBuilder.Create().WithIdentity("c").Build();
            ITrigger t4 = TriggerBuilder.Create().WithIdentity("a", "a").Build();
            ITrigger t5 = TriggerBuilder.Create().WithIdentity("a", "b").Build();
            ITrigger t6 = TriggerBuilder.Create().WithIdentity("a", "c").Build();

            List<ITrigger> ts = new List<ITrigger>();
            // add triggers to list in somewhat randomized order
            ts.Add(t5);
            ts.Add(t6);
            ts.Add(t4);
            ts.Add(t3);
            ts.Add(t1);
            ts.Add(t2);

            // sort the list
            ts.Sort();

            // check the order of the list
            Assert.AreEqual(t1, ts[0]);
            Assert.AreEqual(t2, ts[1]);
            Assert.AreEqual(t3, ts[2]);
            Assert.AreEqual(t4, ts[3]);
            Assert.AreEqual(t5, ts[4]);
            Assert.AreEqual(t6, ts[5]);
        }

        [Test]
        public void TestTriggerTimeSort()
        {
            // build trigger in expected sort order
            ITrigger t1 = TriggerBuilder.Create().WithIdentity("a").StartAt(DateBuilder.FutureDate(1, IntervalUnit.Minute)).Build();
            ((IOperableTrigger) t1).ComputeFirstFireTimeUtc(null);
            ITrigger t2 = TriggerBuilder.Create().WithIdentity("b").StartAt(DateBuilder.FutureDate(2, IntervalUnit.Minute)).Build();
            ((IOperableTrigger) t2).ComputeFirstFireTimeUtc(null);
            ITrigger t3 = TriggerBuilder.Create().WithIdentity("c").StartAt(DateBuilder.FutureDate(3, IntervalUnit.Minute)).Build();
            ((IOperableTrigger) t3).ComputeFirstFireTimeUtc(null);
            ITrigger t4 = TriggerBuilder.Create().WithIdentity("d").StartAt(DateBuilder.FutureDate(5, IntervalUnit.Minute)).WithPriority(7).Build();
            ((IOperableTrigger) t4).ComputeFirstFireTimeUtc(null);
            ITrigger t5 = TriggerBuilder.Create().WithIdentity("e").StartAt(DateBuilder.FutureDate(5, IntervalUnit.Minute)).Build();
            ((IOperableTrigger) t5).ComputeFirstFireTimeUtc(null);
            ITrigger t6 = TriggerBuilder.Create().WithIdentity("g").StartAt(DateBuilder.FutureDate(5, IntervalUnit.Minute)).Build();
            ((IOperableTrigger) t6).ComputeFirstFireTimeUtc(null);
            ITrigger t7 = TriggerBuilder.Create().WithIdentity("h").StartAt(DateBuilder.FutureDate(5, IntervalUnit.Minute)).WithPriority(2).Build();
            ((IOperableTrigger) t7).ComputeFirstFireTimeUtc(null);
            ITrigger t8 = TriggerBuilder.Create().WithIdentity("i").StartAt(DateBuilder.FutureDate(6, IntervalUnit.Minute)).Build();
            ((IOperableTrigger) t8).ComputeFirstFireTimeUtc(null);
            ITrigger t9 = TriggerBuilder.Create().WithIdentity("j").StartAt(DateBuilder.FutureDate(7, IntervalUnit.Minute)).Build();
            ((IOperableTrigger) t9).ComputeFirstFireTimeUtc(null);

            List<ITrigger> ts = new List<ITrigger>();
            // add triggers to list in somewhat randomized order
            ts.Add(t5);
            ts.Add(t9);
            ts.Add(t6);
            ts.Add(t8);
            ts.Add(t4);
            ts.Add(t3);
            ts.Add(t1);
            ts.Add(t7);
            ts.Add(t2);

            // sort the list
            ts.Sort();

            // check the order of the list
            Assert.AreEqual(t1, ts[0]);
            Assert.AreEqual(t2, ts[1]);
            Assert.AreEqual(t3, ts[2]);
            Assert.AreEqual(t4, ts[3]);
            Assert.AreEqual(t5, ts[4]);
            Assert.AreEqual(t6, ts[5]);
            Assert.AreEqual(t7, ts[6]);
            Assert.AreEqual(t8, ts[7]);
            Assert.AreEqual(t9, ts[8]);
        }
    }
}