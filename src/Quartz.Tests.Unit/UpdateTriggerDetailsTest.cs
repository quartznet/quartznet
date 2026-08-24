using System.Collections.Specialized;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Jobs;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit;

public class UpdateTriggerDetailsTest
{
    private RAMJobStore jobStore;
    private IJobDetail jobDetail;

    [SetUp]
    public async Task SetUp()
    {
        jobStore = TestJobStores.Ram();
        TestSignaler signaler = new TestSignaler();
        await jobStore.Initialize(TestJobStores.Identity());
        await jobStore.SchedulerStarted();

        jobDetail = JobBuilder.Create()
            .OfType<NoOpJob>()
            .WithIdentity(new JobKey("job1", "jobGroup1"))
            .StoreDurably(true)
            .Build();

        await jobStore.AddJob(jobDetail, false);
    }

    [Test]
    public async Task UpdateDescription_PreservesFireTimes()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.NextFireTimeUtc!.Value;

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("Updated description");

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        result.Should().BeTrue();
        IOperableTrigger retrieved = (await jobStore.GetTrigger(trigger.Key))!;
        retrieved.Should().NotBeNull();
        retrieved.Description.Should().Be("Updated description");
        retrieved.NextFireTimeUtc.Should().Be(nextFireBefore);
    }

    [Test]
    public async Task UpdatePriority_PreservesFireTimes()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.NextFireTimeUtc!.Value;

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithPriority(10);

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        result.Should().BeTrue();
        IOperableTrigger retrieved = (await jobStore.GetTrigger(trigger.Key))!;
        retrieved.Priority.Should().Be(10);
        retrieved.NextFireTimeUtc.Should().Be(nextFireBefore);
    }

    [Test]
    public async Task UpdateJobDataMap_PreservesFireTimes()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.NextFireTimeUtc!.Value;

        JobDataMap newData = new JobDataMap { { "key1", "value1" } };
        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithJobDataMap(newData);

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        result.Should().BeTrue();
        IOperableTrigger retrieved = (await jobStore.GetTrigger(trigger.Key))!;
        retrieved.JobDataMap.GetString("key1").Should().Be("value1");
        retrieved.NextFireTimeUtc.Should().Be(nextFireBefore);
    }

    [Test]
    public async Task TriggerNotFound_ReturnsFalse()
    {
        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("something");

        bool result = await jobStore.UpdateTriggerDetails(new TriggerKey("nonexistent", "g1"), update);

        result.Should().BeFalse();
    }

    [Test]
    public async Task PreservesState_WhenPaused()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);
        await jobStore.PauseTrigger(trigger.Key);

        (await jobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused);

        await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithDescription("Updated while paused"));

        (await jobStore.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Paused);
        (await jobStore.GetTrigger(trigger.Key))!.Description.Should().Be("Updated while paused");
    }

    [Test]
    public async Task PriorityChange_AffectsAcquisitionOrder()
    {
        DateTimeOffset d = TestDates.EvenMinuteDateAfterNow();

        IOperableTrigger trigger1 = new SimpleTriggerImpl("t1", "g1", jobDetail.Key.Name, jobDetail.Key.Group, d.AddSeconds(100), d.AddSeconds(300), 2, TimeSpan.FromSeconds(2));
        IOperableTrigger trigger2 = new SimpleTriggerImpl("t2", "g1", jobDetail.Key.Name, jobDetail.Key.Group, d.AddSeconds(100), d.AddSeconds(300), 2, TimeSpan.FromSeconds(2));

        trigger1.ComputeFirstFireTimeUtc(null);
        trigger2.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger1, false);
        await jobStore.AddTrigger(trigger2, false);

        await jobStore.UpdateTriggerDetails(trigger2.Key, new TriggerDetailsUpdate().WithPriority(10));

        DateTimeOffset firstFireTime = trigger1.NextFireTimeUtc!.Value;
        var acquired = await jobStore.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = firstFireTime.AddSeconds(10), MaxCount = 2, TimeWindow = TimeSpan.FromMilliseconds(1) });

        acquired.Should().HaveCount(2);
        acquired.First().Key.Should().Be(trigger2.Key);
        acquired.Last().Key.Should().Be(trigger1.Key);
    }

    [Test]
    public async Task CalendarName_ValidatesExistence()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        Assert.ThrowsAsync<JobPersistenceException>(async () =>
            await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithCalendarName("nonexistent")));
    }

    [Test]
    public async Task CalendarName_NullClearsCalendar()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.CalendarName = "myCal";
        trigger.ComputeFirstFireTimeUtc(null);

        await jobStore.AddCalendar("myCal", new BaseCalendar());
        await jobStore.AddTrigger(trigger, false);

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithCalendarName(null));

        result.Should().BeTrue();
        (await jobStore.GetTrigger(trigger.Key))!.CalendarName.Should().BeNull();
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/3294")]
    public async Task CalendarName_BlankClearsCalendarRatherThanFailingTheUpdate()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.CalendarName = "myCal";
        trigger.ComputeFirstFireTimeUtc(null);

        await jobStore.AddCalendar("myCal", new BaseCalendar());
        await jobStore.AddTrigger(trigger, false);

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithCalendarName(""));

        result.Should().BeTrue("a blank name means no calendar, so there is nothing whose existence to check");
        (await jobStore.GetTrigger(trigger.Key))!.CalendarName.Should().BeNull();
    }

    [Test]
    public async Task EmptyUpdate_ReturnsTrueForExistingTrigger()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        (await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate())).Should().BeTrue();
    }

    [Test]
    public async Task MultipleProperties_UpdatedAtOnce()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.NextFireTimeUtc!.Value;

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("new desc")
            .WithPriority(7)
            .WithJobDataMap(new JobDataMap { { "k", "v" } });

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, update);

        result.Should().BeTrue();
        IOperableTrigger retrieved = (await jobStore.GetTrigger(trigger.Key))!;
        retrieved.Description.Should().Be("new desc");
        retrieved.Priority.Should().Be(7);
        retrieved.JobDataMap.GetString("k").Should().Be("v");
        retrieved.NextFireTimeUtc.Should().Be(nextFireBefore);
    }

    [Test]
    public async Task MisfireInstruction_UpdatedWithoutReschedule()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.MisfireInstructionCode = MisfireInstruction.IgnoreMisfirePolicy;
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.NextFireTimeUtc!.Value;

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing));

        result.Should().BeTrue();
        IOperableTrigger retrieved = (await jobStore.GetTrigger(trigger.Key))!;
        retrieved.MisfireInstructionCode.Should().Be(MisfireInstruction.CronTrigger.DoNothing);
        retrieved.NextFireTimeUtc.Should().Be(nextFireBefore);
    }

    [Test]
    public async Task MisfireInstructionCode_TakesTheSameNumberAsTheTypedOverload()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithMisfireInstructionCode((int) CronTriggerMisfireInstruction.DoNothing);

        (await jobStore.UpdateTriggerDetails(trigger.Key, update)).Should().BeTrue();

        IOperableTrigger retrieved = (await jobStore.GetTrigger(trigger.Key))!;
        retrieved.MisfireInstructionCode.Should().Be(MisfireInstruction.CronTrigger.DoNothing,
            "the code form is the same value the typed overload carries, just without the family");
    }

    [Test]
    public async Task MisfireInstruction_FromAnotherFamilyIsRejected()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        // Code 2 is in range for a cron trigger, so TriggerBase's own validation passes it and
        // the trigger silently becomes DoNothing. Only the update object knows a simple trigger's
        // policy was meant.
        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithMisfireInstruction(SimpleTriggerMisfireInstruction.NowWithExistingCount);

        Func<Task> act = async () => await jobStore.UpdateTriggerDetails(trigger.Key, update);

        await act.Should().ThrowAsync<JobPersistenceException>().WithMessage("*simple*cron*");

        (await jobStore.GetTrigger(trigger.Key))!.MisfireInstructionCode.Should().Be(
            MisfireInstruction.SmartPolicy,
            "a rejected update must leave the stored trigger alone");
    }

    /// <summary>
    /// The whole family matrix, run against <see cref="RAMJobStore" />. The ADO store runs the same
    /// list in <c>UpdateTriggerDetailsFamilyTest</c>, so the two stores are provably in agreement
    /// about which combinations they accept.
    /// </summary>
    [TestCaseSource(typeof(MisfireInstructionFamilyCases), nameof(MisfireInstructionFamilyCases.Mismatched))]
    public async Task MisfireInstruction_FromAnotherFamilyIsRejected_ForEveryFamilyPair(MisfireInstructionFamilyCase testCase)
    {
        IOperableTrigger trigger = await GivenStoredTrigger(testCase);

        Func<Task> act = async () => await jobStore.UpdateTriggerDetails(trigger.Key, testCase.CreateUpdate());

        await act.Should().ThrowAsync<JobPersistenceException>()
            .WithMessage($"*{testCase.RequestedName}*{testCase.StoredName}*",
                "the message has to name both families, because the whole problem is that the code alone names neither");

        (await jobStore.GetTrigger(trigger.Key))!.MisfireInstructionCode.Should().Be(
            MisfireInstruction.SmartPolicy,
            "a rejected update must leave the stored trigger alone");
    }

    [TestCaseSource(typeof(MisfireInstructionFamilyCases), nameof(MisfireInstructionFamilyCases.Matching))]
    public async Task MisfireInstruction_FromItsOwnFamilyIsApplied(MisfireInstructionFamilyCase testCase)
    {
        IOperableTrigger trigger = await GivenStoredTrigger(testCase);

        (await jobStore.UpdateTriggerDetails(trigger.Key, testCase.CreateUpdate())).Should().BeTrue();

        (await jobStore.GetTrigger(trigger.Key))!.MisfireInstructionCode.Should().Be(testCase.InstructionCode);
    }

    private async Task<IOperableTrigger> GivenStoredTrigger(MisfireInstructionFamilyCase testCase)
    {
        IOperableTrigger trigger = testCase.CreateTrigger(new TriggerKey("t1", "g1"), jobDetail.Key);
        trigger.MisfireInstructionCode.Should().Be(MisfireInstruction.SmartPolicy,
            "the fixture needs a trigger whose instruction the update would visibly change");
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);
        return trigger;
    }

    [Test]
    public async Task MisfireInstruction_IsReadBackFromTheFamilyInterface()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed));

        ICronTrigger retrieved = (ICronTrigger) (await jobStore.GetTrigger(trigger.Key))!;
        retrieved.MisfireInstruction.Should().Be(CronTriggerMisfireInstruction.FireAndProceed);
        retrieved.MisfireInstructionCode.Should().Be((int) CronTriggerMisfireInstruction.FireAndProceed,
            "the typed property and the raw code are two readings of one value");
    }

    [Test]
    public async Task ExecutionGroup_UpdatedOnItsOwn()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.NextFireTimeUtc!.Value;

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithExecutionGroup("heavy"));

        result.Should().BeTrue();
        IOperableTrigger retrieved = (await jobStore.GetTrigger(trigger.Key))!;
        retrieved.ExecutionGroup.Should().Be("heavy",
            "an update carrying only an execution group must still apply it");
        retrieved.NextFireTimeUtc.Should().Be(nextFireBefore);
    }

    [Test]
    public async Task ExecutionGroup_NullClearsTheGroup()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ExecutionGroup = "heavy";
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        (await jobStore.GetTrigger(trigger.Key))!.ExecutionGroup.Should().Be("heavy");

        (await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithExecutionGroup(null)))
            .Should().BeTrue();

        (await jobStore.GetTrigger(trigger.Key))!.ExecutionGroup.Should().BeNull();
    }

    [Test]
    public async Task PreferredNode_UpdatedOnItsOwn()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        DateTimeOffset nextFireBefore = trigger.NextFireTimeUtc!.Value;

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithPreferredNode(PreferredNode.For("nodeB")));

        result.Should().BeTrue();
        IOperableTrigger retrieved = (await jobStore.GetTrigger(trigger.Key))!;
        retrieved.PreferredNode.Should().Be(
            PreferredNode.For("nodeB"),
            "an update carrying only a pin must still apply it - reporting success while storing nothing is the worst of both");
        retrieved.NextFireTimeUtc.Should().Be(nextFireBefore);
    }

    [Test]
    public async Task PreferredNode_None_ClearsExistingPin()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.PreferredNode = PreferredNode.For("nodeA");
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        (await jobStore.GetTrigger(trigger.Key))!.PreferredNode.Should().Be(PreferredNode.For("nodeA"));

        bool result = await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithPreferredNode(PreferredNode.None));

        result.Should().BeTrue();
        (await jobStore.GetTrigger(trigger.Key))!.PreferredNode.Should().Be(PreferredNode.None);
    }

    [Test]
    public async Task PreferredNode_AutoPinSurvivesAsAutomatic()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        await jobStore.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithPreferredNode(PreferredNode.Auto));

        PreferredNode retrieved = (await jobStore.GetTrigger(trigger.Key))!.PreferredNode;
        retrieved.Should().Be(PreferredNode.Auto);
        retrieved.IsAutomatic.Should().BeTrue("an auto pin that hardened into a named one would never be released again");
    }

    [Test]
    public async Task PreferredNode_UpdatedAlongsideOtherProperties()
    {
        DateTimeOffset start = TestDates.EvenMinuteDateAfterNow();
        IOperableTrigger trigger = CreateCronTrigger("t1", "g1", "0/30 * * * * ?", start);
        trigger.ComputeFirstFireTimeUtc(null);
        await jobStore.AddTrigger(trigger, false);

        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("pinned")
            .WithPreferredNode(PreferredNode.For("nodeB"));

        (await jobStore.UpdateTriggerDetails(trigger.Key, update)).Should().BeTrue();

        IOperableTrigger retrieved = (await jobStore.GetTrigger(trigger.Key))!;
        retrieved.Description.Should().Be("pinned");
        retrieved.PreferredNode.Should().Be(PreferredNode.For("nodeB"));
    }

    [Test]
    public async Task SchedulerLevel_UpdateTriggerDetails()
    {
        NameValueCollection config = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "UpdateTriggerDetailsTest",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(config).BuildScheduler();
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("extJob", "extGroup")
                .StoreDurably()
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("extTrigger", "extGroup")
                .ForJob(job)
                .WithCronSchedule("0/30 * * * * ?")
                .Build();

            await scheduler.AddJob(job, new AddJobOptions { Replace = true });
            await scheduler.ScheduleJob(trigger);

            DateTimeOffset nextFireBefore = trigger.NextFireTimeUtc!.Value;

            TriggerDetailsUpdate update = new TriggerDetailsUpdate()
                .WithDescription("via scheduler")
                .WithPriority(8);

            bool result = await scheduler.UpdateTriggerDetails(trigger.Key, update);

            result.Should().BeTrue();
            ITrigger retrieved = (await scheduler.GetTrigger(trigger.Key))!;
            retrieved.Description.Should().Be("via scheduler");
            retrieved.Priority.Should().Be(8);
            retrieved.NextFireTimeUtc.Should().Be(nextFireBefore);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    private IOperableTrigger CreateCronTrigger(string name, string group, string cronExpression, DateTimeOffset startTime)
    {
        return (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, group)
            .ForJob(jobDetail)
            .StartAt(startTime)
            .WithCronSchedule(cronExpression)
            .Build();
    }

    private sealed class TestSignaler : ISchedulerSignaler
    {
        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;
        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;
        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;
        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;
    }
}
