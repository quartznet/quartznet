#nullable enable

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <summary>
/// The default bodies of <see cref="IJobStore.StoreTrigger" /> and <see cref="IScheduler.ScheduleTrigger" />,
/// which a store or scheduler written for an earlier 4.x runs, and the forwarders that must not run them.
/// </summary>
/// <remarks>
/// The defaults compose the members such an implementation already has: read, decide, write. They are
/// not atomic, so the one race they can see — a trigger created between the read and an insert — is
/// read again and decided on, which is asserted here along with every mode.
/// </remarks>
public sealed class TriggerConflictDefaultsTest
{
    private static readonly TriggerKey key = new("order-42", "orders");
    private static readonly DateTimeOffset soon = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset later = soon.AddHours(1);

    [Test]
    public async Task TheStoreDefaultAddsWhenAskedToThrow()
    {
        IJobStore store = StoreWithDefault(existing: null);
        IOperableTrigger trigger = Trigger(soon);

        ScheduleTriggerResult result = await store.StoreTrigger(trigger, TriggerConflict.Throw);

        result.Should().Be(new ScheduleTriggerResult(soon, ScheduleOutcome.Created));
        A.CallTo(() => store.AddTrigger(trigger, default(AddTriggerOptions), A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        // AddTrigger already refuses a taken key, so there is nothing to read first.
        A.CallTo(() => store.GetTrigger(A<TriggerKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task TheStoreDefaultReplacesAndSaysSo()
    {
        IJobStore store = StoreWithDefault(existing: Trigger(soon));
        IOperableTrigger trigger = Trigger(later);

        ScheduleTriggerResult result = await store.StoreTrigger(trigger, TriggerConflict.Replace);

        result.Should().Be(new ScheduleTriggerResult(later, ScheduleOutcome.Replaced));
        A.CallTo(() => store.AddTrigger(trigger, AddTriggerOptions.Replacing, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TheStoreDefaultCreatesWhenNothingIsThere()
    {
        IJobStore store = StoreWithDefault(existing: null);

        ScheduleTriggerResult result = await store.StoreTrigger(Trigger(soon), TriggerConflict.Keep);

        result.Should().Be(new ScheduleTriggerResult(soon, ScheduleOutcome.Created));
    }

    [TestCase(TriggerConflict.Keep)]
    [TestCase(TriggerConflict.KeepEarlier)]
    public async Task TheStoreDefaultKeepsAPendingTrigger(TriggerConflict onConflict)
    {
        IJobStore store = StoreWithDefault(existing: Trigger(soon));

        ScheduleTriggerResult result = await store.StoreTrigger(Trigger(later), onConflict);

        result.Should().Be(new ScheduleTriggerResult(soon, ScheduleOutcome.Kept));
        A.CallTo(() => store.AddTrigger(A<IOperableTrigger>._, A<AddTriggerOptions>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task TheStoreDefaultDecidesAgainWhenTheKeyIsTakenBetweenItsReadAndItsWrite()
    {
        IJobStore store = A.Fake<IJobStore>();
        A.CallTo(() => store.StoreTrigger(A<IOperableTrigger>._, A<TriggerConflict>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => store.GetTrigger(key, A<CancellationToken>._))
            .ReturnsNextFromSequence(new ValueTask<IOperableTrigger?>((IOperableTrigger?) null), new ValueTask<IOperableTrigger?>(Trigger(soon)));
        A.CallTo(() => store.AddTrigger(A<IOperableTrigger>._, default(AddTriggerOptions), A<CancellationToken>._))
            .Throws(() => new ObjectAlreadyExistsException("another caller stored it first"));

        ScheduleTriggerResult result = await store.StoreTrigger(Trigger(later), TriggerConflict.Keep);

        result.Should().Be(new ScheduleTriggerResult(soon, ScheduleOutcome.Kept),
            "the insert lost to a concurrent one, and reading again finds the trigger that won");
    }

    [Test]
    public async Task TheStoreDefaultRefusesAnUndefinedMode()
    {
        IJobStore store = StoreWithDefault(existing: null);

        Func<Task> act = async () => await store.StoreTrigger(Trigger(soon), (TriggerConflict) 9);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task TheSchedulerDefaultSchedulesWhenAskedToThrow()
    {
        IScheduler scheduler = SchedulerWithDefault(existing: null, storedFireTime: soon);
        ITrigger trigger = Trigger(soon);

        ScheduleTriggerResult result = await scheduler.ScheduleTrigger(trigger, TriggerConflict.Throw);

        result.Should().Be(new ScheduleTriggerResult(soon, ScheduleOutcome.Created));
        A.CallTo(() => scheduler.ScheduleJob(trigger, default(ScheduleJobOptions), A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TheSchedulerDefaultReplacesAndSaysSo()
    {
        IScheduler scheduler = SchedulerWithDefault(existing: Trigger(soon), storedFireTime: later);
        ITrigger trigger = Trigger(later);

        ScheduleTriggerResult result = await scheduler.ScheduleTrigger(trigger, TriggerConflict.Replace);

        result.Should().Be(new ScheduleTriggerResult(later, ScheduleOutcome.Replaced));
        A.CallTo(() => scheduler.ScheduleJob(trigger, ScheduleJobOptions.Replacing, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TheSchedulerDefaultKeepsAPendingTrigger()
    {
        IScheduler scheduler = SchedulerWithDefault(existing: Trigger(soon), storedFireTime: later);

        ScheduleTriggerResult result = await scheduler.ScheduleTrigger(Trigger(later), TriggerConflict.Keep);

        result.Should().Be(new ScheduleTriggerResult(soon, ScheduleOutcome.Kept));
        A.CallTo(() => scheduler.ScheduleJob(A<ITrigger>._, A<ScheduleJobOptions>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task TheSchedulerDefaultWorksOutWhenAnUnscheduledTriggerWouldFire()
    {
        IScheduler scheduler = SchedulerWithDefault(existing: Trigger(later), storedFireTime: soon);

        // Built and never stored, so nothing has computed its first fire time yet: the default works it out
        // on a copy, with the trigger's calendar, before it can compare.
        ITrigger earlier = TriggerBuilder.Create().WithIdentity(key).ForJob("job").StartAt(soon).WithCalendarName("business-days").Build();
        A.CallTo(() => scheduler.GetCalendar("business-days", A<CancellationToken>._))
            .Returns(new ValueTask<ICalendar?>(new Quartz.Impl.Calendar.BaseCalendar()));

        ScheduleTriggerResult result = await scheduler.ScheduleTrigger(earlier, TriggerConflict.KeepEarlier);

        result.Should().Be(new ScheduleTriggerResult(soon, ScheduleOutcome.Replaced));
        earlier.NextFireTimeUtc.Should().BeNull("the caller's trigger is handed on as it was given; the copy was computed");
        A.CallTo(() => scheduler.GetCalendar("business-days", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TheSchedulerDefaultDecidesAgainWhenTheKeyIsTakenBetweenItsReadAndItsWrite()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.ScheduleTrigger(A<ITrigger>._, A<TriggerConflict>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => scheduler.GetTrigger(key, A<CancellationToken>._))
            .ReturnsNextFromSequence(new ValueTask<ITrigger?>((ITrigger?) null), new ValueTask<ITrigger?>(Trigger(soon)));
        A.CallTo(() => scheduler.ScheduleJob(A<ITrigger>._, default(ScheduleJobOptions), A<CancellationToken>._))
            .Throws(() => new ObjectAlreadyExistsException("another caller stored it first"));

        ScheduleTriggerResult result = await scheduler.ScheduleTrigger(Trigger(later), TriggerConflict.KeepEarlier);

        result.Should().Be(new ScheduleTriggerResult(soon, ScheduleOutcome.Kept));
    }

    [Test]
    public async Task TheSchedulerDefaultCreatesWhenNothingIsThere()
    {
        IScheduler scheduler = SchedulerWithDefault(existing: null, storedFireTime: soon);

        ScheduleTriggerResult result = await scheduler.ScheduleTrigger(Trigger(soon), TriggerConflict.KeepEarlier);

        result.Should().Be(new ScheduleTriggerResult(soon, ScheduleOutcome.Created));
    }

    [Test]
    public async Task TheDelegatingSchedulerHandsTheModeOn()
    {
        IScheduler inner = A.Fake<IScheduler>();
        A.CallTo(() => inner.ScheduleTrigger(A<ITrigger>._, A<TriggerConflict>._, A<CancellationToken>._))
            .Returns(new ValueTask<ScheduleTriggerResult>(new ScheduleTriggerResult(soon, ScheduleOutcome.Kept)));
        ITrigger trigger = Trigger(later);

        ScheduleTriggerResult result = await new DelegatingScheduler(inner).ScheduleTrigger(trigger, TriggerConflict.Keep);

        result.Outcome.Should().Be(ScheduleOutcome.Kept);
        A.CallTo(() => inner.ScheduleTrigger(trigger, TriggerConflict.Keep, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        // A forwarder that ran the default would read and write through the inner scheduler without its
        // store's lock.
        A.CallTo(() => inner.GetTrigger(A<TriggerKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task TheDelegatingJobStoreHandsTheModeOn()
    {
        IJobStore inner = A.Fake<IJobStore>();
        A.CallTo(() => inner.StoreTrigger(A<IOperableTrigger>._, A<TriggerConflict>._, A<CancellationToken>._))
            .Returns(new ValueTask<ScheduleTriggerResult>(new ScheduleTriggerResult(soon, ScheduleOutcome.Replaced)));
        IOperableTrigger trigger = Trigger(soon);

        ScheduleTriggerResult result = await new DelegatingJobStore(inner).StoreTrigger(trigger, TriggerConflict.KeepEarlier);

        result.Outcome.Should().Be(ScheduleOutcome.Replaced);
        A.CallTo(() => inner.StoreTrigger(trigger, TriggerConflict.KeepEarlier, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => inner.GetTrigger(A<TriggerKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    private static IJobStore StoreWithDefault(IOperableTrigger? existing)
    {
        IJobStore store = A.Fake<IJobStore>();
        A.CallTo(() => store.StoreTrigger(A<IOperableTrigger>._, A<TriggerConflict>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => store.GetTrigger(key, A<CancellationToken>._)).Returns(new ValueTask<IOperableTrigger?>(existing));
        return store;
    }

    private static IScheduler SchedulerWithDefault(ITrigger? existing, DateTimeOffset storedFireTime)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.ScheduleTrigger(A<ITrigger>._, A<TriggerConflict>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => scheduler.GetTrigger(key, A<CancellationToken>._)).Returns(new ValueTask<ITrigger?>(existing));
        A.CallTo(() => scheduler.ScheduleJob(A<ITrigger>._, A<ScheduleJobOptions>._, A<CancellationToken>._))
            .Returns(new ValueTask<DateTimeOffset>(storedFireTime));
        return scheduler;
    }

    private static IOperableTrigger Trigger(DateTimeOffset at)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob("job")
            .StartAt(at)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }
}
