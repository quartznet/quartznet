using Quartz.Impl.Triggers;
using Quartz.Spi;

namespace Quartz.Tests.Unit;

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

        // add triggers to list in somewhat randomized order
        List<ITrigger> ts =
        [
            t5,
            t6,
            t4,
            t3,
            t1,
            t2
        ];

        // sort the list
        ts.Sort(TriggerComparer.Instance);

        Assert.Multiple(() =>
        {
            // check the order of the list
            Assert.That(ts[0], Is.EqualTo(t1));
            Assert.That(ts[1], Is.EqualTo(t2));
            Assert.That(ts[2], Is.EqualTo(t3));
            Assert.That(ts[3], Is.EqualTo(t4));
            Assert.That(ts[4], Is.EqualTo(t5));
            Assert.That(ts[5], Is.EqualTo(t6));
        });
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

        // add triggers to list in somewhat randomized order
        List<ITrigger> ts =
        [
            t5,
            t9,
            t6,
            t8,
            t4,
            t3,
            t1,
            t7,
            t2
            // sort the list
        ];

        // sort the list
        ts.Sort(TriggerComparer.Instance);

        Assert.Multiple(() =>
        {
            // check the order of the list
            Assert.That(ts[0], Is.EqualTo(t1));
            Assert.That(ts[1], Is.EqualTo(t2));
            Assert.That(ts[2], Is.EqualTo(t3));
            Assert.That(ts[3], Is.EqualTo(t4));
            Assert.That(ts[4], Is.EqualTo(t5));
            Assert.That(ts[5], Is.EqualTo(t6));
            Assert.That(ts[6], Is.EqualTo(t7));
            Assert.That(ts[7], Is.EqualTo(t8));
            Assert.That(ts[8], Is.EqualTo(t9));
        });
    }
}