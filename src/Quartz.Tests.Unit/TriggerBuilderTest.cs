namespace Quartz.Tests.Unit;

[NonParallelizable]
public class TriggerBuilderTest
{
    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestStatefulJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class TestAnnotatedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
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

        DateTimeOffset stime = TestDates.EvenSecondDateAfterNow();

        trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithDescription("my description")
            .WithPriority(2)
            .EndAt(TestDates.FutureDate(10, IntervalUnit.Week))
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
        map["key"] = "overwritingvalue";
        var trigger = TriggerBuilder.Create()
            .UsingJobData("key", "originalvalue")
            .UsingJobData(map)
            .Build();

        Assert.That(trigger.JobDataMap["key"], Is.EqualTo("overwritingvalue"));
    }

    [Test]
    public void UsingJobData_StoresTheValueWithItsOwnType()
    {
        Guid guid = Guid.NewGuid();

        ITrigger trigger = TriggerBuilder.Create<TestJob>()
            .UsingJobData("string", "text")
            .UsingJobData("int", 1)
            .UsingJobData("long", 2L)
            .UsingJobData("float", 3.5f)
            .UsingJobData("double", 4.5d)
            .UsingJobData("decimal", 5.5m)
            .UsingJobData("bool", true)
            .UsingJobData("guid", guid)
            .UsingJobData("char", 'c')
            .UsingJobData("null", null)
            .Build();

        // Same contract as JobBuilder's: the one object?-typed overload has to store exactly what
        // the nine primitive overloads it replaced stored.
        trigger.JobDataMap["string"].Should().Be("text");
        trigger.JobDataMap["int"].Should().Be(1).And.BeOfType<int>();
        trigger.JobDataMap["long"].Should().Be(2L).And.BeOfType<long>();
        trigger.JobDataMap["float"].Should().Be(3.5f).And.BeOfType<float>();
        trigger.JobDataMap["double"].Should().Be(4.5d).And.BeOfType<double>();
        trigger.JobDataMap["decimal"].Should().Be(5.5m).And.BeOfType<decimal>();
        trigger.JobDataMap["bool"].Should().Be(true).And.BeOfType<bool>();
        trigger.JobDataMap["guid"].Should().Be(guid).And.BeOfType<Guid>();
        trigger.JobDataMap["char"].Should().Be('c').And.BeOfType<char>();
        trigger.JobDataMap["null"].Should().BeNull();
    }

    [Test]
    public void WithCalendarName_NamesTheCalendarTheTriggerObserves()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithCalendarName("holidays")
            .Build();

        trigger.CalendarName.Should().Be("holidays");

        TriggerBuilder.Create().WithCalendarName(null).Build().CalendarName.Should().BeNull();
    }

    [Test]
    public void WithXSchedule_KeepsTheBuilderType()
    {
        // The extensions are generic in the receiver, so a chain that starts on TriggerBuilder<TJob>
        // stays on it and can go on naming the job's own properties afterwards.
        TriggerBuilder<TestJob> builder = TriggerBuilder.Create<TestJob>()
            .WithCronSchedule("0 0 12 * * ?")
            .WithIdentity("t1");

        builder.Build().Should().BeAssignableTo<ICronTrigger>();
    }

    [Test]
    public void WithXSchedule_KeepsTheConfiguratorType()
    {
        // ...and one that starts on the configurator the container hands out stays on that.
        ITriggerConfigurator<TestJob> configurator = TriggerBuilder.Create<TestJob>();

        ITriggerConfigurator<TestJob> configured = configurator
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(5)).RepeatForever())
            .WithDescription("every five minutes");

        ITrigger trigger = ((TriggerBuilder<TestJob>) configured).Build();

        trigger.Description.Should().Be("every five minutes");
        trigger.Should().BeAssignableTo<ISimpleTrigger>()
            .Which.RepeatInterval.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void WithSchedule_OnTheConfigurator_KeepsTheJobsType()
    {
        // WithSchedule is redeclared on the generic configurator for the same reason: losing TJob
        // here would lose the property-named job data that comes after it.
        ITriggerConfigurator<TestJob> configured = ((ITriggerConfigurator<TestJob>) TriggerBuilder.Create<TestJob>())
            .WithSchedule(CronScheduleBuilder.Create("0 0 12 * * ?"))
            .WithDescription("noon");

        ((TriggerBuilder<TestJob>) configured).Build().Description.Should().Be("noon");
    }

    [Test]
    public void WithXSchedule_TakesAPrebuiltBuilder()
    {
        // The hash-key cron overloads are gone; a hash key rides on the CronExpression instead.
        ITrigger trigger = TriggerBuilder.Create()
            .WithCronSchedule(CronScheduleBuilder.Create(new CronExpression("H H * * * ?", "custom-key")))
            .Build();

        trigger.Should().BeAssignableTo<ICronTrigger>();
    }
}