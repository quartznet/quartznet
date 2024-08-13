using Newtonsoft.Json;

using Quartz.Impl.Triggers;
using Quartz.Tests.Unit.Utils;

namespace Quartz.Tests.Unit.Impl.Triggers;

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

        Assert.Multiple(() =>
        {
            Assert.That(cloned.Key, Is.EqualTo(trigger.Key));
            Assert.That(cloned.JobKey, Is.EqualTo(trigger.JobKey));
        });
    }

    [Serializable]
    private class TestTrigger : AbstractTrigger
    {
        public TestTrigger() : base(TimeProvider.System)
        {
        }

        public override IScheduleBuilder GetScheduleBuilder()
        {
            throw new NotImplementedException();
        }

        [JsonIgnore]
        public override DateTimeOffset? FinalFireTimeUtc => throw new NotImplementedException();

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
            // This method must be implemented because it's used in AbstractTrigger.MisfireInstruction's setter
            // and JSON serialization serializes at the property level (as opposed to the binary formatter which
            // serialized at the field level and, therefore, did not need this implemented).
            return true;
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

        public override bool HasMillisecondPrecision => false;
    }
}