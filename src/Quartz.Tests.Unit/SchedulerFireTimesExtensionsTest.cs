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

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="SchedulerFireTimesExtensions.GetUpcomingFireTimes" />: a stored trigger's next fire times, for
/// every trigger type, in the trigger's own zone, less what its calendar excludes and up to its end time.
/// </summary>
/// <remarks>
/// Every trigger here fires at 09:00 in Kathmandu, which is UTC+05:45 all year: neither a CI host's zone nor
/// any zone a whole number of hours from UTC, so the host's zone cannot produce 03:15 UTC by accident. The
/// calendar excludes 7 October in Kathmandu, the third of the trigger's days.
/// </remarks>
public sealed class SchedulerFireTimesExtensionsTest
{
    public enum StoreKind
    {
        InMemory,
        Sqlite,
    }

    public enum TriggerKind
    {
        Simple,
        Cron,
        CalendarInterval,
        DailyTimeInterval,
        Recurrence,
    }

    private static readonly TimeZoneInfo kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");

    /// <summary>
    /// The scheduler's clock, a few hours before the first fire time.
    /// </summary>
    private static readonly DateTimeOffset now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 09:00 in Kathmandu on 5 October: the first fire time of every trigger here.
    /// </summary>
    private static readonly DateTimeOffset nine = new(2026, 10, 5, 3, 15, 0, TimeSpan.Zero);

    private static readonly JobKey jobKey = new("export", "reports");
    private static readonly TriggerKey triggerKey = new("daily", "reports");

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

    [Test]
    [Combinatorial]
    public async Task EveryTriggerTypeFiresInItsOwnZoneLessWhatItsCalendarExcludes([Values] TriggerKind kind, [Values] StoreKind store)
    {
        IScheduler scheduler = await Scheduler(store);
        await scheduler.ScheduleJob(Job(), Daily(kind));

        List<DateTimeOffset>? fireTimes = await scheduler.GetUpcomingFireTimes(triggerKey, 4);

        fireTimes.Should().Equal(
            [nine, nine.AddDays(1), nine.AddDays(3), nine.AddDays(4)],
            "09:00 in Kathmandu is 03:15 UTC, and the calendar excludes the 7th there, so the scheduler will not fire it then");
        fireTimes!.Should().AllSatisfy(fireTime => fireTime.Offset.Should().Be(TimeSpan.Zero,
            "the times are answered in UTC, as the wire carries them, whatever zone the trigger reads its schedule in"));
    }

