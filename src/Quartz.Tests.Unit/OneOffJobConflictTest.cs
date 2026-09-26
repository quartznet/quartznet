#nullable enable

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="OneOffJobOptions.OnConflict" /> through the one-liner: what a second call under the same name
/// does, what it answers with, and how the older <see cref="OneOffJobOptions.Replace" /> spelling fits.
/// </summary>
public sealed class OneOffJobConflictTest
{
    private static readonly DateTimeOffset soon = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddDays(2);
    private static readonly DateTimeOffset later = soon.AddHours(1);

    private ServiceProvider container = null!;
    private IScheduler scheduler = null!;

    [SetUp]
    public async Task BuildScheduler()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = $"one-off-conflict-{Guid.NewGuid():N}"));
        container = services.BuildServiceProvider();
        scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    [TearDown]
    public async Task ShutDown()
    {
        await scheduler.Shutdown(waitForJobsToComplete: false);
        await container.DisposeAsync();
    }

    [Test]
    public async Task ByDefaultASecondFiringUnderTheNameIsRefused()
    {
        OneOffJobOptions options = new() { Name = "reminder" };

        ScheduledOneOffJob first = await Schedule(soon, options);
        Func<Task> second = async () => await Schedule(later, options);

        first.Outcome.Should().Be(ScheduleOutcome.Created);
        await second.Should().ThrowAsync<ObjectAlreadyExistsException>("the default is what the one-liner has always done");
    }

    [Test]
    public async Task ThePresetReplacesAndSaysSo()
    {
        OneOffJobOptions options = OneOffJobOptions.Replacing("reminder");

        options.OnConflict.Should().Be(TriggerConflict.Replace, "the preset says the mode, which is what composes with `with`");
        options.Replace.Should().BeTrue("the older spelling still reads what the options mean");

        (await Schedule(soon, options)).Outcome.Should().Be(ScheduleOutcome.Created);
        ScheduledOneOffJob second = await Schedule(later, options);

        second.Outcome.Should().Be(ScheduleOutcome.Replaced);
        second.FirstFireTimeUtc.Should().Be(later, "a debounce: the last call is the one that fires");
    }

    [Test]
    public async Task TheOlderReplaceSpellingStillReplaces()
    {
        OneOffJobOptions options = new() { Name = "reminder", Replace = true };

        await Schedule(soon, options);
        ScheduledOneOffJob second = await Schedule(later, options);

        second.Outcome.Should().Be(ScheduleOutcome.Replaced, "Replace = true keeps meaning OnConflict = Replace");
    }

    [Test]
    public async Task KeepAnswersWithTheFiringAlreadyScheduled()
    {
        OneOffJobOptions options = new() { Name = "sync-account-7", OnConflict = TriggerConflict.Keep };

        ScheduledOneOffJob first = await Schedule(soon, options);
        ScheduledOneOffJob second = await Schedule(later, options);

        second.Should().Be(new ScheduledOneOffJob(first.TriggerKey, soon) { Outcome = ScheduleOutcome.Kept },
            "\"make sure this is scheduled\" answers with the firing that is");
        (await scheduler.GetTrigger(first.TriggerKey))!.NextFireTimeUtc.Should().Be(soon);
    }

    [Test]
    public async Task KeepEarlierKeepsWhicheverFiresFirst()
    {
        OneOffJobOptions options = new() { Name = "digest", OnConflict = TriggerConflict.KeepEarlier };

        await Schedule(later, options);
        ScheduledOneOffJob earlier = await Schedule(soon, options);
        ScheduledOneOffJob afterwards = await Schedule(later.AddHours(1), options);

        earlier.Outcome.Should().Be(ScheduleOutcome.Replaced, "an earlier firing moves the deadline closer");
        afterwards.Outcome.Should().Be(ScheduleOutcome.Kept, "a later one never moves it away");
        afterwards.FirstFireTimeUtc.Should().Be(soon);
    }

    [Test]
    public async Task ThePresetComposesWithAnotherMode()
    {
        OneOffJobOptions options = OneOffJobOptions.Replacing("reminder") with { OnConflict = TriggerConflict.Keep };

        options.Replace.Should().BeFalse("the mode was changed, and the older spelling follows it");

        await Schedule(soon, options);
        (await Schedule(later, options)).Outcome.Should().Be(ScheduleOutcome.Kept);
    }

    [Test]
    public async Task ReplaceBesideADifferentModeIsRefusedAndStoresNothing()
    {
        OneOffJobOptions options = new() { Name = "reminder", Replace = true, OnConflict = TriggerConflict.Keep };

        Func<Task> schedule = async () => await Schedule(soon, options);

        await schedule.Should().ThrowAsync<ArgumentException>().WithMessage("*OnConflict*",
            "Replace and OnConflict are one setting, and two answers to it is a mistake to report rather than a guess to make");
        (await scheduler.Exists(SchedulerConstants.ScheduledJobKey<ReminderJob>())).Should().BeFalse(
            "the options are checked before the job the firings hang off is stored");
    }

    [Test]
    public async Task ReplaceBesideReplaceIsNoContradiction()
    {
        OneOffJobOptions options = new() { Name = "reminder", Replace = true, OnConflict = TriggerConflict.Replace };

        await Schedule(soon, options);
        (await Schedule(later, options)).Outcome.Should().Be(ScheduleOutcome.Replaced);
    }

    [Test]
    public async Task AModeThatIsNoneOfTheFourIsRefused()
    {
        OneOffJobOptions options = new() { Name = "reminder", OnConflict = (TriggerConflict) 7 };

        Func<Task> schedule = async () => await Schedule(soon, options);

        await schedule.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task TheSchedulerTheContainerHandsOutDecidesOnTheConflictToo()
    {
        // The container's IScheduler resolves the built scheduler on first use; it must hand the mode on
        // rather than answer by the interface default, which would read and write without the store's lock.
        IScheduler injected = container.GetRequiredService<IScheduler>();
        await injected.AddJob(JobBuilder.Create<PlainJob>().WithIdentity("plain").StoreDurably().Build());

        ITrigger first = TriggerBuilder.Create().WithIdentity("once").ForJob("plain").StartAt(soon).Build();
        ITrigger second = TriggerBuilder.Create().WithIdentity("once").ForJob("plain").StartAt(later).Build();

        (await injected.ScheduleTrigger(first, TriggerConflict.Keep)).Outcome.Should().Be(ScheduleOutcome.Created);
        (await injected.ScheduleTrigger(second, TriggerConflict.Keep)).Should().Be(new ScheduleTriggerResult(soon, ScheduleOutcome.Kept));
    }

    [Test]
    public async Task TheDefaultModeMakesTheCallTheOneLinerAlwaysMade()
    {
        IScheduler fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.TimeProvider).Returns(TimeProvider.System);
        A.CallTo(() => fake.ScheduleJob(A<ITrigger>._, A<ScheduleJobOptions>._, A<CancellationToken>._))
            .Returns(new ValueTask<DateTimeOffset>(soon));

        ScheduledOneOffJob scheduled = await fake.ScheduleJob<ReminderJob, Reminder>(new Reminder("x"), soon);

        scheduled.Outcome.Should().Be(ScheduleOutcome.Created);
        A.CallTo(() => fake.ScheduleJob(A<ITrigger>._, default(ScheduleJobOptions), A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        // A scheduler of one's own, or a test double configured for 4.2, sees the call it always saw.
        A.CallTo(() => fake.ScheduleTrigger(A<ITrigger>._, A<TriggerConflict>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task AnyOtherModeIsTheSchedulersDecision()
    {
        IScheduler fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.TimeProvider).Returns(TimeProvider.System);
        A.CallTo(() => fake.ScheduleTrigger(A<ITrigger>._, A<TriggerConflict>._, A<CancellationToken>._))
            .Returns(new ValueTask<ScheduleTriggerResult>(new ScheduleTriggerResult(soon, ScheduleOutcome.Kept)));

        ScheduledOneOffJob scheduled = await fake.ScheduleJob<ReminderJob, Reminder>(
            new Reminder("x"),
            later,
            new OneOffJobOptions { Name = "reminder", OnConflict = TriggerConflict.KeepEarlier });

        scheduled.Should().Be(new ScheduledOneOffJob(new TriggerKey("reminder", nameof(ReminderJob)), soon) { Outcome = ScheduleOutcome.Kept });
        A.CallTo(() => fake.ScheduleTrigger(A<ITrigger>._, TriggerConflict.KeepEarlier, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    private ValueTask<ScheduledOneOffJob> Schedule(DateTimeOffset at, OneOffJobOptions options)
    {
        return scheduler.ScheduleJob<ReminderJob, Reminder>(new Reminder("customer-7"), at, options);
    }

    public sealed record Reminder(string Customer);

    public sealed class PlainJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class ReminderJob : IJob<Reminder>
    {
        public ValueTask Execute(IJobExecutionContext context, Reminder input, CancellationToken cancellationToken = default) => default;
    }
}
