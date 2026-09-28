#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

#nullable enable

using System.Collections.Concurrent;

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="SchedulerBackfillExtensions.Backfill" />: which slots a range holds, what each one-shot
/// carries, what is refused before anything is written, and what a second run of the same range does.
/// </summary>
/// <remarks>
/// Every scheduler here runs on a <see cref="FakeTimeProvider" /> that nobody advances, so "now" is the
/// same instant for the whole test and the refusal of a range ending after it is exact.
/// </remarks>
public sealed class SchedulerBackfillExtensionsTest
{
    public enum StoreKind
    {
        InMemory,
        Sqlite,
    }

    private static readonly DateTimeOffset now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset day = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset since = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JobKey jobKey = new("export", "reports");
    private static readonly TriggerKey hourlyKey = new("hourly", "reports");

    /// <summary>
    /// How long a test waits for a scheduler that is running before calling it stuck. Never a measurement.
    /// </summary>
    private static readonly TimeSpan deadline = TimeSpan.FromSeconds(30);

    private readonly List<IAsyncDisposable> owned = [];
    private readonly List<SqliteTestDatabase> databases = [];
    private FakeTimeProvider clock = null!;

    [SetUp]
    public void SetUp()
    {
        clock = new FakeTimeProvider(now);
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (IAsyncDisposable disposable in owned)
        {
            await disposable.DisposeAsync();
        }

        owned.Clear();

        foreach (SqliteTestDatabase database in databases)
        {
            database.Dispose();
        }

        databases.Clear();
    }

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task EveryCronSlotInTheRangeGetsAOneShotOfItsOwn(StoreKind store)
    {
        IScheduler scheduler = await Scheduler(store);
        await scheduler.ScheduleJob(Job(), Hourly());

        BackfillResult result = await scheduler.Backfill(hourlyKey, day, day.AddHours(6));

        result.SlotsFound.Should().Be(6, "an hourly trigger has six fire times in six hours");
        result.Scheduled.Should().Be(6);
        result.AlreadyScheduled.Should().Be(0);
        result.FirstSlot.Should().Be(day, "the range includes its start");
        result.LastSlot.Should().Be(day.AddHours(5), "and excludes its end, which is the seventh fire time");
        result.ScheduledTriggers.Should().Equal(
            Enumerable.Range(0, 6).Select(hour => new TriggerKey($"hourly@2026-09-09T{hour:00}:00:00Z", "backfill:reports")),
            "each slot's trigger is named after the slot, earliest first, in the original's group behind the backfill prefix");

        foreach (TriggerKey key in result.ScheduledTriggers)
        {
            ISimpleTrigger stored = (ISimpleTrigger) (await scheduler.GetTrigger(key))!;
            stored.JobKey.Should().Be(jobKey, "a backfill fires the original trigger's job");
            stored.RepeatCount.Should().Be(0, "one firing per slot");
            stored.MisfireInstruction.Should().Be(SimpleTriggerMisfireInstruction.FireNow,
                "a slot that could not start at once still has to run, which is the point of scheduling it");
            stored.StartTimeUtc.Should().Be(now, "with no spacing every slot starts now");
        }
    }

