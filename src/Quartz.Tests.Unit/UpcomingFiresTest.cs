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

using System.Diagnostics;

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="UpcomingFires" />: every fire of every trigger in a window, earliest first, computed over the members
/// every scheduler has — the dashboard's Agenda (#4438).
/// </summary>
/// <remarks>
/// The schedulers are never started, so nothing fires and nothing misfires while a case reads: a trigger's stored
/// next fire time is the one it was scheduled with.
/// </remarks>
public sealed class UpcomingFiresTest
{
    public enum StoreKind
    {
        InMemory,
        Sqlite,
    }

    /// <summary>The scheduler's clock.</summary>
    private static readonly DateTimeOffset now = new(2026, 11, 25, 0, 0, 0, TimeSpan.Zero);

    private static readonly JobKey jobKey = new("export", "reports");

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

    /// <summary>
    /// The agenda is the union of each trigger's own fire times in the window, merged earliest first with ties by
    /// trigger group then name, and the window's end is excluded: each trigger here fires exactly at it.
    /// </summary>
    [Test]
    public async Task TheFiresAreEachTriggersOwnMergedEarliestFirstAndTheEndIsExcluded([Values] StoreKind store)
    {
        IScheduler scheduler = await Scheduler(store);
        await ScheduleThree(scheduler, calendarName: null);
        DateTimeOffset to = now.AddHours(3);

        UpcomingFiresResult result = await Served(scheduler, new UpcomingFiresQuery(null, to, null, UpcomingFires.MaxTake));

        List<(DateTimeOffset, string, string)> expected = [];
        foreach (TriggerKey key in ThreeKeys)
        {
            ITrigger stored = (await scheduler.GetTrigger(key))!;
            List<DateTimeOffset> own = TriggerFireTimes.ComputeBetween(stored, null, now, to);
            own.Should().Contain(to, $"{key} fires exactly at the window's end, which ComputeBetween includes");
            expected.AddRange(own.Where(time => time < to).Select(time => (time.ToUniversalTime(), key.Group, key.Name)));
        }

        expected.Sort(static (x, y) =>
        {
            int byTime = x.Item1.CompareTo(y.Item1);
            int byGroup = byTime != 0 ? byTime : string.CompareOrdinal(x.Item2, y.Item2);
            return byGroup != 0 ? byGroup : string.CompareOrdinal(x.Item3, y.Item3);
        });

        result.Fires.Select(fire => (fire.FireTimeUtc, fire.TriggerKey.Group, fire.TriggerKey.Name)).Should().Equal(expected,
            "the window is half-open, so two windows that meet never list the time they meet at twice");
        result.Fires.Take(3).Select(fire => fire.TriggerKey.ToString()).Should().Equal(
            ["alerts.every-20", "alerts.interval", "reports.hourly"],
            "three triggers fire at the window's start, and a tie goes by group, then name");
        result.FromUtc.Should().Be(now);
        result.ToUtc.Should().Be(to);
        result.CutAtUtc.Should().BeNull("every fire in the window is listed");
        result.TriggersNotComputed.Should().Be(0);
        result.Fires.Should().AllSatisfy(fire =>
        {
            fire.FireTimeUtc.Offset.Should().Be(TimeSpan.Zero);
            fire.JobKey.Should().Be(jobKey);
            fire.State.Should().Be(TriggerState.Normal);
            fire.ScheduleDescription.Should().NotBeNullOrEmpty();
            fire.OnlyTheNext.Should().BeFalse();
        });
    }

    [Test]
    public async Task ACalendarRemovesTheFiresItExcludes()
    {
        IScheduler scheduler = await Scheduler();
        DailyCalendar maintenance = new(new TimeOnly(2, 0), new TimeOnly(2, 59, 59, 999)) { TimeZone = TimeZoneInfo.Utc };
        await scheduler.AddCalendar("maintenance", maintenance);
        await ScheduleThree(scheduler, calendarName: "maintenance");

        UpcomingFiresResult result = await Served(scheduler, new UpcomingFiresQuery(null, now.AddHours(3), null, UpcomingFires.MaxTake));

        result.Fires.Should().NotBeEmpty();
        result.Fires.Should().NotContain(fire => fire.FireTimeUtc.Hour == 2, "the calendar excludes 02:00 to 03:00");
        result.Fires.Should().Contain(fire => fire.FireTimeUtc.Hour == 1);
    }

