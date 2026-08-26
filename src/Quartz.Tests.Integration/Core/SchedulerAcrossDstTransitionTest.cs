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

using System.Collections.Concurrent;
using System.Text;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Tests.Unit;

namespace Quartz.Tests.Integration.Core;

/// <summary>
/// A running scheduler carried across a daylight saving transition, over both stores, with the fires
/// it produced compared against what the trigger arithmetic says it should have produced.
/// </summary>
/// <remarks>
/// <para>
/// The daylight saving suites in <c>Quartz.Tests.Unit</c> ask <c>GetFireTimeAfter</c> what it
/// computes. Nothing asked what a scheduler <em>fires</em>, or what a database holds afterwards, so
/// the guarantee the documentation makes - that a schedule survives a transition - was untested end
/// to end. This is that test: the same window, the same triggers, run over
/// <see cref="Quartz.Impl.RAMJobStore" /> and over a SQLite file, in three zones and both
/// directions.
/// </para>
/// <para>
/// The clock is a <see cref="FakeTimeProvider" /> and nothing here sleeps. Two facts about the
/// scheduling loop shape the way it is driven, and both are written up in the testing tutorial:
/// advancing a fake clock does not wake the loop, which waits on a semaphore that only knows real
/// elapsed time, so every advance is followed by something that signals a scheduling change; and a
/// fire that is late by less than the misfire threshold is fired late rather than skipped, which is
/// why the threshold here is wider than the whole window. Between them the scheduler replays every
/// scheduled fire in the window in a burst after each advance, with
/// <see cref="IJobExecutionContext.ScheduledFireTimeUtc" /> carrying the time it was scheduled for -
/// which is the value under test.
/// </para>
/// <para>
/// SQLite runs on a file, so this needs no container and carries no <c>db-*</c> category: it runs in
/// the basic leg beside the in-memory half, which is the point of the pair.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class SchedulerAcrossDstTransitionTest
{
    /// <summary>Which job store the case runs against.</summary>
    public enum DstStore
    {
        InMemory,
        Sqlite
    }

    private const string Group = "dst";
    private const string CollectorTokenKey = "collector-token";
    private const string TablePrefix = "QRTZ_";

    /// <summary>
    /// Ten minutes short of four hours of elapsed time, starting one hour before the transition
    /// instant.
    /// </summary>
    /// <remarks>
    /// The ten minutes are missing so that no schedule here lands exactly on the far edge of the
    /// window, where the two sides of the comparison would have to agree about whether the edge is
    /// inside it - and they do not. <see cref="TriggerFireTimes.ComputeBetween" /> bounds the walk by
    /// assigning <c>EndTimeUtc = to</c>, and an end time is inclusive for
    /// <see cref="Quartz.Impl.Triggers.DailyTimeIntervalTriggerImpl" /> and exclusive for
    /// <see cref="Quartz.Impl.Triggers.SimpleTriggerImpl" />, so a fire at exactly <c>to</c> is
    /// counted for one and not the other. A scheduler has no end time and fires both. That
    /// disagreement is about <c>EndAt</c> rather than about daylight saving, so this fixture steps
    /// around it rather than arbitrating it.
    /// </remarks>
    private static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(230);

    /// <summary>How far the fake clock moves per step. Eight steps span the window.</summary>
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The shortest idle wait the option validator accepts, so the loop re-reads the clock about once
    /// a second even when a signal is missed.
    /// </summary>
    private static readonly TimeSpan IdleWaitTime = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Wider than the window, so that no fire in it is ever treated as a misfire: an advance of the
    /// fake clock makes every fire it passed over late by construction, and misfire handling would
    /// discard exactly the occurrences this test is here to count. Misfire behaviour across a
    /// transition is <see cref="Quartz.Tests.Integration.Impl.MisfireAcrossDstTransitionTest" />'s
    /// subject.
    /// </summary>
    private static readonly TimeSpan MisfireThreshold = TimeSpan.FromHours(12);

    /// <summary>
    /// How long to wait for the fires an advance made due. Not a timing assertion: it is how long the
    /// test waits before deciding something is broken.
    /// </summary>
    private static readonly TimeSpan FireDeadline = TimeSpan.FromSeconds(60);

    private static readonly ConcurrentDictionary<string, FireCollector> collectors = new(StringComparer.Ordinal);

    private string databaseFile;

    [TearDown]
    public void DeleteDatabaseFile()
    {
        if (databaseFile is null)
        {
            return;
        }

        // Pools first, or the handle the store left behind keeps the file locked on Windows.
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            try
            {
                File.Delete(databaseFile);
            }
            catch (IOException)
            {
                // scratch space; leaving one behind is not worth failing a passing test over
            }
        }

        databaseFile = null;
    }

    public static IEnumerable<TestCaseData> Cases()
    {
        foreach (DstStore store in new[] { DstStore.InMemory, DstStore.Sqlite })
        {
            foreach (string zone in new[] { "Helsinki", "NewYork", "LordHowe" })
            {
                foreach (string direction in new[] { "SpringForward", "FallBack" })
                {
                    yield return new TestCaseData(store, zone, direction);
                }
            }
        }
    }

    /// <summary>
    /// The whole of it: schedule four triggers in a zone, walk a scheduler through the hours around a
    /// transition, and require that the set of scheduled fire times it produced for each trigger is
    /// exactly the set <see cref="TriggerFireTimes.ComputeBetween" /> computes - then that the store
    /// holds the next fire time the trigger itself would compute.
    /// </summary>
    [TestCaseSource(nameof(Cases))]
    public async Task SchedulerFiresWhatTheTriggerArithmeticSays(DstStore store, string zoneKey, string direction)
    {
        DstTransition transition = ResolveTransition(zoneKey, direction);
        transition.AssumePremise();

        DateTimeOffset windowStart = transition.Instant - TimeSpan.FromHours(1);
        DateTimeOffset windowEnd = windowStart + WindowLength;

        FakeTimeProvider clock = new FakeTimeProvider(windowStart);
        string token = Guid.NewGuid().ToString("N");
        FireCollector collector = new FireCollector();
        collectors[token] = collector;

        JobKey jobKey = new JobKey("recorder", Group);
        List<ITrigger> triggers = BuildTriggers(clock, transition.Zone, windowStart, jobKey);

        // What the arithmetic says, computed from untouched copies of the same triggers. This is the
        // other side of the comparison, and it is deliberately taken before the scheduler runs so
        // that nothing the scheduler did can influence it.
        List<ExpectedFire> expected = triggers
            .SelectMany(trigger => TriggerFireTimes
                .ComputeBetween((IOperableTrigger) BuildTrigger(trigger.Key.Name, clock, transition.Zone, windowStart, jobKey), null, windowStart, windowEnd)
                .Select(fireTime => new ExpectedFire(trigger.Key, fireTime)))
            .OrderBy(x => x.FireTimeUtc)
            .ToList();

        expected.Should().NotBeEmpty("the window must contain fire times, or the case asserts nothing");

        try
        {
            await using StandaloneSchedulerFactory factory = BuildFactory(store, clock);
            IScheduler scheduler = await factory.GetScheduler();

            try
            {
                IJobDetail job = JobBuilder.Create<RecordingJob>()
                    .WithIdentity(jobKey)
                    .UsingJobData(CollectorTokenKey, token)
                    .StoreDurably()
                    .Build();

                await scheduler.AddJob(job, new AddJobOptions { Replace = true });

                foreach (ITrigger trigger in triggers)
                {
                    await scheduler.ScheduleJob(trigger);
                }

                await scheduler.Start();

                for (DateTimeOffset now = windowStart; now < windowEnd;)
                {
                    now = now + Step > windowEnd ? windowEnd : now + Step;
                    clock.SetUtcNow(now);

                    // Advance, then signal. ResumeAll resumes nothing here - no group is paused - but
                    // it releases the loop's semaphore, which is what makes the new "now" visible
                    // without waiting out an idle wait.
                    await scheduler.ResumeAll();

                    int dueByNow = expected.Count(x => x.FireTimeUtc <= now);
                    bool arrived = await collector.WaitForCount(dueByNow, FireDeadline);

                    arrived.Should().BeTrue(
                        "every fire scheduled at or before {0:O} must have happened by the time the clock reaches it, and {1}",
                        now,
                        Describe(expected.Where(x => x.FireTimeUtc <= now).ToList(), collector.Snapshot()));
                }

                // Nothing else can fire now: the clock is frozen at the end of the window and the
                // next fire of every trigger is beyond it. Standby stops the loop from acquiring
                // while the store is read.
                await scheduler.Standby();

                List<FiredJob> fired = collector.Snapshot();

                TestContext.Out.WriteLine($"{store}/{zoneKey}/{direction}: {fired.Count} fires in {windowStart:O}..{windowEnd:O}");

                fired.Should().OnlyContain(x => x.ScheduledFireTimeUtc.HasValue,
                    "a firing always knows the time it was scheduled for, which is what the rest of this test compares");

                foreach (ITrigger trigger in triggers)
                {
                    List<DateTimeOffset> firedTimes = fired
                        .Where(x => x.TriggerKey.Equals(trigger.Key))
                        .Select(x => x.ScheduledFireTimeUtc.Value)
                        .OrderBy(x => x)
                        .ToList();

                    List<DateTimeOffset> expectedTimes = expected
                        .Where(x => x.TriggerKey.Equals(trigger.Key))
                        .Select(x => x.FireTimeUtc)
                        .ToList();

                    TestContext.Out.WriteLine($"    {trigger.Key.Name}: {firedTimes.Count} fires, local {string.Join(", ", firedTimes.Select(x => TimeZoneInfo.ConvertTime(x, transition.Zone).ToString("HH:mm zzz")))}");

                    firedTimes.Should().Equal(expectedTimes,
                        "the scheduler must fire trigger '{0}' at exactly the times its own schedule computes across the {1} {2} transition - a scheduler that disagrees with the arithmetic is firing a job at a time nobody asked for, or skipping one somebody did",
                        trigger.Key.Name, zoneKey, direction);
                }

                await AssertStoredNextFireTimes(scheduler, triggers, windowEnd);

                if (store == DstStore.Sqlite)
                {
                    await AssertNextFireTimeColumn(scheduler.SchedulerName, triggers, windowEnd);
                }
            }
            finally
            {
                await scheduler.Shutdown(waitForJobsToComplete: true);
            }
        }
        finally
        {
            collectors.TryRemove(token, out _);
        }
    }

    /// <summary>
    /// What the store holds once the window has passed: the next fire time it kept must be the one
    /// the trigger itself computes for the instant the clock stopped at. This is the half that a
    /// pure arithmetic test cannot reach - the value that survives a restart.
    /// </summary>
    private static async Task AssertStoredNextFireTimes(IScheduler scheduler, List<ITrigger> triggers, DateTimeOffset windowEnd)
    {
        foreach (ITrigger scheduled in triggers)
        {
            ITrigger stored = await scheduler.GetTrigger(scheduled.Key);

            stored.Should().NotBeNull("trigger '{0}' was scheduled and never unscheduled", scheduled.Key.Name);

            DateTimeOffset? computed = stored.GetFireTimeAfter(windowEnd);

            stored.NextFireTimeUtc.Should().Be(computed,
                "the next fire time trigger '{0}' left in the store is the one it recomputes from its own schedule, so a restart at {1:O} resumes where the run stopped",
                scheduled.Key.Name, windowEnd);
        }
    }

    /// <summary>
    /// The same value read straight out of <c>NEXT_FIRE_TIME</c>. Reading it through the store would
    /// pass even if the column held something else and the value came from somewhere the store
    /// caches, so the column is read on its own terms: UTC ticks, which is what the schema contract
    /// says it holds.
    /// </summary>
    private async Task AssertNextFireTimeColumn(string schedulerName, List<ITrigger> triggers, DateTimeOffset windowEnd)
    {
        Dictionary<string, long> stored = new Dictionary<string, long>(StringComparer.Ordinal);

        await using (SqliteConnection connection = new SqliteConnection(ConnectionString))
        {
            await connection.OpenAsync();

            await using SqliteCommand command = new SqliteCommand(
                $"SELECT TRIGGER_NAME, NEXT_FIRE_TIME FROM {TablePrefix}TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_GROUP = @group",
                connection);

            command.Parameters.AddWithValue("@schedulerName", schedulerName);
            command.Parameters.AddWithValue("@group", Group);

            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                stored[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        stored.Should().HaveCount(triggers.Count, "every scheduled trigger has a row of its own");

        foreach (ITrigger trigger in triggers)
        {
            DateTimeOffset column = new DateTimeOffset(stored[trigger.Key.Name], TimeSpan.Zero);

            column.Should().Be(trigger.GetFireTimeAfter(windowEnd),
                "NEXT_FIRE_TIME for '{0}' holds the instant the trigger will fire next, as UTC ticks",
                trigger.Key.Name);
        }
    }

    private StandaloneSchedulerFactory BuildFactory(DstStore store, TimeProvider clock)
    {
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create()
            .UseTimeProvider(clock)
            .ConfigureScheduler(options =>
            {
                options.InstanceName = $"dst-{Guid.NewGuid():N}";
                options.IdleWaitTime = IdleWaitTime;
            });

        if (store == DstStore.InMemory)
        {
            builder.UseInMemoryStore(options => options.MisfireThreshold = MisfireThreshold);
        }
        else
        {
            PrepareDatabase();

            builder.UsePersistentStore(persistent =>
            {
                persistent.UseSqlite(ConnectionString);
                persistent.ConfigureStore(options =>
                {
                    options.TablePrefix = TablePrefix;
                    options.MisfireThreshold = MisfireThreshold;
                    // The handler scans on the store's own clock, which is the fake one; a frequency
                    // wider than the window keeps its scans out of the way of the advances below.
                    options.MisfireHandlerFrequency = MisfireThreshold;
                });
            });
        }

        return builder.Build();
    }

    private string ConnectionString => $"Data Source={databaseFile};";

    private void PrepareDatabase()
    {
        databaseFile = $"dst-scheduler-{Guid.NewGuid():N}.db";

        using SqliteConnection connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using SqliteCommand command = new SqliteCommand(LoadTableScript(), connection);
        command.ExecuteNonQuery();
    }

    private static string LoadTableScript()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "database", "tables", "tables_sqlite.sql");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate database/tables/tables_sqlite.sql from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// The four schedules, one of each kind that computes a fire time differently across a
    /// transition: cron anchors to the wall clock, the daily time interval walks a within-day grid,
    /// the calendar interval preserves its hour of day, and the simple trigger counts elapsed time
    /// and so should not notice the transition at all.
    /// </summary>
    private static List<ITrigger> BuildTriggers(TimeProvider clock, TimeZoneInfo zone, DateTimeOffset windowStart, JobKey jobKey)
    {
        return
        [
            BuildTrigger("cron-half-past", clock, zone, windowStart, jobKey),
            BuildTrigger("daily-quarter-hour", clock, zone, windowStart, jobKey),
            BuildTrigger("calendar-daily", clock, zone, windowStart, jobKey),
            BuildTrigger("simple-half-hour", clock, zone, windowStart, jobKey)
        ];
    }

    private static ITrigger BuildTrigger(string name, TimeProvider clock, TimeZoneInfo zone, DateTimeOffset windowStart, JobKey jobKey)
    {
        // The cron trigger is the one type that is built by hand rather than through the builder,
        // and it has to be: CronTriggerImpl.ComputeFirstFireTimeUtc - which the scheduler calls on
        // every ScheduleJob - clamps a first fire time that is in the past forward to "now", and the
        // "now" it reads is the trigger's own TimeProvider. TriggerBuilder does not hand the trigger
        // the clock it was created with, so a builder-built cron trigger reads the system clock and
        // a scheduler running in 2024 on a fake clock would schedule it for today. Constructing the
        // implementation with the clock is the only way to say which clock it computes against.
        if (name == "cron-half-past")
        {
            return new Quartz.Impl.Triggers.CronTriggerImpl(clock)
            {
                Key = new TriggerKey(name, Group),
                JobKey = jobKey,
                CronExpressionString = "0 30 * * * ?",
                TimeZone = zone,
                StartTimeUtc = windowStart
            };
        }

        // The clock is passed to the builder and the start time is stated, because a builder given
        // neither starts the trigger at the wall clock - which would make every expectation here an
        // assertion about the machine's own "now".
        TriggerBuilder<IJob> builder = TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(jobKey)
            .StartAt(windowStart);

        return name switch
        {
            "daily-quarter-hour" => builder
                .WithDailyTimeIntervalSchedule(x => x
                    .WithInterval(15, IntervalUnit.Minute)
                    .OnEveryDay()
                    .StartingDailyAt(new TimeOnly(0, 0))
                    .EndingDailyAt(new TimeOnly(23, 59, 59))
                    .InTimeZone(zone))
                .Build(),
            "calendar-daily" => builder
                .WithCalendarIntervalSchedule(x => x
                    .WithInterval(1, IntervalUnit.Day)
                    .InTimeZone(zone)
                    .PreserveHourOfDayAcrossDaylightSavings())
                .Build(),
            "simple-half-hour" => builder
                .WithSimpleSchedule(x => x
                    .WithInterval(TimeSpan.FromMinutes(30))
                    .RepeatForever())
                .Build(),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown trigger")
        };
    }

    private static DstTransition ResolveTransition(string zoneKey, string direction)
    {
        switch (zoneKey, direction)
        {
            case ("Helsinki", "SpringForward"):
                // 03:00 EET becomes 04:00 EEST; 03:30 never happens.
                return new DstTransition(
                    TestTimeZones.Helsinki,
                    new DateTimeOffset(2024, 3, 31, 1, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeInvalidLocalTime(TestTimeZones.Helsinki, new DateTime(2024, 3, 31, 3, 30, 0)));

            case ("Helsinki", "FallBack"):
                // 04:00 EEST becomes 03:00 EET; 03:30 happens twice.
                return new DstTransition(
                    TestTimeZones.Helsinki,
                    new DateTimeOffset(2024, 10, 27, 1, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeAmbiguousLocalTime(TestTimeZones.Helsinki, new DateTime(2024, 10, 27, 3, 30, 0)));

            case ("NewYork", "SpringForward"):
                // 02:00 EST becomes 03:00 EDT; 02:30 never happens.
                return new DstTransition(
                    TestTimeZones.Eastern,
                    new DateTimeOffset(2024, 3, 10, 7, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeInvalidLocalTime(TestTimeZones.Eastern, new DateTime(2024, 3, 10, 2, 30, 0)));

            case ("NewYork", "FallBack"):
                // 02:00 EDT becomes 01:00 EST; 01:30 happens twice.
                return new DstTransition(
                    TestTimeZones.Eastern,
                    new DateTimeOffset(2024, 11, 3, 6, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeAmbiguousLocalTime(TestTimeZones.Eastern, new DateTime(2024, 11, 3, 1, 30, 0)));

            case ("LordHowe", "SpringForward"):
                // The half hour delta: 02:00 becomes 02:30, so only 02:00-02:29 is missing.
                return new DstTransition(
                    TestTimeZones.LordHowe,
                    new DateTimeOffset(2024, 10, 5, 15, 30, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeInvalidLocalTime(TestTimeZones.LordHowe, new DateTime(2024, 10, 6, 2, 15, 0)));

            case ("LordHowe", "FallBack"):
                // 02:00 becomes 01:30, so only 01:30-01:59 repeats.
                return new DstTransition(
                    TestTimeZones.LordHowe,
                    new DateTimeOffset(2024, 4, 6, 15, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeAmbiguousLocalTime(TestTimeZones.LordHowe, new DateTime(2024, 4, 7, 1, 45, 0)));

            default:
                throw new ArgumentOutOfRangeException(nameof(zoneKey), zoneKey, "unknown zone and direction");
        }
    }

    private static string Describe(List<ExpectedFire> due, List<FiredJob> fired)
    {
        List<ExpectedFire> missing = due
            .Where(x => fired.Count(y => y.TriggerKey.Equals(x.TriggerKey) && y.ScheduledFireTimeUtc == x.FireTimeUtc) == 0)
            .ToList();

        StringBuilder builder = new StringBuilder();
        builder.Append("these have not arrived: ");
        builder.Append(missing.Count == 0
            ? "none, which cannot happen while the count is short and is worth looking at on its own"
            : string.Join(", ", missing.Select(x => $"{x.TriggerKey.Name}@{x.FireTimeUtc:O}")));
        return builder.ToString();
    }

    private sealed record DstTransition(TimeZoneInfo Zone, DateTimeOffset Instant, Action AssumePremise);

    private sealed record ExpectedFire(TriggerKey TriggerKey, DateTimeOffset FireTimeUtc);

    private sealed record FiredJob(TriggerKey TriggerKey, DateTimeOffset? ScheduledFireTimeUtc);

    /// <summary>
    /// Collects what fired and lets the test wait for a count rather than for a duration. The job
    /// finds its collector through a token in the job data map, which is a string and so survives the
    /// round trip through a database - an object reference would not.
    /// </summary>
    private sealed class FireCollector
    {
        private readonly Lock gate = new();
        private readonly List<FiredJob> fires = [];
        private TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int target = int.MaxValue;

        public void Record(TriggerKey triggerKey, DateTimeOffset? scheduledFireTimeUtc)
        {
            lock (gate)
            {
                fires.Add(new FiredJob(triggerKey, scheduledFireTimeUtc));
                if (fires.Count >= target)
                {
                    reached.TrySetResult();
                }
            }
        }

        public List<FiredJob> Snapshot()
        {
            lock (gate)
            {
                return [.. fires];
            }
        }

        public async Task<bool> WaitForCount(int count, TimeSpan deadline)
        {
            Task waiting;
            lock (gate)
            {
                target = count;
                reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (fires.Count >= target)
                {
                    reached.TrySetResult();
                }

                waiting = reached.Task;
            }

            try
            {
                // A real-time deadline, because it is the answer to "when should this test give up",
                // not to "when should the job have run".
                await waiting.WaitAsync(deadline).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Records the time it was scheduled for, which is the value the whole fixture is about:
    /// <see cref="IJobExecutionContext.FireTimeUtc" /> is when the scheduler got to it, while
    /// <see cref="IJobExecutionContext.ScheduledFireTimeUtc" /> is the occurrence the schedule
    /// promised.
    /// </summary>
    public sealed class RecordingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            string token = context.MergedJobDataMap.GetString(CollectorTokenKey);
            if (collectors.TryGetValue(token, out FireCollector collector))
            {
                collector.Record(context.Trigger.Key, context.ScheduledFireTimeUtc);
            }

            return default;
        }
    }
}
