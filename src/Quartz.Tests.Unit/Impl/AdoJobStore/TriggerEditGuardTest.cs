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

using System.Data.Common;

using FakeItEasy;

using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Calendar;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// An editor that reads a trigger's row, changes it and writes it back holds the write to the row as it
/// was read, and applies the edit again to the row a firing left when the write misses (#4067).
/// </summary>
/// <remarks>
/// <c>UpdateTriggerDetails</c> and a calendar update that recomputes its triggers both hold
/// <c>TRIGGER_ACCESS</c>; a firing round of work that allows concurrent execution does not, so it can
/// fire the row between their read and their write. The delegate is a fake: a write that misses is a
/// count of zero, and the row the fire left is what the next read answers.
/// </remarks>
public sealed class TriggerEditGuardTest
{
    private const string Group = "edit-guard";

    /// <summary>Log event <c>AdoJobStoreLog.TriggerEditReapplied</c>.</summary>
    private const int TriggerEditReapplied = 3059;

    private static readonly DateTimeOffset AsRead = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsFired = AsRead.AddHours(1);

    private static readonly JobKey jobKey = new("job", Group);
    private static readonly TriggerKey triggerKey = new("edited", Group);

    private AdoJobStoreBaseTest.TestAdoJobStoreBase store = null!;
    private IDriverDelegate driverDelegate = null!;
    private ConnectionAndTransactionHolder conn = null!;
    private RecordingLoggerProvider logs = null!;
    private ILoggerFactory loggerFactory = null!;
    private IJobDetail job = null!;

    [SetUp]
    public void SetUp()
    {
        logs = new RecordingLoggerProvider();
        loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        store = new AdoJobStoreBaseTest.TestAdoJobStoreBase(loggerFactory: loggerFactory);
        driverDelegate = A.Fake<IDriverDelegate>();
        store.DirectDelegate = driverDelegate;
        store.DirectSignaler = A.Fake<ISchedulerSignaler>();
        conn = new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null);
        job = JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build();

