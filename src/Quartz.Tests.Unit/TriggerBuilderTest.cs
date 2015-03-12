using System;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class TriggerBuilderTest
    {
        [DisallowConcurrentExecution]
        [PersistJobDataAfterExecution]
        public class TestStatefulJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
            }
        }

        public class TestJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
            }
        }

        [DisallowConcurrentExecution]
        [PersistJobDataAfterExecution]
        public class TestAnnotatedJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
            }
        }

        [SetUp]
        public void SetUp()
        {
        }

        [Test]
        public void TestTriggerBuilder()
        {
            ITrigger trigger = TriggerBuilder.Create()
                .Build();

            Assert.IsTrue(trigger.Key.Name != null, "Expected non-null trigger name ");
            Assert.IsTrue(trigger.Key.Group.Equals(JobKey.DefaultGroup), "Unexpected trigger group: " + trigger.Key.Group);
            Assert.IsTrue(trigger.JobKey == null, "Unexpected job key: " + trigger.JobKey);
            Assert.IsTrue(trigger.Description == null, "Unexpected job description: " + trigger.Description);
            Assert.IsTrue(trigger.Priority == TriggerConstants.DefaultPriority, "Unexpected trigger priority: " + trigger.Priority);
            Assert.That(trigger.StartTimeUtc.DateTime, Is.EqualTo(DateTimeOffset.UtcNow.DateTime).Within(TimeSpan.FromSeconds(1)), "Unexpected start-time: " + trigger.StartTimeUtc);
            Assert.IsTrue(trigger.EndTimeUtc == null, "Unexpected end-time: " + trigger.EndTimeUtc);

            DateTimeOffset stime = DateBuilder.EvenSecondDateAfterNow();

            trigger = TriggerBuilder.Create()
                .WithIdentity("t1")
                .WithDescription("my description")
                .WithPriority(2)
                .EndAt(DateBuilder.FutureDate(10, IntervalUnit.Week))
                .StartAt(stime)
                .Build();

            Assert.IsTrue(trigger.Key.Name.Equals("t1"), "Unexpected trigger name " + trigger.Key.Name);
            Assert.IsTrue(trigger.Key.Group.Equals(JobKey.DefaultGroup), "Unexpected trigger group: " + trigger.Key.Group);
            Assert.IsTrue(trigger.JobKey == null, "Unexpected job key: " + trigger.JobKey);
            Assert.IsTrue(trigger.Description.Equals("my description"), "Unexpected job description: " + trigger.Description);
            Assert.IsTrue(trigger.Priority == 2, "Unexpected trigger priority: " + trigger);
            Assert.IsTrue(trigger.StartTimeUtc.Equals(stime), "Unexpected start-time: " + trigger.StartTimeUtc);
            Assert.IsTrue(trigger.EndTimeUtc != null, "Unexpected end-time: " + trigger.EndTimeUtc);
        }

        [Test]
        public void TestTriggerBuilderWithEndTimePriorCurrentTime()
        {
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("some trigger name", "some trigger group")
                .ForJob("some job name", "some job group")
                .StartAt(DateTime.Now - TimeSpan.FromMilliseconds(200000000))
                .EndAt(DateTime.Now - TimeSpan.FromMilliseconds(100000000))
                .WithCronSchedule("0 0 0 * * ?")
                .Build();
        }
    }
}