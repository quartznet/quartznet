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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Microsoft.Data.Sqlite;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;
using Quartz.Xml;

namespace Quartz.Tests.Integration.Xml;

/// <summary>
/// A start re-applies each declared trigger: it reads the stored trigger, works the replacement's fire
/// times out from what it read, and replaces the trigger. A firing between the read and the replace is
/// not undone by it, and an unchanged declaration leaves the trigger's run state as it is, an
/// acquisition of it included (#4324).
/// </summary>
/// <remarks>
/// <para>
/// The firing in between is made through the store right after the processor's read, as another node of
/// a cluster would, by the scheduler the processor is handed. The processor is the one <c>AddQuartz</c>
/// declarations go through: <see cref="XMLSchedulingDataProcessor" />, with its declarations given in
/// code. Each scheduler is never started; the trigger's fire times are a day ahead and the firings are
/// driven through the store, so nothing waits on the clock.
/// </para>
/// <para>
/// Relative scheduling is on (<see cref="XMLSchedulingDataProcessor.ScheduleTriggerRelativeToReplacedTrigger" />),
/// the setting that keeps the stored schedule across a start, so that the replacement's fire times are the
/// ones it read. Clustered, on PostgreSQL: <c>ReplacedTriggerClusteredPostgresTest</c>.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class DeclaredTriggerReappliedUnderAFiringTest
{
    public enum StoreKind
    {
        InMemory,
        Sqlite,
    }

    private const string SchedulerName = "declared-trigger-reapplied-under-a-firing";
    private static readonly TimeSpan interval = TimeSpan.FromHours(1);
    private static readonly JobKey jobKey = new JobKey("declared", "reapplied");
    private static readonly TriggerKey triggerKey = new TriggerKey("t-declared", "reapplied");

    // The test's own clock, a day ahead of the real one: the stores read the real clock only for misfires,
    // and nothing here is late.
    private DateTimeOffset epoch;
    private DateTimeOffset now;

    private IJobStore store;
    private IScheduler scheduler;
    private string databaseFile;
    private RecordingLogProvider logs;

    [SetUp]
    public void StartTheClock()
    {
        epoch = DateTimeOffset.UtcNow.AddDays(1);
        epoch = epoch.AddTicks(-(epoch.Ticks % TimeSpan.TicksPerSecond));
        now = epoch;
    }

    [TearDown]
    public async Task ShutDownAndDeleteDatabase()
    {
        if (logs != null)
        {
            LogProvider.SetCurrentLogProvider(null);
            logs = null;
        }

        if (scheduler != null)
        {
            await scheduler.Shutdown();
            scheduler = null;
        }

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

    /// <summary>
    /// The issue's case: the trigger fires between the processor's read and its replace, and the replace
    /// does not put back the fire time that has just run.
    /// </summary>
    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite, Category = "db-sqlite")]
    public async Task AFiringBetweenTheReadAndTheReplaceIsNotUndone(StoreKind kind)
    {
        await StoreFiredOnce(kind);

        await Reapply(Every(interval), fireAfterRead: true);

        ITrigger stored = await store.RetrieveTrigger(triggerKey);
        stored.GetPreviousFireTimeUtc().Should().Be(epoch.Add(interval), "the firing in between is the trigger's last firing");
        stored.GetNextFireTimeUtc().Should().Be(epoch.Add(interval).Add(interval),
            "the fire time that ran in between is not due again: the replace works from the row as it is when it is made");
        ((ISimpleTrigger) stored).TimesTriggered.Should().Be(2, "both firings are counted");

        now = now.AddMinutes(1);
        (await AcquireDue()).Should().BeEmpty($"{epoch.Add(interval):O} has run, and runs once");
    }

    /// <summary>
    /// A changed declaration starts its schedule from the trigger's last firing, and that is the firing in
    /// between, not the one the processor read.
    /// </summary>
    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite, Category = "db-sqlite")]
    public async Task AChangedDeclarationIsScheduledFromTheFiringInBetween(StoreKind kind)
    {
        await StoreFiredOnce(kind);

        await Reapply(Every(TimeSpan.FromMinutes(30)), fireAfterRead: true);

        ITrigger stored = await store.RetrieveTrigger(triggerKey);
        ((ISimpleTrigger) stored).RepeatInterval.Should().Be(TimeSpan.FromMinutes(30), "the changed declaration is applied");
        stored.GetPreviousFireTimeUtc().Should().Be(epoch.Add(interval));
        stored.GetNextFireTimeUtc().Should().Be(epoch.AddMinutes(90),
            "the next fire time is the new schedule's first after the trigger's last firing, which is the one in between; "
            + "from the firing the processor read it would be half an hour ago");
    }

    /// <summary>
    /// An unchanged declaration re-applied while the trigger is acquired leaves the acquisition firable:
    /// the trigger's fire times, its count and its state are what they were.
    /// </summary>
    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite, Category = "db-sqlite")]
    public async Task AnUnchangedDeclarationLeavesAnAcquisitionOfItFirable(StoreKind kind)
    {
        await StoreFiredOnce(kind);

        now = now.Add(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(10));
        IReadOnlyCollection<IOperableTrigger> held = await AcquireDue();
        held.Should().ContainSingle("the premise: the trigger is acquired for its next fire time");

        await Reapply(Every(interval), fireAfterRead: false);

        (await scheduler.GetTriggerState(triggerKey)).Should().Be(TriggerState.Normal);
        ITrigger stored = await store.RetrieveTrigger(triggerKey);
        stored.GetNextFireTimeUtc().Should().Be(epoch.Add(interval));
        ((ISimpleTrigger) stored).TimesTriggered.Should().Be(1);

        now = now.AddSeconds(10);
        IReadOnlyCollection<TriggerFiredResult> fired = await store.TriggersFired(held);
        fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "re-applying the same declaration is not a new trigger, and the acquisition of the stored one still holds");
    }

    /// <summary>
    /// A declaration whose replace is refused every time — the trigger fires between every read and the
    /// replace — is left as stored with a warning, and the start goes on: the declarations after it are
    /// applied, and nothing throws out of the start.
    /// </summary>
    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite, Category = "db-sqlite")]
    public async Task ADeclarationThatKeepsLosingToFiringsIsLeftAsStoredAndTheStartGoesOn(StoreKind kind)
    {
        await StoreFiredOnce(kind);
        TriggerKey next = new TriggerKey("t-next", triggerKey.Group);

        // Before the processor is built: it takes its logger as it is constructed.
        logs = new RecordingLogProvider();
        LogProvider.SetCurrentLogProvider(logs);

        DeclaringProcessor processor = new DeclaringProcessor(
            JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).Build(),
            TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).StartNow()
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(30)).RepeatForever()).Build(),
            TriggerBuilder.Create().WithIdentity(next).ForJob(jobKey).StartNow()
                .WithSimpleSchedule(s => s.WithInterval(interval).RepeatForever()).Build());

        int refused = 0;
        IScheduler refusing = A.Fake<IScheduler>(x => x.Wrapping(scheduler));
        A.CallTo(() => refusing.RescheduleJob(triggerKey, A<ITrigger>._, A<CancellationToken>._))
            .ReturnsLazily(() =>
            {
                // A store whose trigger fired since the read answers that it replaced nothing.
                refused++;
                return Task.FromResult<DateTimeOffset?>(null);
            });

        Func<Task> start = () => processor.ScheduleJobs(refusing);

        await start.Should().NotThrowAsync("the store refused every write, so nothing is at risk and the start goes on");
        refused.Should().Be(8, "the declaration is worked out again after each refusal, eight times in all");
        logs.Warnings.Should().ContainSingle(x => x.Contains(triggerKey.ToString()) && x.Contains("left as stored"));
        ((ISimpleTrigger) await store.RetrieveTrigger(triggerKey)).RepeatInterval.Should().Be(interval,
            "the trigger is left as stored, and the changed declaration applies at the next start");
        (await store.RetrieveTrigger(next)).Should().NotBeNull("the declarations after the one left as stored are still applied");
    }

    /// <summary>
    /// The control for the one above: what keeping a row leaves out. A trigger in <c>ERROR</c> is still
    /// stored afresh by a start, which is how a redeploy that fixes its job brings it back.
    /// </summary>
    [Test]
    [Category("db-sqlite")]
    public async Task AnUnchangedDeclarationStillBringsBackATriggerInError()
    {
        await StoreFiredOnce(StoreKind.Sqlite);
        using (SqliteConnection connection = new SqliteConnection($"Data Source={databaseFile}"))
        {
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = 'ERROR'";
            (await command.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await Reapply(Every(interval), fireAfterRead: false);

        (await scheduler.GetTriggerState(triggerKey)).Should().Be(TriggerState.Normal);
    }

    private static Action<TriggerBuilder> Every(TimeSpan every)
    {
        return t => t.WithSimpleSchedule(s => s.WithInterval(every).RepeatForever());
    }

    /// <summary>
    /// A store that holds the declared job and trigger, the trigger fired once at <see cref="epoch" />, and
    /// the clock half an hour on.
    /// </summary>
    private async Task StoreFiredOnce(StoreKind kind)
    {
        store = kind == StoreKind.InMemory ? new RAMJobStore() : NewSqliteStore();

        DirectSchedulerFactory.Instance.CreateScheduler(SchedulerName, "one", new ZeroSizeThreadPool(), store);
        scheduler = await DirectSchedulerFactory.Instance.GetScheduler(SchedulerName);

        await scheduler.ScheduleJob(
            JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).Build(),
            TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).StartAt(epoch)
                .WithSimpleSchedule(s => s.WithInterval(interval).RepeatForever()).Build());

        await FireAndComplete();
        now = now.AddMinutes(30);
    }

    /// <summary>
    /// What a start does with a declared trigger the store holds. With <paramref name="fireAfterRead" />,
    /// the trigger's next fire time comes and it is fired and completed through the store right after the
    /// processor's first read of it.
    /// </summary>
    private async Task Reapply(Action<TriggerBuilder> schedule, bool fireAfterRead)
    {
        TriggerBuilder declared = TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).StartNow();
        schedule(declared);

        DeclaringProcessor processor = new DeclaringProcessor(JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).Build(), declared.Build());

        await processor.ScheduleJobs(fireAfterRead ? FiringAfterRead(scheduler, FireInBetween) : scheduler);

        async Task FireInBetween()
        {
            now = now.AddMinutes(30);
            await FireAndComplete();
        }
    }

    /// <summary>
    /// <paramref name="scheduler" />, which runs <paramref name="afterRead" /> once, right after the first read
    /// of the declared trigger and before the read is handed back: the processor then works from a trigger
    /// that has fired since.
    /// </summary>
    private static IScheduler FiringAfterRead(IScheduler scheduler, Func<Task> afterRead)
    {
        Func<Task> pending = afterRead;
        IScheduler firing = A.Fake<IScheduler>(x => x.Wrapping(scheduler));
        A.CallTo(() => firing.GetTrigger(A<TriggerKey>._, A<CancellationToken>._))
            .ReturnsLazily(async (TriggerKey key, CancellationToken cancellationToken) =>
            {
                ITrigger read = await scheduler.GetTrigger(key, cancellationToken);
                if (read != null && Interlocked.Exchange(ref pending, null) is { } fire)
                {
                    await fire();
                }

                return read;
            });

        return firing;
    }

    private Task<IReadOnlyCollection<IOperableTrigger>> AcquireDue()
    {
        return store.AcquireNextTriggers(now.AddMinutes(1), 1, TimeSpan.Zero);
    }

    private async Task FireAndComplete()
    {
        IReadOnlyCollection<IOperableTrigger> acquired = await AcquireDue();
        acquired.Should().ContainSingle("the premise: the trigger is due");

        TriggerFiredBundle bundle = (await store.TriggersFired(acquired)).Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("the premise: nothing stands in the trigger's way");

        // The bundle's trigger, as the run shell's: a persistent store advances a copy of its own.
        SchedulerInstruction instruction = bundle.Trigger.ExecutionComplete(A.Fake<IJobExecutionContext>(), null);
        await store.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, instruction);
    }

    /// <summary>
    /// A store over a file of its own, with a data source of its own.
    /// </summary>
    private JobStoreTX NewSqliteStore()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-declared-reapplied-{Guid.NewGuid():N}.db");
        using (SqliteConnection connection = new SqliteConnection($"Data Source={databaseFile}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = File.ReadAllText(ResolveRepositoryFile("database", "tables", "tables_sqlite.sql"));
            command.ExecuteNonQuery();
        }

        string dataSource = $"declared-reapplied-{Guid.NewGuid():N}";
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
    /// The processor a start runs declarations through, given its declarations in code, with existing data
    /// overwritten and relative scheduling on.
    /// </summary>
    private sealed class DeclaringProcessor : XMLSchedulingDataProcessor
    {
        private readonly List<IJobDetail> jobs;
        private readonly List<ITrigger> triggers;

        public DeclaringProcessor(IJobDetail job, params ITrigger[] triggers) : base(new SimpleTypeLoadHelper())
        {
            jobs = [job];
            this.triggers = [.. triggers];
            OverWriteExistingData = true;
            ScheduleTriggerRelativeToReplacedTrigger = true;
        }

        protected override IReadOnlyList<IJobDetail> LoadedJobs => jobs;

        protected override IReadOnlyList<ITrigger> LoadedTriggers => triggers;
    }

    /// <summary>
    /// Records the warnings logged while it is the current log provider.
    /// </summary>
    private sealed class RecordingLogProvider : ILogProvider
    {
        private readonly List<string> warnings = new List<string>();

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (warnings)
                {
                    return warnings.ToArray();
                }
            }
        }

        public Logger GetLogger(string name)
        {
            return (logLevel, messageFunc, exception, formatParameters) =>
            {
                // LibLog asks whether a level is enabled by passing no message.
                if (messageFunc is null)
                {
                    return true;
                }

                if (logLevel >= LogLevel.Warn)
                {
                    string message = messageFunc();
                    if (formatParameters is { Length: > 0 })
                    {
                        try
                        {
                            message = string.Format(CultureInfo.InvariantCulture, message, formatParameters);
                        }
                        catch (FormatException)
                        {
                            // A template with named holes; the template is enough to tell warnings apart.
                        }
                    }

                    lock (warnings)
                    {
                        warnings.Add(message);
                    }
                }

                return true;
            };
        }

        public IDisposable OpenNestedContext(string message) => NoContext.Instance;

        public IDisposable OpenMappedContext(string key, object value, bool destructure = false) => NoContext.Instance;

        private sealed class NoContext : IDisposable
        {
            public static readonly NoContext Instance = new NoContext();

            public void Dispose()
            {
            }
        }
    }

    public sealed class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
