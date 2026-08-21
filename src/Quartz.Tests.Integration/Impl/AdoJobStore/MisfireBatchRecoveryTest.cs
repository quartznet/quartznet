using System.Data.Common;

using FakeItEasy;

using Microsoft.Data.Sqlite;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Serialization.Newtonsoft;
using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// End-to-end coverage of batched misfire recovery against a real database. Runs on SQLite so it needs
/// no container, and exercises the batch SELECT for every trigger flavour: the SIMPLE/CRON fast path
/// that comes back on the joined row, the simple-properties types that need the follow-up query, and
/// blob-stored triggers.
/// </summary>
[NonParallelizable]
public class MisfireBatchRecoveryTest
{
    private const string TablePrefix = "QRTZ_";
    private const string DataSourceName = "misfire-batch-sqlite";
    private const string SchedulerName = "MisfireBatchRecoveryTest";

    private string dbFileName;
    private IDbProvider dbProvider;
    private CountingSQLiteDelegate.Counter commandCounter;
    private readonly List<TestLocalTransactionJobStore> jobStores = [];

    [SetUp]
    public async Task SetUp()
    {
        jobStores.Clear();
        dbFileName = $"test-misfire-batch-{Guid.NewGuid():N}.db";

        await using (var connection = new SqliteConnection($"Data Source={dbFileName};"))
        {
            await connection.OpenAsync();
            await using var command = new SqliteCommand(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        // The store reads through the provider it is constructed with, so the test only has to build
        // one — there is no registry to publish it to.
        dbProvider = new DbProvider("SQLite-Microsoft", $"Data Source={dbFileName};");

        commandCounter = new CountingSQLiteDelegate.Counter();
        CountingSQLiteDelegate.CurrentCounter = commandCounter;
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (TestLocalTransactionJobStore jobStore in jobStores)
        {
            await jobStore.Shutdown();
        }

        jobStores.Clear();

        CountingSQLiteDelegate.CurrentCounter = null;

        SqliteConnection.ClearAllPools();
        if (File.Exists(dbFileName))
        {
            try
            {
                File.Delete(dbFileName);
            }
            catch (IOException)
            {
                // the file is only test scratch space, leaving it behind is not worth failing over
            }
        }
    }

    [Test]
    public async Task RecoversMisfiredTriggersOfEveryType()
    {
        TestLocalTransactionJobStore jobStore = await CreateJobStore();

        // One of each shape the batch read has to deal with:
        //  - SIMPLE and CRON arrive complete on the joined row
        //  - DAILY_I and CAL_INT need the SIMPROP_TRIGGERS follow-up query
        await StoreMisfiredTrigger(jobStore, "simple", TriggerBuilder.Create()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever().WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow)));

        await StoreMisfiredTrigger(jobStore, "cron", TriggerBuilder.Create()
            .WithCronSchedule("0 * * * * ?", x => x.WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed)));

