namespace Quartz.Tests.Unit;

[TestFixture]
public class TriggerBuilderTest
{
    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestStatefulJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return default;
        }
    }

    public class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return default;
        }
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestAnnotatedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return default;
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

        Assert.Multiple(() =>
        {
            Assert.That(trigger.Key.Name, Is.Not.EqualTo(null), "Expected non-null trigger name ");
            Assert.That(trigger.Key.Group, Is.EqualTo(JobKey.DefaultGroup), "Unexpected trigger group: " + trigger.Key.Group);
            Assert.That(trigger.JobKey, Is.EqualTo(null), "Unexpected job key: " + trigger.JobKey);
            Assert.That(trigger.Description, Is.EqualTo(null), "Unexpected job description: " + trigger.Description);
            Assert.That(trigger.Priority, Is.EqualTo(TriggerConstants.DefaultPriority), "Unexpected trigger priority: " + trigger.Priority);
            Assert.That(trigger.StartTimeUtc.DateTime, Is.EqualTo(DateTimeOffset.UtcNow.DateTime).Within(TimeSpan.FromSeconds(1)), "Unexpected start-time: " + trigger.StartTimeUtc);
            Assert.That(trigger.EndTimeUtc, Is.EqualTo(null), "Unexpected end-time: " + trigger.EndTimeUtc);
        });

        DateTimeOffset stime = DateBuilder.EvenSecondDateAfterNow();

        trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithDescription("my description")
            .WithPriority(2)
            .EndAt(DateBuilder.FutureDate(10, IntervalUnit.Week))
            .StartAt(stime)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(trigger.Key.Name, Is.EqualTo("t1"), "Unexpected trigger name " + trigger.Key.Name);
            Assert.That(trigger.Key.Group, Is.EqualTo(JobKey.DefaultGroup), "Unexpected trigger group: " + trigger.Key.Group);
            Assert.That(trigger.JobKey, Is.EqualTo(null), "Unexpected job key: " + trigger.JobKey);
            Assert.That(trigger.Description, Is.EqualTo("my description"), "Unexpected job description: " + trigger.Description);
            Assert.That(trigger.Priority, Is.EqualTo(2), "Unexpected trigger priority: " + trigger);
            Assert.That(trigger.StartTimeUtc, Is.EqualTo(stime), "Unexpected start-time: " + trigger.StartTimeUtc);
            Assert.That(trigger.EndTimeUtc, Is.Not.EqualTo(null), "Unexpected end-time: " + trigger.EndTimeUtc);
        });
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

    [Test(Description = "https://github.com/quartznet/quartznet/pull/212")]
    public void TestOverwriting()
    {
        var map = new JobDataMap();
        map.Put("key", "overwritingvalue");
        var trigger = TriggerBuilder.Create()
            .UsingJobData("key", "originalvalue")
            .UsingJobData(map)
            .Build();

        Assert.That(trigger.JobDataMap["key"], Is.EqualTo("overwritingvalue"));
    }
}