        A.CallTo(() => driverDelegate.SelectJobForTrigger(A<ConnectionAndTransactionHolder>._, triggerKey, A<ITypeLoader>._, A<bool>._, A<CancellationToken>._))
            .Returns(new ValueTask<IJobDetail?>(job));
        A.CallTo(() => driverDelegate.SelectJobDetail(A<ConnectionAndTransactionHolder>._, jobKey, A<ITypeLoader>._, A<CancellationToken>._))
            .Returns(new ValueTask<IJobDetail?>(job));
        A.CallTo(() => driverDelegate.SelectTriggerState(A<ConnectionAndTransactionHolder>._, triggerKey, A<CancellationToken>._))
            .Returns(new ValueTask<StoredTriggerState>(StoredTriggerState.Waiting));
    }

    [TearDown]
    public void TearDown()
    {
        conn.Dispose();
        loggerFactory.Dispose();
        logs.Dispose();
    }

    [Test]
    public async Task AnEditHoldsTheWriteToTheFireTimeItRead()
    {
        IOperableTrigger existing = Trigger(AsRead);
        GivenTheRowReads(existing);
        GivenTheWriteTakesFor(AsRead);

        bool updated = await store.CallUpdateTriggerDetails(conn, triggerKey, new TriggerDetailsUpdate().WithDescription("edited"));

        updated.Should().BeTrue();
        A.CallTo(() => driverDelegate.UpdateTriggerIfUnchanged(A<ConnectionAndTransactionHolder>._, existing, StoredTriggerState.Waiting, job, AsRead, A<CancellationToken>._))
            // the write names the fire time the row was read with, which a firing in between would have moved
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.UpdateTrigger(A<ConnectionAndTransactionHolder>._, A<IOperableTrigger>._, A<StoredTriggerState>._, A<IJobDetail>._, A<CancellationToken>._))
            // an unguarded write by key is what would re-arm the occurrence a firing round has just fired
            .MustNotHaveHappened();
    }

    [Test]
    public async Task AnEditOfATriggerBeingFiredIsReappliedToTheRowTheFireLeft()
    {
        IOperableTrigger existing = Trigger(AsRead);
        IOperableTrigger moved = Trigger(AsFired);
        GivenTheRowReads(existing, moved);
        GivenTheWriteTakesFor(AsFired);

        bool updated = await store.CallUpdateTriggerDetails(conn, triggerKey, new TriggerDetailsUpdate().WithDescription("edited").WithPriority(9));

        updated.Should().BeTrue();
        moved.Description.Should().Be("edited", "the edit is applied to the row the fire left, which is the serialised order: round, then edit");
        moved.Priority.Should().Be(9);
        moved.NextFireTimeUtc.Should().Be(AsFired, "the fire time the round wrote is kept; the edit never read it back to the one before the fire");

        A.CallTo(() => driverDelegate.UpdateTriggerIfUnchanged(A<ConnectionAndTransactionHolder>._, existing, A<StoredTriggerState>._, job, AsRead, A<CancellationToken>._))
            // the first write was held to the row as first read, and missed
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.UpdateTriggerIfUnchanged(A<ConnectionAndTransactionHolder>._, moved, A<StoredTriggerState>._, job, AsFired, A<CancellationToken>._))
            // the second write was held to the row as the fire left it, and took
            .MustHaveHappenedOnceExactly();

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == TriggerEditReapplied,
            "a reapplied edit is logged at debug, for whoever wonders why an edit read the row twice");
    }

    [Test]
    public async Task AnEditWhoseRowKeepsMovingGivesUpAndSaysSo()
    {
        A.CallTo(() => driverDelegate.SelectTrigger(A<ConnectionAndTransactionHolder>._, triggerKey, A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<IOperableTrigger?>(Trigger(AsRead)));
        A.CallTo(() => driverDelegate.UpdateTriggerIfUnchanged(A<ConnectionAndTransactionHolder>._, A<IOperableTrigger>._, A<StoredTriggerState>._, A<IJobDetail>._, A<DateTimeOffset?>._, A<CancellationToken>._))
            .Returns(new ValueTask<int>(0));

        Func<Task> act = async () => await store.CallUpdateTriggerDetails(conn, triggerKey, new TriggerDetailsUpdate().WithDescription("edited"));

        await act.Should().ThrowAsync<JobPersistenceException>("a trigger firing faster than it can be edited is reported rather than waited out for good")
            .WithMessage("*fired 8 times while it was being edited*");
        A.CallTo(() => driverDelegate.UpdateTriggerIfUnchanged(A<ConnectionAndTransactionHolder>._, A<IOperableTrigger>._, A<StoredTriggerState>._, A<IJobDetail>._, A<DateTimeOffset?>._, A<CancellationToken>._))
            .MustHaveHappened(8, Times.Exactly);
    }

    [Test]
    public async Task ACalendarUpdateIsReappliedToTheRowTheFireLeft()
    {
        const string calendarName = "cal";
        ICalendar calendar = new BaseCalendar();
        IOperableTrigger existing = Trigger(AsRead, calendarName);
        IOperableTrigger moved = Trigger(AsFired, calendarName);

        A.CallTo(() => driverDelegate.CalendarExists(A<ConnectionAndTransactionHolder>._, calendarName, A<CancellationToken>._))
            .Returns(new ValueTask<bool>(true));
        A.CallTo(() => driverDelegate.UpdateCalendar(A<ConnectionAndTransactionHolder>._, calendarName, calendar, A<CancellationToken>._))
            .Returns(new ValueTask<int>(1));
        A.CallTo(() => driverDelegate.SelectTriggersForCalendar(A<ConnectionAndTransactionHolder>._, calendarName, A<CancellationToken>._))
            .Returns(new ValueTask<List<IOperableTrigger>>([existing]));
        A.CallTo(() => driverDelegate.SelectTrigger(A<ConnectionAndTransactionHolder>._, triggerKey, A<CancellationToken>._))
            .Returns(new ValueTask<IOperableTrigger?>(moved));
        GivenTheWriteTakesFor(AsFired);

        await store.CallAddCalendar(conn, calendarName, calendar, replace: true, updateTriggers: true);

        A.CallTo(() => driverDelegate.UpdateTriggerIfUnchanged(A<ConnectionAndTransactionHolder>._, existing, StoredTriggerState.Waiting, job, AsRead, A<CancellationToken>._))
            // the recomputation was first written held to the row as the calendar read found it
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.UpdateTriggerIfUnchanged(A<ConnectionAndTransactionHolder>._, moved, StoredTriggerState.Waiting, job, AsFired, A<CancellationToken>._))
            // and then done again from the row the fire left, held to that
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.InsertTrigger(A<ConnectionAndTransactionHolder>._, A<IOperableTrigger>._, A<StoredTriggerState>._, A<IJobDetail>._, A<CancellationToken>._))
            // a write that missed a row that moved is not a row that went: nothing is inserted
            .MustNotHaveHappened();

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == TriggerEditReapplied);
    }

    [Test]
    public async Task ACalendarUpdateOfATriggerThatWentWritesNothing()
    {
        const string calendarName = "cal";
        ICalendar calendar = new BaseCalendar();
        IOperableTrigger existing = Trigger(AsRead, calendarName);

        A.CallTo(() => driverDelegate.CalendarExists(A<ConnectionAndTransactionHolder>._, calendarName, A<CancellationToken>._))
            .Returns(new ValueTask<bool>(true));
        A.CallTo(() => driverDelegate.UpdateCalendar(A<ConnectionAndTransactionHolder>._, calendarName, calendar, A<CancellationToken>._))
            .Returns(new ValueTask<int>(1));
        A.CallTo(() => driverDelegate.SelectTriggersForCalendar(A<ConnectionAndTransactionHolder>._, calendarName, A<CancellationToken>._))
            .Returns(new ValueTask<List<IOperableTrigger>>([existing]));
        A.CallTo(() => driverDelegate.SelectTrigger(A<ConnectionAndTransactionHolder>._, triggerKey, A<CancellationToken>._))
            .Returns(new ValueTask<IOperableTrigger?>((IOperableTrigger?) null));
        A.CallTo(() => driverDelegate.UpdateTriggerIfUnchanged(A<ConnectionAndTransactionHolder>._, A<IOperableTrigger>._, A<StoredTriggerState>._, A<IJobDetail>._, A<DateTimeOffset?>._, A<CancellationToken>._))
            .Returns(new ValueTask<int>(0));

        await store.CallAddCalendar(conn, calendarName, calendar, replace: true, updateTriggers: true);

        A.CallTo(() => driverDelegate.UpdateTriggerIfUnchanged(A<ConnectionAndTransactionHolder>._, A<IOperableTrigger>._, A<StoredTriggerState>._, A<IJobDetail>._, A<DateTimeOffset?>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => driverDelegate.InsertTrigger(A<ConnectionAndTransactionHolder>._, A<IOperableTrigger>._, A<StoredTriggerState>._, A<IJobDetail>._, A<CancellationToken>._))
            // its last firing's completion deleted the row, and a calendar update does not resurrect a trigger
            .MustNotHaveHappened();
    }

    private void GivenTheRowReads(IOperableTrigger first, IOperableTrigger? then = null)
    {
        // The rule configured last answers first, until its one call is spent.
        A.CallTo(() => driverDelegate.SelectTrigger(A<ConnectionAndTransactionHolder>._, triggerKey, A<CancellationToken>._))
            .Returns(new ValueTask<IOperableTrigger?>(then));
        A.CallTo(() => driverDelegate.SelectTrigger(A<ConnectionAndTransactionHolder>._, triggerKey, A<CancellationToken>._))
            .Returns(new ValueTask<IOperableTrigger?>(first)).Once();
    }

    private void GivenTheWriteTakesFor(DateTimeOffset expectedNextFireTimeUtc)
    {
        A.CallTo(() => driverDelegate.UpdateTriggerIfUnchanged(A<ConnectionAndTransactionHolder>._, A<IOperableTrigger>._, A<StoredTriggerState>._, A<IJobDetail>._, A<DateTimeOffset?>._, A<CancellationToken>._))
            .ReturnsLazily((ConnectionAndTransactionHolder _, IOperableTrigger _, StoredTriggerState _, IJobDetail _, DateTimeOffset? expected, CancellationToken _) =>
                new ValueTask<int>(expected == expectedNextFireTimeUtc ? 1 : 0));
    }

    private static IOperableTrigger Trigger(DateTimeOffset nextFireTimeUtc, string? calendarName = null)
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(nextFireTimeUtc)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever());

        if (calendarName is not null)
        {
            builder = builder.WithCalendarName(calendarName);
        }

        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.NextFireTimeUtc = nextFireTimeUtc;
        return trigger;
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
