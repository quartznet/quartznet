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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using FakeItEasy;

using Microsoft.Data.Sqlite;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Matchers;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;
using Quartz.Xml;

namespace Quartz.Tests.Integration.Xml;

/// <summary>
/// A declared trigger the store already holds is re-applied when the scheduler is created, and keeps what
/// it has done: how many times it has fired, and a pause an operator put on it (#4128). A changed schedule
/// starts fresh.
/// </summary>
/// <remarks>
/// Each start is a scheduler of its own over the same store: the in-memory store is one instance handed to
/// both, the SQLite one is one file. The schedulers are never started; the firings are driven through the
/// store, a minute apart and up to an hour ahead, so nothing waits on the clock.
/// </remarks>
[NonParallelizable]
public sealed class TriggerRunStateReappliedAtStartupTest
{
    public enum StoreKind
    {
        InMemory,
        Sqlite,
    }

    private const string SchedulerName = "trigger-run-state-reapplied";
    private static readonly JobKey jobKey = new JobKey("declared", "run-state");
    private static readonly TriggerKey triggerKey = new TriggerKey("t-declared", "run-state");

    private RAMJobStore memoryStore;
    private string databaseFile;
    private IJobStore store;
    private IScheduler scheduler;

    [TearDown]
    public async Task ShutDownAndDeleteDatabase()
    {
        if (scheduler != null)
        {
            await scheduler.Shutdown();
            scheduler = null;
        }

        memoryStore = null;
        store = null;

        if (databaseFile is null)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }

