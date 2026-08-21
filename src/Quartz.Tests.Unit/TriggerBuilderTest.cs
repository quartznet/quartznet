using System;
using System.Threading.Tasks;

using Quartz.Spi;

namespace Quartz.Tests.Unit;

[NonParallelizable]
public class TriggerBuilderTest
{
    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestStatefulJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            return Task.CompletedTask;
        }
    }

    public class TestJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            return Task.CompletedTask;
        }
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestAnnotatedJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            return Task.CompletedTask;
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

    [Test(Description = "https://github.com/quartznet/quartznet/issues/3294")]
    public void ModifiedByCalendar_TreatsABlankNameAsNoCalendar()
    {
        TriggerBuilder.Create().ModifiedByCalendar("holidays").Build()
            .CalendarName.Should().Be("holidays");

        TriggerBuilder.Create().ModifiedByCalendar(null).Build()
            .CalendarName.Should().BeNull();

        TriggerBuilder.Create().ModifiedByCalendar("").Build()
            .CalendarName.Should().BeNull(
                "every job store gates its calendar lookup on a non-null name, so a blank one would be looked up, not found, and the trigger would silently stop firing");

        TriggerBuilder.Create().ModifiedByCalendar("   ").Build()
            .CalendarName.Should().BeNull();

        TriggerBuilder.Create().ModifiedByCalendar(" holidays ").Build()
            .CalendarName.Should().Be(" holidays ",
                "a calendar is looked up by the exact name it was stored under, so trimming would break a calendar genuinely registered with padding");
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/3294")]
    public void CalendarName_TreatsABlankNameAsNoCalendar_WhenSetDirectly()
    {
        // The builder is not the only writer: the JSON converters, the ADO store and plain user code
        // all assign the property, so the normalization has to live in the setter.
        IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create().ModifiedByCalendar("holidays").Build();

        trigger.CalendarName = "";
        trigger.CalendarName.Should().BeNull();

        trigger.CalendarName = "   ";
        trigger.CalendarName.Should().BeNull();

        trigger.CalendarName = "holidays";
        trigger.CalendarName.Should().Be("holidays");
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/3294")]
    public void GetTriggerBuilder_DoesNotResurrectABlankCalendarName()
    {
        IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create().WithIdentity("t1").Build();
        trigger.CalendarName = "";

        trigger.GetTriggerBuilder().Build().CalendarName.Should().BeNull();
    }
}