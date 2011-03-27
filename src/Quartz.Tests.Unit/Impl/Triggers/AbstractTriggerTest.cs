using System;

using NUnit.Framework;

using Quartz.Impl.Triggers;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.Triggers
{
    [TestFixture]
    public class AbstractTriggerTest
    {
        private AbstractTrigger trigger;

        [SetUp]
        public void SetUp()
        {
            trigger = new TestTrigger();
        }

        [Test]
        public void TriggersShouldBeSerializableSoThatKeyIsSerialized()
        {
            trigger.Key = new TriggerKey("tname", "tgroup");
            trigger.JobKey = new JobKey("jname", "jgroup");
            
            AbstractTrigger cloned = trigger.DeepClone();
            
            Assert.That(cloned.Name, Is.EqualTo(trigger.Name));
            Assert.That(cloned.Group, Is.EqualTo(trigger.Group));
            Assert.That(cloned.Key, Is.EqualTo(trigger.Key));
        }


        [Serializable]
        private class TestTrigger : AbstractTrigger
        {
            public override IScheduleBuilder GetScheduleBuilder()
            {
                throw new NotImplementedException();
            }

            public override DateTimeOffset? FinalFireTimeUtc
            {
                get { throw new NotImplementedException(); }
            }

            public override void Triggered(ICalendar cal)
            {
                throw new NotImplementedException();
            }

            public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar cal)
            {
                throw new NotImplementedException();
            }

            public override bool GetMayFireAgain()
            {
                throw new NotImplementedException();
            }

            public override DateTimeOffset? GetNextFireTimeUtc()
            {
                throw new NotImplementedException();
            }

            public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime)
            {
                throw new NotImplementedException();
            }

            protected override bool ValidateMisfireInstruction(int misfireInstruction)
            {
                throw new NotImplementedException();
            }

            public override void UpdateAfterMisfire(ICalendar cal)
            {
                throw new NotImplementedException();
            }

            public override void UpdateWithNewCalendar(ICalendar cal, TimeSpan misfireThreshold)
            {
                throw new NotImplementedException();
            }

            public override void SetNextFireTimeUtc(DateTimeOffset? nextFireTime)
            {
                throw new NotImplementedException();
            }

            public override void SetPreviousFireTimeUtc(DateTimeOffset? previousFireTime)
            {
                throw new NotImplementedException();
            }

            public override DateTimeOffset? GetPreviousFireTimeUtc()
            {
                throw new NotImplementedException();
            }

            public override bool HasMillisecondPrecision
            {
                get { throw new NotImplementedException(); }
            }
        }
    }
}