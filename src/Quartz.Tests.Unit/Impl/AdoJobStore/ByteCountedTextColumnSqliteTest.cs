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

using System.Text;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Every statement that writes a text the store cuts to fit cuts it to the column's width in bytes, where
/// the dialect says its columns count bytes (#3905).
/// </summary>
/// <remarks>
/// On a SQLite file, which stores whatever it is given: a delegate that reports a narrow width in bytes
/// stands in for Firebird's and Oracle's, so what comes back is what the statement bound. The databases
/// that refuse an over-long text are the integration legs' to run.
/// </remarks>
public sealed class ByteCountedTextColumnSqliteTest
{
    /// <summary>The width in bytes the delegate reports for every text the store cuts.</summary>
    private const int Width = 99;

    private const string SchedulerName = "ByteCountedTextColumnSqliteTest";
    private const string Group = "bytes";

    private SqliteTestDatabase database = null!;
    private LocalTransactionJobStore store = null!;

    [SetUp]
    public async Task BuildStore()
    {
        database = new SqliteTestDatabase("byte-counted-text");

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: "node-a"),
            storeOptions: TestJobStores.StoreOptions("byte-counted-text"),
            dbProvider: new DbProvider("SQLite-Microsoft", database.ConnectionString),
            driverDelegate: new ByteCountingSqliteDelegate()));

        // Initialized but not started, so no misfire loop moves anything underneath the tests.
        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: "node-a"));
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
        database.Dispose();
    }

    [Test]
    public async Task APauseIsCutToTheWidthInBytesOnEveryTableItIsWrittenTo()
    {
        PauseDetails details = new()
        {
            Reason = new string('é', PauseDetails.MaxReasonLength),
            RequestedBy = string.Concat(Enumerable.Repeat("\U0001F600", PauseDetails.MaxRequestedByLength / 2)),
        };

        // 49 two-byte characters are 98 bytes, and the 50th would be 100; 24 pairs are 96, and the
        // 25th would be 100.
        string reason = new('é', 49);
        string requestedBy = string.Concat(Enumerable.Repeat("\U0001F600", 24));

        TriggerKey trigger = await Schedule("paused-alone", "jobs-a", "triggers-a");
        (await store.PauseTriggerWith(trigger, details)).Should().BeTrue();
        ShouldHold(await store.GetTriggerPause(trigger), reason, requestedBy, "the trigger's own row");

        TriggerKey inTriggerGroup = await Schedule("paused-by-group", "jobs-b", "triggers-b");
        await store.PauseTriggerGroupsWith(GroupMatcher<TriggerKey>.GroupEquals("triggers-b"), details);
        ShouldHold(await store.GetTriggerGroupPause("triggers-b"), reason, requestedBy, "the paused trigger group's row");
        ShouldHold(await store.GetTriggerPause(inTriggerGroup), reason, requestedBy, "a trigger the group pause moved");

        TriggerKey inJobGroup = await Schedule("paused-by-job-group", "jobs-c", "triggers-c");
        await store.PauseJobGroupsWith(GroupMatcher<JobKey>.GroupEquals("jobs-c"), details);
        ShouldHold(await store.GetJobGroupPause("jobs-c"), reason, requestedBy, "the paused job group's row");
        ShouldHold(await store.GetTriggerPause(inJobGroup), reason, requestedBy, "a trigger the job group pause moved");
    }

    [Test]
    public async Task AProgressMessageIsCutToTheWidthInBytes()
    {
        await Schedule("reporting", "jobs", Group, startAt: DateTimeOffset.UtcNow);
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = 1
        });
        IOperableTrigger firing = acquired.Should().ContainSingle().Subject;
        await store.TriggersFired([firing]);

        await store.UpdateFireInstanceProgress(firing.FireInstanceId!, new FireInstanceProgress
        {
            Percent = 50,
            Message = new string('日', FireInstanceProgress.MaxMessageLength)
        });

        FireInstance listed = (await store.QueryFireInstances(new FireInstanceQuery())).Items.Should().ContainSingle().Subject;
        listed.ProgressMessage.Should().Be(new string('日', 33), "33 three-byte characters are the 99 bytes the column holds");
    }

    [Test]
    public async Task AnErrorMessageIsCutToTheWidthInBytes()
    {
        IDbProvider dbProvider = new DbProvider("SQLite-Microsoft", database.ConnectionString);
        ByteCountingSqliteDelegate driverDelegate = new();
        driverDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = AdoConstants.DefaultTablePrefix,
            SchedulerName = SchedulerName,
            InstanceId = "node-a",
            DbProvider = dbProvider,
            TypeLoader = new SimpleTypeLoader(),
        });

        using AdoExecutionHistoryStore history = new(
            dbProvider,
            driverDelegate,
            Options.Create(new ExecutionHistoryOptions()),
            Options.Create(new QuartzSchedulerOptions { InstanceName = SchedulerName }));

        await history.AddExecution(new ExecutionHistoryEntry(
            SchedulerName: SchedulerName,
            SchedulerInstanceId: "node-a",
            JobGroup: "jobs",
            JobName: "failing",
            TriggerGroup: Group,
            TriggerName: "once",
            FiredAtUtc: DateTimeOffset.UtcNow,
            Duration: TimeSpan.FromSeconds(1),
            Succeeded: false,
            ExceptionMessage: new string('é', StdAdoDelegate.MaxErrorMessageLength))
        {
            EntryId = "failed-once"
        });

        ExecutionHistoryEntry? read = await history.GetExecution(SchedulerName, "failed-once");

        read.Should().NotBeNull("the history store drops a row whose insert fails");
        read!.ExceptionMessage.Should().Be(new string('é', 49), "49 two-byte characters are 98 bytes, and a 50th would be 100");
    }

    private static void ShouldHold(PauseInfo? pause, string reason, string requestedBy, string where)
    {
        pause.Should().NotBeNull("{0} records the pause", where);
        pause!.Reason.Should().Be(reason, "{0} is written through a statement that cuts to the column's bytes", where);
        pause.RequestedBy.Should().Be(requestedBy, "and never inside a surrogate pair");
        Encoding.UTF8.GetByteCount(pause.Reason!).Should().BeLessThanOrEqualTo(Width);
    }

    private async Task<TriggerKey> Schedule(string name, string jobGroup, string triggerGroup, DateTimeOffset? startAt = null)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, jobGroup).Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, triggerGroup)
            .ForJob(job)
            .StartAt(startAt ?? DateTimeOffset.UtcNow.AddHours(1))
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);
        await store.ScheduleJob(job, trigger);
        return trigger.Key;
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// The shipped SQLite delegate, reporting a narrow width in bytes for every text the store cuts, as
    /// the Firebird and Oracle delegates report theirs.
    /// </summary>
    private sealed class ByteCountingSqliteDelegate : SQLiteDelegate
    {
        internal override int? TextColumnByteWidth(string column) => Width;
    }
}