        await StoreMisfiredTrigger(jobStore, "daily", TriggerBuilder.Create()
            .WithDailyTimeIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Minute).WithMisfireInstruction(DailyTimeIntervalTriggerMisfireInstruction.FireAndProceed)));

        await StoreMisfiredTrigger(jobStore, "calint", TriggerBuilder.Create()
            .WithCalendarIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Minute).WithMisfireInstruction(CalendarIntervalTriggerMisfireInstruction.FireAndProceed)));

        //  - a custom trigger with no persistence delegate, which lands in BLOB_TRIGGERS
        await StoreMisfiredBlobTrigger(jobStore, "blob");

        RecoverMisfiredJobsResult result = await jobStore.RecoverMisfires();

        result.ProcessedMisfiredTriggerCount.Should().Be(5, "every misfired trigger should have been materialized and handled");
        result.HasMoreMisfiredTriggers.Should().BeFalse();

        // The blob trigger round-tripped through serialization with its custom state intact.
        var recoveredBlobTrigger = (CustomTrigger) await jobStore.GetTrigger(new TriggerKey("blob", "misfire-test"));
        recoveredBlobTrigger.Should().NotBeNull();
        recoveredBlobTrigger.SomeCustomProperty.Should().BeTrue();

        // All of them are back in WAITING with a next fire time in the future.
        foreach (var name in new[] { "simple", "cron", "daily", "calint", "blob" })
        {
            var key = new TriggerKey(name, "misfire-test");

            (await jobStore.GetTriggerState(key)).Should().Be(TriggerState.Normal, "trigger '{0}' should be rescheduled, not left misfired", name);

            IOperableTrigger trigger = await jobStore.GetTrigger(key);
            trigger.Should().NotBeNull();
            trigger.NextFireTimeUtc.Should().NotBeNull("trigger '{0}' should have a next fire time", name);
            trigger.NextFireTimeUtc.Value.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1), "trigger '{0}' should have moved forward", name);
        }
    }

    /// <summary>
    /// The read side is what this asserts: the whole batch is materialized by one statement rather than
    /// one per trigger, so the read count does not grow with the batch size. (SQLite reports
    /// CanCreateBatch = false, so the writes here still go out individually — the batched write path is
    /// covered by the unit tests.)
    /// </summary>
    [Test]
    public async Task ReadCountDoesNotGrowWithBatchSize()
    {
        int readsForTwo = await CountReadsRecovering(2);
        int readsForForty = await CountReadsRecovering(40);

        readsForForty.Should().Be(readsForTwo,
            "recovering 40 triggers took {0} reads against {1} for two, so it is still reading per trigger",
            readsForForty, readsForTwo);
    }

    private async Task<int> CountReadsRecovering(int triggerCount)
    {
        TestLocalTransactionJobStore jobStore = await CreateJobStore(maxMisfiresToHandleAtATime: triggerCount);
        await jobStore.Clear();

        for (var i = 0; i < triggerCount; i++)
        {
            await StoreMisfiredTrigger(jobStore, "simple" + i, TriggerBuilder.Create()
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever().WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow)));
        }

        commandCounter.Reset();
        RecoverMisfiredJobsResult result = await jobStore.RecoverMisfires();

        result.ProcessedMisfiredTriggerCount.Should().Be(triggerCount);

        commandCounter.Commands.Should().NotContain(x => x.Contains("LEFT JOIN") && x.Contains("@triggerName"),
            "the per-trigger SelectTrigger read should not be used during batch recovery");

        return commandCounter.Commands.Count(x => x.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task ReportsHasMoreWhenBatchIsTruncated()
    {
        TestLocalTransactionJobStore jobStore = await CreateJobStore(maxMisfiresToHandleAtATime: 2);

        for (var i = 0; i < 5; i++)
        {
            await StoreMisfiredTrigger(jobStore, "simple" + i, TriggerBuilder.Create()
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever().WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow)));
        }

        RecoverMisfiredJobsResult result = await jobStore.RecoverMisfires();

        result.ProcessedMisfiredTriggerCount.Should().Be(2);
        result.HasMoreMisfiredTriggers.Should().BeTrue();
    }

    /// <summary>
    /// Stores a job and a trigger whose start time is well in the past, which is what puts it in the
    /// WAITING state with an overdue next fire time — exactly what misfire recovery looks for.
    /// </summary>
    private static async Task StoreMisfiredTrigger(TestLocalTransactionJobStore jobStore, string name, TriggerBuilder<IJob> builder)
    {
        IJobDetail job = JobBuilder.Create<MisfireTestJob>()
            .WithIdentity(name, "misfire-test")
            .Build();

        var trigger = (IOperableTrigger) builder
            .WithIdentity(name, "misfire-test")
            .ForJob(job)
            .StartAt(DateTimeOffset.UtcNow.AddHours(-2))
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);

        // A past start time is not enough on its own — a cron schedule, for one, just recomputes forward
        // to the next matching time. Pin the stored next fire time into the past, which is what a trigger
        // that nobody got around to firing actually looks like.
        trigger.NextFireTimeUtc = DateTimeOffset.UtcNow.AddHours(-1);

        await jobStore.ScheduleJob(job, trigger);
    }

    /// <summary>
    /// Stores an overdue trigger that has no <see cref="ITriggerPersistenceDelegate" />, so it is
    /// persisted as a blob and has to be picked up by the batch read's blob follow-up query.
    /// </summary>
    private static async Task StoreMisfiredBlobTrigger(TestLocalTransactionJobStore jobStore, string name)
    {
        IJobDetail job = JobBuilder.Create<MisfireTestJob>()
            .WithIdentity(name, "misfire-test")
            .Build();

        var trigger = new CustomTrigger
        {
            Key = new TriggerKey(name, "misfire-test"),
            JobKey = job.Key,
            CronExpressionString = "0 * * * * ?",
            StartTimeUtc = DateTimeOffset.UtcNow.AddHours(-2),
            MisfireInstructionCode = MisfireInstruction.CronTrigger.FireOnceNow
        };

        trigger.ComputeFirstFireTimeUtc(null);
        trigger.NextFireTimeUtc = DateTimeOffset.UtcNow.AddHours(-1);

        await jobStore.ScheduleJob(job, trigger);
    }

    private async Task<TestLocalTransactionJobStore> CreateJobStore(int maxMisfiresToHandleAtATime = 20)
    {
        var registry = new NewtonsoftJsonSerializerRegistry()
            .AddTriggerSerializer<CustomTrigger>(new CustomNewtonsoftTriggerSerializer());

        var serializer = new NewtonsoftJsonObjectSerializer(registry);

        var jobStore = new TestLocalTransactionJobStore(serializer, dbProvider, new CountingSQLiteDelegate(), maxMisfiresToHandleAtATime)
        {
            InstanceId = "AUTO",
            InstanceName = SchedulerName,
        };

        // Initialized but deliberately not started: SchedulerStarted() spawns the MisfireHandler loop,
        // which sweeps for misfires on its own thread every MisfireHandlerFrequency - and that defaults
        // to MisfireThreshold, one second here. A sweep landing between storing the triggers and the
        // RecoverMisfires() call below recovers some of them first, and the explicit pass then sees a
        // short count. These tests drive recovery by hand, so the loop can only race them.
        await jobStore.Initialize();
        await jobStore.Clear();

        jobStores.Add(jobStore);

        return jobStore;
    }

    private static string LoadSqliteTableScript()
    {
        var path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    public sealed class MisfireTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class TestLocalTransactionJobStore : LocalTransactionJobStore
    {
        public TestLocalTransactionJobStore(IObjectSerializer serializer, IDbProvider dbProvider, IDriverDelegate driverDelegate, int maxMisfiresToHandleAtATime)
            : base(
                TestJobStores.Signaler(),
                TestJobStores.TypeLoader(),
                TimeProvider.System,
                TestJobStores.SchedulerOptions(),
                TestJobStores.StoreOptions(DataSourceName, MisfireBatchRecoveryTest.TablePrefix, options =>
                {
                    options.MaxMisfiresToHandleAtATime = maxMisfiresToHandleAtATime;
                    // Anything overdue by more than a moment counts as misfired.
                    options.MisfireThreshold = TimeSpan.FromSeconds(1);
                }),
                TestJobStores.ClusteringOptions(),
                serializer,
                dbProvider,
                driverDelegate,
                TestJobStores.LockHandler())
        {
        }

        public ValueTask<RecoverMisfiredJobsResult> RecoverMisfires()
            => DoRecoverMisfires(Guid.NewGuid(), CancellationToken.None);
    }

    /// <summary>
    /// Counts every statement the delegate issues through <see cref="StdAdoDelegate.PrepareCommand" />,
    /// which is how the tests tell a batched pass from a per-trigger one. Batched statements do not go
    /// through here, so a batching provider reads as an even lower count.
    /// </summary>
    private sealed class CountingSQLiteDelegate : SQLiteDelegate
    {
        internal sealed class Counter
        {
            private readonly List<string> commands = [];

            public IReadOnlyList<string> Commands
            {
                get
                {
                    lock (commands)
                    {
                        return commands.ToArray();
                    }
                }
            }

            public void Record(string commandText)
            {
                lock (commands)
                {
                    commands.Add(commandText);
                }
            }

            public void Reset()
            {
                lock (commands)
                {
                    commands.Clear();
                }
            }
        }

        // The delegate is constructed reflectively from DriverDelegateType, so the counter is handed
        // over out of band.
        internal static Counter CurrentCounter;

        private readonly Counter counter = CurrentCounter;

        public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            counter?.Record(commandText);
            return base.PrepareCommand(cth, commandText);
        }
    }
}
