using Quartz.Impl.Triggers;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit;

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
        List<ITrigger> triggers =
        [
            t5,
            t6,
            t4,
            t3,
            t1,
            t2
        ];

        // sort the list
        triggers.Sort(TriggerComparer.Instance);

        Assert.Multiple(() =>
        {
            // check the order of the list
            Assert.That(triggers[0], Is.EqualTo(t1));
            Assert.That(triggers[1], Is.EqualTo(t2));
            Assert.That(triggers[2], Is.EqualTo(t3));
            Assert.That(triggers[3], Is.EqualTo(t4));
            Assert.That(triggers[4], Is.EqualTo(t5));
            Assert.That(triggers[5], Is.EqualTo(t6));
        });
    }

    [Test]
    public void TestTriggerTimeSort()
    {
        // build trigger in expected sort order
        ITrigger t1 = TriggerBuilder.Create().WithIdentity("a").StartAt(TestDates.FutureDate(1, IntervalUnit.Minute)).Build();
        ((IOperableTrigger) t1).ComputeFirstFireTimeUtc(null);
        ITrigger t2 = TriggerBuilder.Create().WithIdentity("b").StartAt(TestDates.FutureDate(2, IntervalUnit.Minute)).Build();
        ((IOperableTrigger) t2).ComputeFirstFireTimeUtc(null);
        ITrigger t3 = TriggerBuilder.Create().WithIdentity("c").StartAt(TestDates.FutureDate(3, IntervalUnit.Minute)).Build();
        ((IOperableTrigger) t3).ComputeFirstFireTimeUtc(null);
        ITrigger t4 = TriggerBuilder.Create().WithIdentity("d").StartAt(TestDates.FutureDate(5, IntervalUnit.Minute)).WithPriority(7).Build();
        ((IOperableTrigger) t4).ComputeFirstFireTimeUtc(null);
        ITrigger t5 = TriggerBuilder.Create().WithIdentity("e").StartAt(TestDates.FutureDate(5, IntervalUnit.Minute)).Build();
        ((IOperableTrigger) t5).ComputeFirstFireTimeUtc(null);
        ITrigger t6 = TriggerBuilder.Create().WithIdentity("g").StartAt(TestDates.FutureDate(5, IntervalUnit.Minute)).Build();
        ((IOperableTrigger) t6).ComputeFirstFireTimeUtc(null);
        ITrigger t7 = TriggerBuilder.Create().WithIdentity("h").StartAt(TestDates.FutureDate(5, IntervalUnit.Minute)).WithPriority(2).Build();
        ((IOperableTrigger) t7).ComputeFirstFireTimeUtc(null);
        ITrigger t8 = TriggerBuilder.Create().WithIdentity("i").StartAt(TestDates.FutureDate(6, IntervalUnit.Minute)).Build();
        ((IOperableTrigger) t8).ComputeFirstFireTimeUtc(null);
        ITrigger t9 = TriggerBuilder.Create().WithIdentity("j").StartAt(TestDates.FutureDate(7, IntervalUnit.Minute)).Build();
        ((IOperableTrigger) t9).ComputeFirstFireTimeUtc(null);

        // add triggers to list in somewhat randomized order
        List<ITrigger> triggers =
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
        triggers.Sort(TriggerComparer.Instance);

        Assert.Multiple(() =>
        {
            // check the order of the list
            Assert.That(triggers[0], Is.EqualTo(t1));
            Assert.That(triggers[1], Is.EqualTo(t2));
            Assert.That(triggers[2], Is.EqualTo(t3));
            Assert.That(triggers[3], Is.EqualTo(t4));
            Assert.That(triggers[4], Is.EqualTo(t5));
            Assert.That(triggers[5], Is.EqualTo(t6));
            Assert.That(triggers[6], Is.EqualTo(t7));
            Assert.That(triggers[7], Is.EqualTo(t8));
            Assert.That(triggers[8], Is.EqualTo(t9));
        });
    }
}