        databaseFile = null;
    }

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite, Category = "db-sqlite")]
    public async Task AFiniteRepeatCountIsNotRestartedByAStart(StoreKind kind)
    {
        await Start(kind, FourFiringsAMinuteApart);
        (await FireOnce()).Should().BeTrue();
        (await FireOnce()).Should().BeTrue();

        await Start(kind, FourFiringsAMinuteApart);

        ((ISimpleTrigger) await store.RetrieveTrigger(triggerKey)).TimesTriggered.Should().Be(2,
            "the trigger fired twice before the start, and the start re-applies the same trigger");
        (await FireUntilDone()).Should().Be(2,
            "a repeat count of three is four firings in all, two of which happened before the start");
    }

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite, Category = "db-sqlite")]
    public async Task AChangedRepeatCountStartsTheCountAgain(StoreKind kind)
    {
        await Start(kind, FourFiringsAMinuteApart);
        (await FireOnce()).Should().BeTrue();
        (await FireOnce()).Should().BeTrue();

        await Start(kind, t => t.WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(1)).WithRepeatCount(5)));

        ((ISimpleTrigger) await store.RetrieveTrigger(triggerKey)).TimesTriggered.Should().Be(0,
            "the declaration changed its schedule, which makes it a new trigger");
        (await FireUntilDone()).Should().Be(6, "a changed schedule wins, and runs its whole count");
    }

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite, Category = "db-sqlite")]
    public async Task ATriggerPausedOnItsOwnStaysPausedAcrossAStart(StoreKind kind)
    {
        await Start(kind);
        await scheduler.PauseTrigger(triggerKey);

        await Start(kind);

        (await scheduler.GetTriggerState(triggerKey)).Should().Be(TriggerState.Paused,
            "an operator paused this trigger, and a redeploy is not somebody resuming it");
        (await FireOnce()).Should().BeFalse("a paused trigger is never acquired");
    }

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite, Category = "db-sqlite")]
    public async Task ATriggerInAPausedGroupStaysPausedAcrossAStart(StoreKind kind)
    {
        await Start(kind);
        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(triggerKey.Group));

        await Start(kind);

        (await scheduler.GetTriggerState(triggerKey)).Should().Be(TriggerState.Paused, "a paused group imposes its pause on what is stored into it");

        await scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals(triggerKey.Group));
        (await scheduler.GetTriggerState(triggerKey)).Should().Be(TriggerState.Normal, "resuming the group resumes its triggers, as before");
    }

    /// <summary>
    /// Only a start keeps a pause through a replace. A trigger handed to <c>RescheduleJob</c> by the
    /// application is a new schedule, and ends an individual pause as it always has — including a trigger
    /// read back from the store just after a start re-applied it.
    /// </summary>
    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite, Category = "db-sqlite")]
    public async Task RescheduleJobByTheApplicationStillEndsAnIndividualPause(StoreKind kind)
    {
        await Start(kind);
        await scheduler.PauseTrigger(triggerKey);

        await Start(kind);
        (await scheduler.GetTriggerState(triggerKey)).Should().Be(TriggerState.Paused, "the premise: the start kept the pause");

        ITrigger readBack = await scheduler.GetTrigger(triggerKey);
        (await scheduler.RescheduleJob(triggerKey, readBack)).Should().NotBeNull();

        (await scheduler.GetTriggerState(triggerKey)).Should().Be(TriggerState.Normal,
            "a replace the application asks for decides the state afresh, which is Java's contract");
    }

    private static void FourFiringsAMinuteApart(TriggerBuilder trigger)
    {
        trigger.WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(1)).WithRepeatCount(3));
    }

    /// <summary>
    /// What a process start does: the scheduler before it is shut down, and a new one over the same store
    /// re-applies the declared job and trigger as the container's processor does.
    /// </summary>
    private async Task Start(StoreKind kind, Action<TriggerBuilder> schedule = null)
    {
        if (scheduler != null)
        {
            await scheduler.Shutdown();
        }

        store = kind == StoreKind.InMemory ? memoryStore ??= new RAMJobStore() : NewSqliteStore();

        DirectSchedulerFactory.Instance.CreateScheduler(SchedulerName, "one", new ZeroSizeThreadPool(), store);
        scheduler = await DirectSchedulerFactory.Instance.GetScheduler(SchedulerName);

        await new DeclaringProcessor(schedule).ScheduleJobs(scheduler);
    }

    /// <summary>
    /// A store over the same file each time, with a data source of its own, as a second process would have:
    /// a store's shutdown shuts its own down.
    /// </summary>
    private JobStoreTX NewSqliteStore()
    {
        if (databaseFile is null)
        {
            databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-run-state-{Guid.NewGuid():N}.db");
            InstallSchema($"Data Source={databaseFile}");
        }

        string dataSource = $"run-state-{Guid.NewGuid():N}";
        DBConnectionManager.Instance.AddConnectionProvider(dataSource, new DbProvider("SQLite-Microsoft", $"Data Source={databaseFile}"));

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
        serializer.Initialize();

        return new JobStoreTX
        {
            DataSource = dataSource,
            TablePrefix = "QRTZ_",
            DriverDelegateType = typeof(SQLiteDelegate).AssemblyQualifiedName,
            ObjectSerializer = serializer,
        };
    }

    /// <summary>
    /// Fires the trigger at its next fire time within the hour, completing the firing the way the run shell
    /// does: the trigger decides what comes next. Answers whether there was a firing to make.
    /// </summary>
    private async Task<bool> FireOnce()
    {
        IReadOnlyCollection<IOperableTrigger> acquired = await store.AcquireNextTriggers(DateTimeOffset.UtcNow.AddHours(1), 1, TimeSpan.Zero);
        if (acquired.Count == 0)
        {
            return false;
        }

        acquired.Should().ContainSingle(x => x.Key.Equals(triggerKey), "the declared trigger is the only one stored");

        IReadOnlyCollection<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        TriggerFiredBundle bundle = fired.Should().ContainSingle().Which.TriggerFiredBundle;
        bundle.Should().NotBeNull("the trigger is due and nothing stands in its way");

        // The bundle's trigger, as the run shell's: a persistent store advances a copy of its own.
        SchedulerInstruction instruction = bundle.Trigger.ExecutionComplete(A.Fake<IJobExecutionContext>(), null);
        await store.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, instruction);
        return true;
    }

    private async Task<int> FireUntilDone()
    {
        const int Ceiling = 20;

        for (int fired = 0; fired < Ceiling; fired++)
        {
            if (!await FireOnce())
            {
                return fired;
            }
        }

        return Ceiling;
    }

    private static void InstallSchema(string connectionString)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(ResolveRepositoryFile("database", "tables", "tables_sqlite.sql"));
        command.ExecuteNonQuery();
    }

    private static string ResolveRepositoryFile(params string[] pathSegments)
    {
        string relativePath = Path.Combine(pathSegments);
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find '{relativePath}' above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// Declares one job and one trigger as the container's processor declares registrations: freshly built
    /// for each start, starting now, existing data overwritten.
    /// </summary>
    private sealed class DeclaringProcessor : XMLSchedulingDataProcessor
    {
        private readonly List<IJobDetail> jobs;
        private readonly List<ITrigger> triggers;

        public DeclaringProcessor(Action<TriggerBuilder> schedule) : base(new SimpleTypeLoadHelper())
        {
            jobs = [JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).Build()];

            TriggerBuilder trigger = TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).StartNow();
            if (schedule is null)
            {
                trigger.WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever());
            }
            else
            {
                schedule(trigger);
            }

            triggers = [trigger.Build()];
        }

        public override bool OverWriteExistingData => true;

        protected override IReadOnlyList<IJobDetail> LoadedJobs => jobs;

        protected override IReadOnlyList<ITrigger> LoadedTriggers => triggers;
    }

    public sealed class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            return Task.CompletedTask;
        }
    }
}