    [Test]
    public async Task TheListStopsAtTheTriggersEndTime([Values] TriggerKind kind)
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Daily(kind, endAt: nine.AddDays(3)));

        List<DateTimeOffset>? fireTimes = await scheduler.GetUpcomingFireTimes(triggerKey, 10);

        fireTimes.Should().Equal(
            [nine, nine.AddDays(1), nine.AddDays(3)],
            "the end time is the last instant the trigger may fire, so a fire time on it is listed and none after it, however many were asked for");
    }

    [TestCase(1)]
    [TestCase(100)]
    public async Task ACountFromOneToAHundredIsAnswered(int count)
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Daily(TriggerKind.Cron));

        List<DateTimeOffset>? fireTimes = await scheduler.GetUpcomingFireTimes(triggerKey, count);

        fireTimes.Should().HaveCount(count, "a daily trigger with no end time has as many fire times as are asked for");
        fireTimes![0].Should().Be(nine);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(101)]
    public async Task ACountOutsideOneToAHundredIsRefusedBeforeAnythingIsRead(int count)
    {
        IScheduler scheduler = A.Fake<IScheduler>();

        Func<Task> act = () => scheduler.GetUpcomingFireTimes(triggerKey, count).AsTask();

        (await act.Should().ThrowAsync<ArgumentOutOfRangeException>()).Which.ParamName.Should().Be("count");
        A.CallTo(() => scheduler.GetTrigger(A<TriggerKey>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task AnUnknownTriggerAnswersNullAsGetTriggerDoes()
    {
        IScheduler scheduler = await Scheduler();

        List<DateTimeOffset>? fireTimes = await scheduler.GetUpcomingFireTimes(new TriggerKey("ghost", "reports"), 10);

        fireTimes.Should().BeNull("an absent trigger is null on every read of one, which an empty list would be confused with");
    }

    /// <summary>
    /// A stored trigger with no next fire time has fired for the last time. Its schedule computed afresh from
    /// its start would list times already behind it.
    /// </summary>
    [Test]
    public async Task ATriggerWithNoNextFireTimeHasNoUpcomingFires()
    {
        ITrigger spent = TriggerBuilder.Create(clock)
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(nine)
            .WithSimpleSchedule(TimeSpan.FromDays(1))
            .Build();
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.GetTrigger(triggerKey, A<CancellationToken>._)).Returns(spent);

        List<DateTimeOffset>? fireTimes = await scheduler.GetUpcomingFireTimes(triggerKey, 10);

        fireTimes.Should().BeEmpty("the scheduler fires a trigger at its next fire time, and this one has none");
    }

    [Test]
    public async Task ACalendarThatIsGoneIsRaisedRatherThanLeftOut()
    {
        ITrigger trigger = Daily(TriggerKind.Cron);
        ((IOperableTrigger) trigger).ComputeFirstFireTimeUtc(null);
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.GetTrigger(triggerKey, A<CancellationToken>._)).Returns(trigger);
        A.CallTo(() => scheduler.GetCalendar("holidays", A<CancellationToken>._)).Returns((ICalendar?) null);

        Func<Task> act = () => scheduler.GetUpcomingFireTimes(triggerKey, 10).AsTask();

        await act.Should().ThrowAsync<ObjectDoesNotExistException>().WithMessage("Calendar 'holidays'*does not exist*",
            "the times without the calendar would include the ones it excludes, which the scheduler will not fire");
    }

    /// <summary>
    /// Through decorators over a scheduler in this process the times are composed over the outermost one, so
    /// every member a decorator overrides takes part. Decorators over an <c>HttpScheduler</c> forward to it
    /// instead, which <c>TriggerFireTimesOverHttpTest</c> covers.
    /// </summary>
    [Test]
    public async Task FireTimesOverAnInProcessSchedulerAreComposedOverTheOutermostDecorator()
    {
        IScheduler scheduler = await Scheduler();
        await scheduler.ScheduleJob(Job(), Daily(TriggerKind.Cron));
        TriggerReadingScheduler decorator = new(new DelegatingScheduler(scheduler));

        List<DateTimeOffset>? fireTimes = await decorator.GetUpcomingFireTimes(triggerKey, 2);

        fireTimes.Should().Equal([nine, nine.AddDays(1)]);
        decorator.TriggersRead.Should().Equal([triggerKey], "the trigger was read through the outermost decorator's own override");
    }

    private async Task<IScheduler> Scheduler(StoreKind store = StoreKind.InMemory)
    {
        SqliteTestDatabase? database = null;
        if (store == StoreKind.Sqlite)
        {
            database = new SqliteTestDatabase("fire-times");
            databases.Add(database);
        }

        StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
            .Create(q =>
            {
                q.UseTimeProvider(clock);
                q.ConfigureScheduler(options => options.InstanceName = $"fire-times-{Guid.NewGuid():N}");

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
        IScheduler scheduler = await factory.GetScheduler();

        HolidayCalendar holidays = new() { TimeZone = kathmandu };
        holidays.AddExcludedDay(new DateOnly(2026, 10, 7));
        await scheduler.AddCalendar("holidays", holidays);

        return scheduler;
    }

    private static IJobDetail Job()
    {
        return JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).Build();
    }

    /// <summary>
    /// A trigger of <paramref name="kind" /> that fires at 09:00 in Kathmandu every day from the 5th, under the
    /// holidays calendar. A simple trigger has no zone of its own: it repeats every 24 hours from 09:00 there.
    /// </summary>
    private ITrigger Daily(TriggerKind kind, DateTimeOffset? endAt = null)
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create(clock)
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(nine)
            .EndAt(endAt)
            .WithCalendarName("holidays");

        TriggerBuilder<IJob> scheduled = kind switch
        {
            TriggerKind.Simple => builder.WithSimpleSchedule(TimeSpan.FromDays(1)),
            TriggerKind.Cron => builder.WithCronSchedule("0 0 9 * * ?", cron => cron.InTimeZone(kathmandu)),
            TriggerKind.CalendarInterval => builder.WithCalendarIntervalSchedule(schedule => schedule
                .WithInterval(1, IntervalUnit.Day)
                .InTimeZone(kathmandu)),
            TriggerKind.DailyTimeInterval => builder.WithDailyTimeIntervalSchedule(schedule => schedule
                .StartingDailyAt(new TimeOnly(9, 0))
                .EndingDailyAt(new TimeOnly(9, 30))
                .WithInterval(1, IntervalUnit.Hour)
                .InTimeZone(kathmandu)),
            _ => builder.WithRecurrenceSchedule("FREQ=DAILY", recurrence => recurrence.InTimeZone(kathmandu)),
        };

        return scheduled.Build();
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    /// <summary>
    /// A decorator that watches which triggers are read.
    /// </summary>
    private sealed class TriggerReadingScheduler : DelegatingScheduler
    {
        public TriggerReadingScheduler(IScheduler scheduler) : base(scheduler)
        {
        }

        public List<TriggerKey> TriggersRead { get; } = [];

        public override ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            TriggersRead.Add(triggerKey);
            return base.GetTrigger(triggerKey, cancellationToken);
        }
    }
}