    /// <summary>
    /// Scheduled while its start was still ahead: a simple trigger stored with a start in the past starts
    /// from the moment it is stored, and its slots are counted from its start as it is now.
    /// </summary>
    [Test]
    public async Task ASimpleTriggersSlotsStopAtItsStartAndEndTime()
    {
        clock = new FakeTimeProvider(day);
        IScheduler scheduler = await Scheduler();
        ITrigger everyTenMinutes = TriggerBuilder.Create(clock)
            .WithIdentity("ten", "reports")
            .ForJob(jobKey)
            .StartAt(day.AddMinutes(65))
            .EndAt(day.AddMinutes(115))
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMinutes(10)).RepeatForever())
            .Build();
        await scheduler.ScheduleJob(Job(), everyTenMinutes);
        clock.SetUtcNow(now);

        BackfillResult result = await scheduler.Backfill(everyTenMinutes.Key, day, day.AddHours(3));

        result.SlotsFound.Should().Be(6, "01:05 through 01:55: nothing before the start time, and the end time is the last instant it may fire");
        result.FirstSlot.Should().Be(day.AddMinutes(65));
        result.LastSlot.Should().Be(day.AddMinutes(115), "an end time is inclusive, for every trigger type");
    }

    /// <summary>
    /// A trigger built without a start time starts when it is built, so it has no slots before that: the
    /// start time bounds a backfill the way it bounds the trigger's own firings.
    /// </summary>
    [Test]
    public async Task ATriggerHasNoSlotsBeforeItsStartTime()
    {
        IScheduler scheduler = await Scheduler();
        ITrigger startedToday = TriggerBuilder.Create(clock)
            .WithIdentity(hourlyKey)
            .ForJob(jobKey)
            .WithCronSchedule("0 0 * * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc))
            .Build();
        await scheduler.ScheduleJob(Job(), startedToday);

        BackfillResult result = await scheduler.Backfill(hourlyKey, now.AddDays(-1), now);

        result.SlotsFound.Should().Be(0, "the builder's default start time is now, and nothing before a trigger's start is one of its fire times");
    }

    /// <summary>
    /// A calendar-interval trigger looks a second past the time it is asked about, so a walk that began a
    /// tick before the range would step over a slot sitting exactly on its start.
    /// </summary>
    [Test]
    public async Task ACalendarIntervalSlotOnTheRangesStartIsIncluded()
    {
        IScheduler scheduler = await Scheduler();
        ITrigger daily = TriggerBuilder.Create(clock)
            .WithIdentity("daily", "reports")
            .ForJob(jobKey)
            .StartAt(new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero))
            .WithCalendarIntervalSchedule(schedule => schedule.WithInterval(1, IntervalUnit.Day))
            .Build();
        await scheduler.ScheduleJob(Job(), daily);

        DateTimeOffset from = new(2026, 9, 5, 3, 0, 0, TimeSpan.Zero);
        BackfillResult result = await scheduler.Backfill(daily.Key, from, from.AddDays(3));

        result.SlotsFound.Should().Be(3);
        result.FirstSlot.Should().Be(from, "the slot on the range's start is in the range");
        result.LastSlot.Should().Be(from.AddDays(2));
    }

    [Test]
    public async Task ASlotTheTriggersCalendarExcludesIsDropped()
    {
        IScheduler scheduler = await Scheduler();
        HolidayCalendar holidays = new() { TimeZone = TimeZoneInfo.Utc };
        holidays.AddExcludedDay(new DateOnly(2026, 9, 6));
        await scheduler.AddCalendar("holidays", holidays);

        ITrigger nightly = TriggerBuilder.Create(clock)
            .WithIdentity("nightly", "reports")
            .ForJob(jobKey)
            .StartAt(since)
            .WithCalendarName("holidays")
            .WithCronSchedule("0 0 3 * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc))
            .Build();
        await scheduler.ScheduleJob(Job(), nightly);

        BackfillResult result = await scheduler.Backfill(nightly.Key, new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero));

        result.ScheduledTriggers.Select(key => key.Name).Should().Equal(
            ["nightly@2026-09-05T03:00:00Z", "nightly@2026-09-07T03:00:00Z"],
            "GetFireTimeAfter does not consult the calendar, and the trigger would not have fired on the holiday either");

        ITrigger stored = (await scheduler.GetTrigger(result.ScheduledTriggers[0]))!;
        stored.CalendarName.Should().BeNull("the slot has already been checked against the calendar, and it runs now");
    }

    [Test]
    public async Task TheOriginalsDataPriorityRetryPolicyNodeAndExecutionGroupAreCarried()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), TriggerBuilder.Create(clock)
            .WithIdentity(hourlyKey)
            .ForJob(jobKey)
            .UsingJobData("region", "eu-west")
            .WithPriority(8)
            .WithExecutionGroup("imports")
            .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(1)))
            .WithPreferredNode(PreferredNode.For("node-a"))
            .StartAt(since)
            .WithCronSchedule("0 0 * * * ?")
            .Build());

        BackfillResult result = await scheduler.Backfill(hourlyKey, day, day.AddHours(1));

        ITrigger stored = (await scheduler.GetTrigger(result.ScheduledTriggers.Single()))!;
        stored.JobDataMap.GetString("region").Should().Be("eu-west", "a job reads its trigger's data the same way on a backfilled firing");
        stored.JobDataMap.GetString(SchedulerConstants.BackfillOriginalFireTime).Should().Be("2026-09-09T00:00:00.0000000+00:00",
            "the slot travels as a round-trip string, which every store and wire format keeps");
        stored.Priority.Should().Be(8);
        stored.ExecutionGroup.Should().Be("imports", "the firing counts against the limits the original's firings count against");
        stored.RetryPolicy.Should().Be(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(1)));
        stored.PreferredNode.Should().Be(PreferredNode.For("node-a"));
        stored.Description.Should().Be("Backfill of reports.hourly for 2026-09-09T00:00:00Z");
    }

    [Test]
    public async Task AnExecutionGroupInTheOptionsReplacesTheOriginalsAndIsStoredAsWritten()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), TriggerBuilder.Create(clock)
            .WithIdentity(hourlyKey)
            .ForJob(jobKey)
            .WithExecutionGroup("imports")
            .StartAt(since)
            .WithCronSchedule("0 0 * * * ?")
            .Build());

        BackfillResult result = await scheduler.Backfill(hourlyKey, day, day.AddHours(1), new BackfillOptions { ExecutionGroup = " backfill{low} " });

        ITrigger stored = (await scheduler.GetTrigger(result.ScheduledTriggers.Single()))!;
        stored.ExecutionGroup.Should().Be("backfill{low}",
            "a backfill can be kept to a group of its own, and a name is a name: its braces are not placeholders");
    }

    [Test]
    public async Task RunningTheSameRangeAgainSkipsTheSlotsAlreadyScheduledAndCountsThem()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Hourly());

        await scheduler.Backfill(hourlyKey, day, day.AddHours(6));
        BackfillResult again = await scheduler.Backfill(hourlyKey, day.AddHours(3), day.AddHours(9));

        again.SlotsFound.Should().Be(6);
        again.AlreadyScheduled.Should().Be(3, "03:00 to 05:00 were scheduled by the first run and have not fired");
        again.Scheduled.Should().Be(3);
        again.ScheduledTriggers.Select(key => key.Name).Should().Equal(
            "hourly@2026-09-09T06:00:00Z", "hourly@2026-09-09T07:00:00Z", "hourly@2026-09-09T08:00:00Z");

        List<TriggerKey> backfilled = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("backfill:reports"));
        backfilled.Should().HaveCount(9, "a slot is scheduled once however many runs name it");
    }

    [Test]
    public async Task SpacingStaggersTheStartsFromNow()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Hourly());

        BackfillResult result = await scheduler.Backfill(hourlyKey, day, day.AddHours(3), new BackfillOptions { Spacing = TimeSpan.FromMinutes(10) });

        List<DateTimeOffset> starts = [];
        foreach (TriggerKey key in result.ScheduledTriggers)
        {
            starts.Add((await scheduler.GetTrigger(key))!.StartTimeUtc);
        }

        starts.Should().Equal([now, now.AddMinutes(10), now.AddMinutes(20)], "slot i starts at now plus i times the spacing");
    }

    [Test]
    public async Task ARangeThatEndsWhereItStartsOrBeforeIsRefused()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Hourly());

        Func<Task> empty = () => scheduler.Backfill(hourlyKey, day, day).AsTask();
        Func<Task> reversed = () => scheduler.Backfill(hourlyKey, day.AddHours(1), day).AsTask();

        (await empty.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("to");
        await reversed.Should().ThrowAsync<ArgumentException>().WithMessage("The range must end after it starts*Nothing was scheduled.*");
        await NothingWasBackfilled(scheduler);
    }

    [Test]
    public async Task ARangeEndingAfterNowIsRefusedBecauseTheTriggerFiresTheFutureItself()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Hourly());

        Func<Task> act = () => scheduler.Backfill(hourlyKey, now.AddHours(-2), now.AddTicks(1)).AsTask();

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*after the scheduler's current time*would fire those slots twice*");
        await NothingWasBackfilled(scheduler);

        BackfillResult upToNow = await scheduler.Backfill(hourlyKey, now.AddHours(-2), now);
        upToNow.SlotsFound.Should().Be(2, "a range ending exactly now is in the past: its end is excluded");
    }

    [Test]
    public async Task ARangeHoldingMoreSlotsThanAllowedIsRefusedWholeAndSaysHowMany()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Hourly());

        Func<Task> act = () => scheduler.Backfill(hourlyKey, day, day.AddDays(1), new BackfillOptions { MaxSlots = 10 }).AsTask();

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("The range holds 24 slots of trigger 'reports.hourly', more than BackfillOptions.MaxSlots (10) allows*",
                "the count is what tells an operator how far to narrow the range");
        await NothingWasBackfilled(scheduler);
    }

    [Test]
    public async Task ARangeTooLargeToCountSaysMoreThanTheCeiling()
    {
        IScheduler scheduler = await Scheduler();
        ITrigger everySecond = TriggerBuilder.Create(clock)
            .WithIdentity("every-second", "reports")
            .ForJob(jobKey)
            .StartAt(since)
            .WithCronSchedule("* * * * * ?")
            .Build();
        await scheduler.ScheduleJob(Job(), everySecond);

        Func<Task> act = () => scheduler.Backfill(everySecond.Key, day.AddDays(-1), day.AddDays(1)).AsTask();

        await act.Should().ThrowAsync<ArgumentException>().WithMessage($"The range holds more than {Backfilling.CountCeiling} slots*",
            "counting two days of seconds to the last one would cost more than the answer is worth");
    }

    [Test]
    public async Task AMissingTriggerIsRefusedAsAnObjectThatDoesNotExist()
    {
        IScheduler scheduler = await Scheduler();

        Func<Task> act = () => scheduler.Backfill(new TriggerKey("ghost", "reports"), day, day.AddHours(1)).AsTask();

        await act.Should().ThrowAsync<ObjectDoesNotExistException>().WithMessage("Trigger 'reports.ghost' does not exist*");
        await NothingWasBackfilled(scheduler);
    }

    [TestCase(0, 0, null, "BackfillOptions.MaxSlots must be at least 1, was 0.*")]
    [TestCase(10, -1, null, "BackfillOptions.Spacing must not be negative*")]
    [TestCase(10, 0, "*", "BackfillOptions.ExecutionGroup '*' is reserved*")]
    public async Task OptionsOutOfRangeAreRefused(int maxSlots, int spacingMinutes, string? executionGroup, string message)
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Hourly());
        BackfillOptions options = new() { MaxSlots = maxSlots, Spacing = TimeSpan.FromMinutes(spacingMinutes), ExecutionGroup = executionGroup };

        Func<Task> act = () => scheduler.Backfill(hourlyKey, day, day.AddHours(1), options).AsTask();

        (await act.Should().ThrowAsync<ArgumentException>().WithMessage(message)).Which.ParamName.Should().Be("options");
        await NothingWasBackfilled(scheduler);
    }

    [Test]
    public async Task ASpacingThatStartsTheLastSlotPastTheEndOfTimeIsRefused()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Hourly());

        Func<Task> act = () => scheduler.Backfill(hourlyKey, day, day.AddHours(2), new BackfillOptions { Spacing = TimeSpan.MaxValue }).AsTask();

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*starts slot 1 past the largest time there is*");
        await NothingWasBackfilled(scheduler);
    }

    [Test]
    public async Task ARangeWithNoSlotsSchedulesNothingAndSaysSo()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Hourly());

        BackfillResult result = await scheduler.Backfill(hourlyKey, day.AddMinutes(1), day.AddMinutes(59));

        result.SlotsFound.Should().Be(0, "an hourly trigger has no fire time strictly inside an hour");
        result.Scheduled.Should().Be(0);
        result.AlreadyScheduled.Should().Be(0);
        result.FirstSlot.Should().BeNull();
        result.LastSlot.Should().BeNull();
    }

    [Test]
    public async Task ARangeFromTheEarliestTimeThereIsIsWalkedFromTheTriggersStart()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), TriggerBuilder.Create(clock)
            .WithIdentity(hourlyKey)
            .ForJob(jobKey)
            .StartAt(day)
            .WithCronSchedule("0 0 * * * ?")
            .Build());

        BackfillResult result = await scheduler.Backfill(hourlyKey, DateTimeOffset.MinValue, day.AddHours(2));

        result.SlotsFound.Should().Be(2, "nothing before the trigger's start time is a slot, and a second before the earliest time is not a time");
    }

    /// <summary>
    /// A <see cref="DisallowConcurrentExecutionAttribute" /> job's backfilled firings are held back behind
    /// one another by the store, as its scheduled firings are, and each reads the slot it stands in for.
    /// </summary>
    [Test]
    public async Task ADisallowConcurrentJobsBackfillRunsOneFiringAtATime()
    {
        SerialJob.Reset(expected: 3);
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(JobBuilder.Create<SerialJob>().WithIdentity(jobKey).Build(), Hourly());

        BackfillResult result = await scheduler.Backfill(hourlyKey, day, day.AddHours(3));
        await scheduler.Start();

        Task finished = await Task.WhenAny(SerialJob.Done.Task, Task.Delay(deadline));
        finished.Should().BeSameAs(SerialJob.Done.Task, "the three backfilled slots are due now, on a clock that does not move");

        SerialJob.MaxRunning.Should().Be(1, "the job disallows concurrent execution, and a backfill's firings are its firings");
        SerialJob.Slots.Should().BeEquivalentTo([day, day.AddHours(1), day.AddHours(2)],
            "each firing reads the slot its trigger was scheduled for, whichever order they ran in");
        result.Scheduled.Should().Be(3);
    }

    [Test]
    public void TheSlotReaderAnswersTheSlotOnlyForABackfilledFiring()
    {
        ITrigger backfilled = TriggerBuilder.Create()
            .WithIdentity("hourly@2026-09-09T00:00:00Z", "backfill:reports")
            .ForJob(jobKey)
            .UsingJobData(SchedulerConstants.BackfillOriginalFireTime, "2026-09-09T00:00:00.0000000+00:00")
            .StartNow()
            .Build();
        ITrigger ordinary = Hourly();

        IJobExecutionContext backfilledContext = JobExecutionContextBuilder.For(new SerialJob()).WithTrigger(backfilled).Build();
        IJobExecutionContext ordinaryContext = JobExecutionContextBuilder.For(new SerialJob()).WithTrigger(ordinary).Build();

        backfilledContext.GetBackfillSlot().Should().Be(day);
        ordinaryContext.GetBackfillSlot().Should().BeNull("a firing that is not a backfill stands in for nothing");
    }

    [Test]
    public void DefaultOptionsAllowAThousandSlotsStartingNow()
    {
        BackfillOptions unset = default;

        unset.MaxSlots.Should().Be(BackfillOptions.DefaultMaxSlots, "omitting the argument has the same ceiling as naming nothing");
        new BackfillOptions().MaxSlots.Should().Be(1000);
        new BackfillOptions { MaxSlots = 5 }.MaxSlots.Should().Be(5);
        unset.Spacing.Should().Be(TimeSpan.Zero);
        unset.ExecutionGroup.Should().BeNull();
    }

    [TestCase(0, "hourly@2026-09-09T00:00:00Z")]
    [TestCase(500, "hourly@2026-09-09T00:00:00.5Z")]
    public void ASlotsNameHasAFractionOnlyWhenTheSlotHasOne(int milliseconds, string expected)
    {
        Backfilling.SlotName("hourly", day.AddMilliseconds(milliseconds).ToOffset(TimeSpan.FromHours(3))).Should().Be(expected,
            "the name is the slot in UTC, and two slots a second apart or less still get names of their own");
    }

    [Test]
    public void AWalkEndsWhenTheTriggerStopsMovingForward()
    {
        ITrigger stuck = A.Fake<ITrigger>();
        A.CallTo(() => stuck.GetFireTimeAfter(A<DateTimeOffset?>._)).Returns(day);

        SlotCount found = Backfilling.Walk(stuck, calendar: null, day, day.AddHours(1), keep: 10, countUpTo: 100);

        found.Count.Should().Be(1, "a trigger answering the time it was asked about would otherwise be asked forever");
        found.Slots.Should().Equal(day);
    }

    [Test]
    public async Task ACalendarThatIsGoneIsRefusedBeforeAnythingIsScheduled()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.TimeProvider).Returns(clock);
        A.CallTo(() => scheduler.GetTrigger(hourlyKey, A<CancellationToken>._))
            .Returns(TriggerBuilder.Create(clock).WithIdentity(hourlyKey).ForJob(jobKey).StartAt(since).WithCalendarName("gone").WithCronSchedule("0 0 * * * ?").Build());
        A.CallTo(() => scheduler.GetCalendar("gone", A<CancellationToken>._)).Returns((ICalendar?) null);

        Func<Task> act = () => scheduler.Backfill(hourlyKey, day, day.AddHours(1)).AsTask();

        await act.Should().ThrowAsync<ObjectDoesNotExistException>().WithMessage("Calendar 'gone', which trigger 'reports.hourly' names, does not exist*");
        A.CallTo(() => scheduler.ScheduleJob(A<ITrigger>._, A<ScheduleJobOptions>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// A slot another caller schedules between the existence check and this call's own write is refused
    /// by the store, and that refusal is the answer the check would have given.
    /// </summary>
    [Test]
    public async Task ASlotStoredBetweenTheCheckAndTheWriteIsCountedAsAlreadyScheduled()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.TimeProvider).Returns(clock);
        A.CallTo(() => scheduler.GetTrigger(hourlyKey, A<CancellationToken>._)).Returns(Hourly());
        A.CallTo(() => scheduler.GetTriggers(A<IReadOnlyCollection<TriggerKey>>._, A<CancellationToken>._)).Returns(new List<ITrigger>());
        A.CallTo(() => scheduler.ScheduleJob(A<ITrigger>.That.Matches(t => t.Key.Name == "hourly@2026-09-09T01:00:00Z"), A<ScheduleJobOptions>._, A<CancellationToken>._))
            .Throws(new ObjectAlreadyExistsException("somebody else stored it"));

        BackfillResult result = await scheduler.Backfill(hourlyKey, day, day.AddHours(2));

        result.AlreadyScheduled.Should().Be(1);
        result.ScheduledTriggers.Select(key => key.Name).Should().Equal("hourly@2026-09-09T00:00:00Z");
    }

    private async Task NothingWasBackfilled(IScheduler scheduler)
    {
        List<TriggerKey> backfilled = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith(SchedulerConstants.BackfillGroupPrefix));
        backfilled.Should().BeEmpty("a refusal is decided before anything is written");
    }

    private async Task<IScheduler> Scheduler(StoreKind store = StoreKind.InMemory)
    {
        SqliteTestDatabase? database = null;
        if (store == StoreKind.Sqlite)
        {
            database = new SqliteTestDatabase("backfill");
            databases.Add(database);
        }

        StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
            .Create(q =>
            {
                q.UseTimeProvider(clock);
                q.ConfigureScheduler(options => options.InstanceName = $"backfill-{Guid.NewGuid():N}");

                if (database is not null)
                {
                    q.UsePersistentStore(persistent =>
                    {
                        persistent.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                        persistent.ProvisionSchema();
                    });
                }
                else
                {
                    q.UseInMemoryStore();
                }
            })
            .Build();

        owned.Add(factory);
        return await factory.GetScheduler();
    }

    private static IJobDetail Job()
    {
        return JobBuilder.Create<SerialJob>().WithIdentity(jobKey).Build();
    }

    /// <summary>
    /// An hourly cron trigger that started on the first of the month, so the days the tests backfill are
    /// days it had.
    /// </summary>
    private ITrigger Hourly()
    {
        return TriggerBuilder.Create(clock)
            .WithIdentity(hourlyKey)
            .ForJob(jobKey)
            .StartAt(since)
            .WithCronSchedule("0 0 * * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc))
            .Build();
    }

    [DisallowConcurrentExecution]
    private sealed class SerialJob : IJob
    {
        private static int running;
        private static int maxRunning;
        private static int expected;

        public static TaskCompletionSource Done { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static ConcurrentQueue<DateTimeOffset?> Slots { get; private set; } = new();

        public static int MaxRunning => Volatile.Read(ref maxRunning);

        public static void Reset(int expected)
        {
            running = 0;
            maxRunning = 0;
            SerialJob.expected = expected;
            Slots = new ConcurrentQueue<DateTimeOffset?>();
            Done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            int nowRunning = Interlocked.Increment(ref running);
            int seen;
            while (nowRunning > (seen = Volatile.Read(ref maxRunning)) && Interlocked.CompareExchange(ref maxRunning, nowRunning, seen) != seen)
            {
            }

            // Long enough that a second firing started beside this one would be seen running.
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            Slots.Enqueue(context.GetBackfillSlot());
            Interlocked.Decrement(ref running);

            if (Slots.Count >= expected)
            {
                Done.TrySetResult();
            }
        }
    }
}