    [Test]
    public async Task ATakeCutsTheListAtItsLastFireAndKeepsItsOrder([Values] StoreKind store)
    {
        IScheduler scheduler = await Scheduler(store);
        await ScheduleThree(scheduler, calendarName: null);
        DateTimeOffset to = now.AddHours(3);

        UpcomingFiresResult whole = await Served(scheduler, new UpcomingFiresQuery(null, to, null, UpcomingFires.MaxTake));
        UpcomingFiresResult cut = await Served(scheduler, new UpcomingFiresQuery(null, to, null, 10));

        whole.Fires.Should().HaveCountGreaterThan(10);
        cut.Fires.Should().Equal(whole.Fires.Take(10), "a cut list is the whole list's start");
        cut.CutAtUtc.Should().Be(cut.Fires[^1].FireTimeUtc, "fires at or after the last one listed may be missing");
    }

    [Test]
    public async Task ATakeThatListsEveryFireIsNotACut()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Simple("quarterly", "reports", now, TimeSpan.FromMinutes(15)));

        UpcomingFiresResult result = await Served(scheduler, new UpcomingFiresQuery(null, now.AddHours(1), null, 4));

        result.Fires.Should().HaveCount(4);
        result.CutAtUtc.Should().BeNull("the window holds exactly the four fires asked for");
    }

    [TestCase(7 * 24 * 60 + 1, 1000, "longer than 7 days")]
    [TestCase(0, 1000, "not after the scheduler's current time")]
    [TestCase(-60, 1000, "History lists what ran")]
    [TestCase(60, 0, "take must be from 1 to 1000, was 0")]
    [TestCase(60, 1001, "take must be from 1 to 1000, was 1001")]
    public async Task AWindowOrATakeOutOfRangeIsRefusedBeforeAnythingIsRead(int toMinutes, int take, string words)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.TimeProvider).Returns(clock);

        UpcomingFiresOutcome outcome = await UpcomingFires.Compose(scheduler, new UpcomingFiresQuery(null, now.AddMinutes(toMinutes), null, take), CancellationToken.None);

        outcome.Result.Should().BeNull();
        outcome.Refusal.Should().Contain(words);
        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// A refusal is read by a person on every carrier, so its instants say the minute in UTC, the second only when there
    /// is one, and never a round-trip timestamp.
    /// </summary>
    [Test]
    public async Task ARefusalSaysItsInstantsReadably()
    {
        IScheduler scheduler = await Scheduler();
        clock.Advance(TimeSpan.FromSeconds(30));

        UpcomingFiresOutcome past = await UpcomingFires.Query(scheduler, new UpcomingFiresQuery(now.AddDays(-3), now.AddHours(-14), null, 10), CancellationToken.None);
        UpcomingFiresOutcome tooLong = await UpcomingFires.Query(scheduler, new UpcomingFiresQuery(now.AddHours(1), now.AddDays(9), null, 10), CancellationToken.None);

        past.Refusal.Should().Be(
            "The window ends at 2026-11-24 10:00 UTC, which is not after the scheduler's current time, 2026-11-25 00:00:30 UTC. "
            + "The agenda lists what will fire; History lists what ran.");
        tooLong.Refusal.Should().Be("The window from 2026-11-25 01:00 UTC to 2026-12-04 00:00 UTC is longer than 7 days: narrow it.");
    }

    [Test]
    public async Task AWindowEndingMoreThanThirtyDaysAheadIsRefused()
    {
        IScheduler scheduler = await Scheduler();

        UpcomingFiresOutcome outcome = await UpcomingFires.Query(scheduler, new UpcomingFiresQuery(now.AddDays(25), now.AddDays(31), null, 10), CancellationToken.None);

        outcome.Refusal.Should().Contain("more than 30 days after the scheduler's current time");
    }

    [Test]
    public async Task AWindowThatEndsBeforeItStartsIsRefused()
    {
        IScheduler scheduler = await Scheduler();

        UpcomingFiresOutcome outcome = await UpcomingFires.Query(scheduler, new UpcomingFiresQuery(now.AddHours(2), now.AddHours(1), null, 10), CancellationToken.None);

        outcome.Refusal.Should().Contain("must end after it starts");
    }

    [Test]
    public async Task AStartBeforeTheSchedulersNowIsReadAsNow()
    {
        // Scheduled an hour ago, and never started since: the trigger is overdue by an hour.
        clock = new FakeTimeProvider(now.AddHours(-1));
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Cron("quarterly", "reports", now.AddHours(-1), "0 0/15 * * * ?"));
        clock.Advance(TimeSpan.FromMinutes(65));
        (await scheduler.GetTrigger(new TriggerKey("quarterly", "reports")))!.NextFireTimeUtc.Should().Be(now.AddHours(-1));

        UpcomingFiresResult result = await Served(scheduler, new UpcomingFiresQuery(now.AddDays(-1), now.AddHours(1), null, 100));

        result.FromUtc.Should().Be(now.AddMinutes(5), "the agenda lists what will fire, from the scheduler's own now");
        result.Fires.Select(fire => fire.FireTimeUtc).Should().Equal(
            [now.AddMinutes(15), now.AddMinutes(30), now.AddMinutes(45)],
            "an overdue trigger is listed at its schedule's times after the window starts, without misfire handling");
    }

    [Test]
    public async Task PausedErrorAndBlockedTriggersAreListedWithTheirStateAndCompleteOnesAreNot()
    {
        IScheduler inner = await Scheduler();
        await inner.AddJob(Job());
        foreach (string name in (string[]) ["blocked", "complete", "error", "normal", "paused"])
        {
            await inner.ScheduleJob(Simple(name, "states", now.AddMinutes(1), TimeSpan.FromHours(1)));
        }

        await inner.PauseTrigger(new TriggerKey("paused", "states"));
        HeaderRewritingScheduler scheduler = new(inner)
        {
            Rewrite = header => header.Key.Name switch
            {
                "blocked" => header with { State = TriggerState.Blocked },
                "error" => header with { State = TriggerState.Error },
                "complete" => header with { State = TriggerState.Complete, NextFireTimeUtc = null },
                _ => header,
            }
        };

        UpcomingFiresResult result = await Served(scheduler, new UpcomingFiresQuery(null, now.AddMinutes(30), null, 100));

        result.Fires.Select(fire => (fire.TriggerKey.Name, fire.State)).Should().Equal(
            [("blocked", TriggerState.Blocked), ("error", TriggerState.Error), ("normal", TriggerState.Normal), ("paused", TriggerState.Paused)],
            "an operator who paused a trigger sees its fires paused rather than gone, and a complete trigger fires no more");
    }

    [Test]
    public async Task ATriggerMeasuredFromCompletionListsOnlyItsNextFire()
    {
        IScheduler scheduler = await Scheduler();
        ITrigger fixedDelay = TriggerBuilder.Create(clock)
            .WithIdentity("fixed-delay", "reports")
            .StartAt(now.AddMinutes(5))
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(10)).RepeatForever().MeasureIntervalFromCompletion())
            .Build();
        await scheduler.ScheduleJob(Job(), fixedDelay);

        UpcomingFiresResult result = await Served(scheduler, new UpcomingFiresQuery(null, now.AddHours(2), null, 100));

        result.Fires.Should().ContainSingle()
            .Which.Should().Match<UpcomingFire>(fire => fire.FireTimeUtc == now.AddMinutes(5) && fire.OnlyTheNext,
                "each later run waits for the one before it to complete, which nothing knows ahead of time");
    }

    [Test]
    public async Task MoreThanTenThousandDueTriggersAreRefusedAndAGroupFilterUnderTheCapIsServed()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.AddJob(Job());
        for (int i = 0; i < UpcomingFires.MaxTriggers; i++)
        {
            await scheduler.ScheduleJob(Simple("t" + i, "bulk", now.AddMinutes(1), TimeSpan.FromHours(1)));
        }

        await scheduler.ScheduleJob(Simple("one", "other", now.AddMinutes(2), TimeSpan.FromHours(1)));

        UpcomingFiresOutcome refused = await UpcomingFires.Query(scheduler, new UpcomingFiresQuery(null, now.AddMinutes(30), null, 100), CancellationToken.None);
        UpcomingFiresResult filtered = await Served(scheduler, new UpcomingFiresQuery(null, now.AddMinutes(30), "other", 100));

        refused.Refusal.Should().Contain("More than 10,000 triggers are due before").And.Contain("filter by trigger group");
        filtered.Fires.Should().ContainSingle().Which.TriggerKey.Should().Be(new TriggerKey("one", "other"));
    }

    /// <summary>
    /// ◆ A short answer over many triggers reads few of them: a trigger is read only once it could fire before the
    /// earliest fire not yet listed. Read them all first and this fails.
    /// </summary>
    [Test]
    public async Task AShortAnswerOverManyTriggersReadsFewOfThem()
    {
        IScheduler inner = await Scheduler();
        await inner.AddJob(Job());
        for (int i = 0; i < 5_000; i++)
        {
            await inner.ScheduleJob(Simple("t" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), "minutely", now.AddSeconds(i), TimeSpan.FromMinutes(1)));
        }

        HeaderRewritingScheduler scheduler = new(inner);

        UpcomingFiresResult result = await Served(scheduler, new UpcomingFiresQuery(null, now.AddHours(2), null, 100));

        result.Fires.Should().HaveCount(100);
        result.CutAtUtc.Should().NotBeNull();
        scheduler.TriggersRead.Should().BeLessThanOrEqualTo(200,
            "5,000 triggers are due, and the first 100 fires need only the first 80 or so of them, read 100 at a time");
    }

    /// <summary>
    /// Every walk shares one budget of steps, and a calendar check inside a step is one too: a trigger overdue by a
    /// month that fires every second, or one whose calendar excludes every day, ends the answer early rather than
    /// stepping for minutes.
    /// </summary>
    [Test]
    public async Task TheStepBudgetCutsTheAnswerRatherThanStepForever([Values] bool calendarExcludesEveryDay)
    {
        // Scheduled while the clock read a month earlier and read now: a scheduler stopped for a month, whose trigger
        // nothing has moved since. Scheduling moves a start already in the past to now.
        DateTimeOffset start = calendarExcludesEveryDay ? now : now.AddDays(-30);
        clock = new FakeTimeProvider(start);
        IScheduler scheduler = await Scheduler();
        await scheduler.AddCalendar("closed", new WeeklyCalendar { TimeZone = TimeZoneInfo.Utc });
        ITrigger everySecond = Cron("every-second", "reports", start, "* * * * * ?", calendarExcludesEveryDay ? "closed" : null);
        await scheduler.ScheduleJob(Job(), everySecond);
        clock.Advance(now - start);

        if (calendarExcludesEveryDay)
        {
            // Replaced without moving the trigger, as an application may: its stored next fire time stays now.
            WeeklyCalendar closed = new() { TimeZone = TimeZoneInfo.Utc };
            foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
            {
                closed.AddExcludedDay(day);
            }

            await scheduler.AddCalendar("closed", closed, new AddCalendarOptions { Replace = true });
        }

        (await scheduler.GetTrigger(everySecond.Key))!.NextFireTimeUtc.Should().Be(start, "the scheduler was never started, so nothing moved the trigger");

        Stopwatch watch = Stopwatch.StartNew();
        UpcomingFiresResult result = await Served(scheduler, new UpcomingFiresQuery(null, now.AddDays(7), null, UpcomingFires.MaxTake));
        watch.Stop();

        result.CutAtUtc.Should().NotBeNull("the budget ran out before the walk reached the window's end");
        result.Fires.Should().HaveCountLessThanOrEqualTo(1, "only the stored next fire time is known before the budget runs out");
        result.CutAtUtc.Should().Be(result.Fires.Count == 0 ? now : result.Fires[^1].FireTimeUtc);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "a million steps end in well under a second or two");
    }

    [Test]
    public async Task ATriggerWhoseCalendarIsMissingOrThatCannotBeAdvancedIsCountedAndTheRestAreListed()
    {
        IScheduler inner = await Scheduler();
        await inner.AddCalendar("gone", new WeeklyCalendar());
        await inner.ScheduleJob(Job(), Simple("fine", "reports", now.AddMinutes(1), TimeSpan.FromMinutes(10)));
        await inner.ScheduleJob(Simple("calendar-gone", "reports", now.AddMinutes(2), TimeSpan.FromMinutes(10), "gone"));
        await inner.ScheduleJob(Simple("not-operable", "reports", now.AddMinutes(3), TimeSpan.FromMinutes(10)));

        ITrigger notOperable = A.Fake<ITrigger>();
        A.CallTo(() => notOperable.Key).Returns(new TriggerKey("not-operable", "reports"));
        HeaderRewritingScheduler scheduler = new(inner)
        {
            MissingCalendar = "gone",
            Substitute = trigger => trigger.Key.Name == "not-operable" ? notOperable : trigger,
        };

        UpcomingFiresResult result = await Served(scheduler, new UpcomingFiresQuery(null, now.AddMinutes(30), null, 100));

        result.TriggersNotComputed.Should().Be(2);
        result.Fires.Should().NotBeEmpty().And.OnlyContain(fire => fire.TriggerKey.Name == "fine", "one bad trigger does not fail the rest");
    }

    [Test]
    public async Task ADecoratorOverAnInProcessSchedulerComposesOverItself()
    {
        IScheduler inner = await Scheduler();
        await ScheduleThree(inner, calendarName: null);
        HeaderRewritingScheduler decorator = new(new DelegatingScheduler(inner));

        UpcomingFiresResult result = await Served(decorator, new UpcomingFiresQuery(null, now.AddHours(1), null, 100));

        result.Fires.Should().NotBeEmpty();
        decorator.TriggersRead.Should().Be(3, "the triggers were read through the outermost decorator's own override");
    }

    private static TriggerKey[] ThreeKeys => [new("hourly", "reports"), new("every-20", "alerts"), new("interval", "alerts")];

    /// <summary>
    /// An hourly cron trigger, a simple trigger every 20 minutes and a daily-interval trigger every 45 minutes, all
    /// from now: each fires at now + 3 hours.
    /// </summary>
    private async Task ScheduleThree(IScheduler scheduler, string? calendarName)
    {
        ITrigger hourly = TriggerBuilder.Create(clock)
            .WithIdentity("hourly", "reports")
            .StartAt(now)
            .WithCronSchedule("0 0 * * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc))
            .WithCalendarName(calendarName)
            .Build();
        ITrigger every20 = TriggerBuilder.Create(clock)
            .WithIdentity("every-20", "alerts")
            .ForJob(jobKey)
            .StartAt(now)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(20)).RepeatForever())
            .WithCalendarName(calendarName)
            .Build();
        ITrigger interval = TriggerBuilder.Create(clock)
            .WithIdentity("interval", "alerts")
            .ForJob(jobKey)
            .StartAt(now)
            .WithDailyTimeIntervalSchedule(x => x.StartingDailyAt(new TimeOnly(0, 0)).WithInterval(45, IntervalUnit.Minute).InTimeZone(TimeZoneInfo.Utc))
            .WithCalendarName(calendarName)
            .Build();

        await scheduler.ScheduleJob(Job(), hourly);
        await scheduler.ScheduleJob(every20);
        await scheduler.ScheduleJob(interval);
    }

    private ITrigger Simple(string name, string group, DateTimeOffset start, TimeSpan interval, string? calendarName = null)
    {
        return TriggerBuilder.Create(clock)
            .WithIdentity(name, group)
            .ForJob(jobKey)
            .StartAt(start)
            .WithCalendarName(calendarName)
            .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever())
            .Build();
    }

    private ITrigger Cron(string name, string group, DateTimeOffset start, string expression, string? calendarName = null)
    {
        return TriggerBuilder.Create(clock)
            .WithIdentity(name, group)
            .ForJob(jobKey)
            .StartAt(start)
            .WithCronSchedule(expression, cron => cron.InTimeZone(TimeZoneInfo.Utc))
            .WithCalendarName(calendarName)
            .Build();
    }

    private static IJobDetail Job()
    {
        return JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build();
    }

    private static async Task<UpcomingFiresResult> Served(IScheduler scheduler, UpcomingFiresQuery query)
    {
        UpcomingFiresOutcome outcome = await UpcomingFires.Query(scheduler, query, CancellationToken.None);
        outcome.Refusal.Should().BeNull();
        return outcome.Result!;
    }

    private async Task<IScheduler> Scheduler(StoreKind store = StoreKind.InMemory)
    {
        SqliteTestDatabase? database = null;
        if (store == StoreKind.Sqlite)
        {
            database = new SqliteTestDatabase("upcoming-fires");
            databases.Add(database);
        }

        StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
            .Create(q =>
            {
                q.UseTimeProvider(clock);
                q.ConfigureScheduler(options => options.InstanceName = $"upcoming-fires-{Guid.NewGuid():N}");

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

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    /// <summary>
    /// A decorator that counts the triggers read, and can change what the listing, the trigger reads and the
    /// calendar reads answer.
    /// </summary>
    private sealed class HeaderRewritingScheduler : DelegatingScheduler
    {
        public HeaderRewritingScheduler(IScheduler scheduler) : base(scheduler)
        {
        }

        public int TriggersRead { get; private set; }

        public Func<TriggerHeader, TriggerHeader>? Rewrite { get; init; }

        public Func<ITrigger, ITrigger>? Substitute { get; init; }

        public string? MissingCalendar { get; init; }

        public override async ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
        {
            PagedResult<TriggerHeader> page = await base.QueryTriggers(query, cancellationToken);
            return Rewrite is null ? page : page with { Items = page.Items.Select(Rewrite).ToList() };
        }

        public override async ValueTask<List<ITrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            TriggersRead += triggerKeys.Count;
            List<ITrigger> triggers = await base.GetTriggers(triggerKeys, cancellationToken);
            return Substitute is null ? triggers : triggers.Select(Substitute).ToList();
        }

        public override ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
        {
            return calendarName == MissingCalendar ? ValueTask.FromResult<ICalendar?>(null) : base.GetCalendar(calendarName, cancellationToken);
        }
    }
}