using Quartz.Impl.AdoJobStore;
using Quartz.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Specialized;
using System.Data.SQLite;
using System.Diagnostics;

using FirebirdSql.Data.FirebirdClient;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

using MySqlConnector;

using Npgsql;

using Oracle.ManagedDataAccess.Client;

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Jobs;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

[NonParallelizable]
public class AdoJobStoreSmokeTest
{
    private static readonly Dictionary<string, string> dbConnectionStrings = new()
    {
        ["SQLite"] = "Data Source=test.db;Version=3;",
        ["SQLite-Microsoft"] = "Data Source=test.db;"
    };
    private readonly bool clearJobs = true;
    private readonly bool scheduleJobs = true;
    private readonly List<IScheduler> createdSchedulers = [];

    private const string KeyResetEvent = "ResetEvent";
    private const string KeyJobFired = "JobFired";

    [Test]
    [Category("db-sqlserver")]
    [TestCaseSource(nameof(GetSmokeTestCases))]
    public Task TestSqlServer(string serializerType, ProviderMode providerMode)
    {
        return RunAdoJobStoreTest(TestConstants.DefaultSqlServerProvider, "SQLServer", serializerType, typeof(SqlServerDelegate), providerMode: providerMode);
    }

    /// <summary>
    /// A memory-optimized <c>QRTZ_LOCKS</c> refuses the <c>UPDLOCK,ROWLOCK</c> hints the store's own SQL
    /// Server handler locks with, so the handler has to be named. This names it the way 4.x code does;
    /// <see cref="TheLegacyLockHandlerKeyIsTheOneThatLocksAMemoryOptimizedTable" /> names it the way a
    /// 3.x configuration file does.
    /// </summary>
    /// <remarks>
    /// Until #3903 this passed <c>quartz.jobStore.lockHandler.type</c> in a property bag the harness never
    /// read, ran the store's default handler and failed on the first lock — and was <c>[Explicit]</c>,
    /// so nothing noticed.
    /// </remarks>
    [Test]
    [Category("db-sqlserver")]
    [TestCase("stj")]
    public Task TestSqlServerMemoryOptimizedTables(string serializerType)
    {
        return RunAdoJobStoreTest(
            TestConstants.DefaultSqlServerProvider,
            "SQLServerMOT",
            serializerType,
            typeof(SqlServerDelegate),
            configureStore: store => store.UseLockHandler<SqlServerMemoryOptimizedUpdateRowLockHandler>());
    }

    /// <summary>
    /// The legacy key names the handler, the bridge registers it and the store locks with it. With the
    /// store's own SQL Server handler instead, the first lock fails with "The table option 'rowlock' is
    /// not supported with memory optimized tables".
    /// </summary>
    [Test]
    [Category("db-sqlserver")]
    public async Task TheLegacyLockHandlerKeyIsTheOneThatLocksAMemoryOptimizedTable()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "TestScheduler_SQLServerMOT_LegacyKey",
            ["quartz.scheduler.instanceId"] = "AUTO",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz",
            ["quartz.jobStore.driverDelegateType"] = typeof(SqlServerDelegate).AssemblyQualifiedNameWithoutVersion(),
            ["quartz.jobStore.lockHandler.type"] = typeof(SqlServerMemoryOptimizedUpdateRowLockHandler).AssemblyQualifiedNameWithoutVersion(),
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.jobStore.clustered"] = "true",
            ["quartz.dataSource.default.connectionString"] = GetConnectionString("SQLServerMOT"),
            ["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider,
        };

        ServiceCollection services = new ServiceCollection();
        services.AddQuartz(properties);
        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            AdoJobStoreBase store = (AdoJobStoreBase) provider.GetRequiredService<IJobStore>();
            store.LockHandler.Should().BeOfType<SqlServerMemoryOptimizedUpdateRowLockHandler>(
                "quartz.jobStore.lockHandler.type named it, and Initialize builds a handler only when it was handed none");

            // A firing takes TRIGGER_ACCESS on the memory-optimized row, which is what the handler is for.
            TaskCompletionSource fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            scheduler.Context[KeyJobFired] = fired;
            await scheduler.Clear();
            await scheduler.Start();
            await scheduler.ScheduleJob(
                JobBuilder.Create<SignalingJob>().WithIdentity("mot-legacy-key").Build(),
                TriggerBuilder.Create().WithIdentity("mot-legacy-key").StartNow().Build());

            await fired.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            // Before the provider goes, since the store and its connections belong to it.
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    [Category("db-postgres")]
    [TestCaseSource(nameof(GetSmokeTestCases))]
    public Task TestPostgreSql(string serializerType, ProviderMode providerMode)
    {
        return RunAdoJobStoreTest("Npgsql", "PostgreSQL", serializerType, typeof(PostgreSQLDelegate), providerMode: providerMode);
    }

    [Test]
    [Category("db-mysql")]
    [TestCaseSource(nameof(GetSmokeTestCases))]
    public Task TestMySql(string serializerType, ProviderMode providerMode)
    {
        return RunAdoJobStoreTest("MySqlConnector", "MySQL", serializerType, typeof(MySQLDelegate), providerMode: providerMode);
    }

    [Test]
    [Category("db-sqlite")]
    [TestCaseSource(nameof(GetSmokeTestCases))]
    public async Task TestSQLiteMicrosoft(string serializerType, ProviderMode providerMode)
    {
        var dbFilename = $"test-sqlite-ms-{serializerType}-{providerMode}.db";
        dbConnectionStrings["SQLite-Microsoft"] = $"Data Source={dbFilename};";

        if (File.Exists(dbFilename))
        {
            File.Delete(dbFilename);
        }

        using (var connection = new SqliteConnection(GetConnectionString("SQLite-Microsoft")))
        {
            await connection.OpenAsync();
            var sql = LoadSqliteTableScript();

            var command = new SqliteCommand(sql, connection);
            command.ExecuteNonQuery();

            connection.Close();
        }

        await RunAdoJobStoreTest("SQLite-Microsoft", "SQLite-Microsoft", serializerType, typeof(SQLiteDelegate), clustered: false, providerMode: providerMode);
    }

    /// <summary>
    /// Registers the same database through the driver's own factory, which is the registration a
    /// trimmed application makes: nothing here names a type for Quartz to resolve.
    /// </summary>
    /// <remarks>
    /// Oracle is the one that takes more than a factory, and the two things it takes are exactly what
    /// the name path reads off <c>OracleCommand</c> and <c>OracleParameter</c> by reflection. Without
    /// the first, every statement binds by position; without the second, a job data map over two
    /// kilobytes does not fit in an <c>OracleDbType.Raw</c> parameter, which is what
    /// <see cref="System.Data.DbType.Binary" /> means to ODP.NET.
    /// </remarks>
    private static void UseDriverFactory(IPersistentStoreBuilder store, string dbProvider, string connectionString)
    {
        switch (dbProvider)
        {
            case "SqlServer":
                store.UseSqlServer(SqlClientFactory.Instance, connectionString);
                break;
            case "Npgsql":
                store.UsePostgres(NpgsqlFactory.Instance, connectionString);
                break;
            case "MySqlConnector":
                store.UseMySqlConnector(MySqlConnectorFactory.Instance, connectionString);
                break;
            case "SQLite-Microsoft":
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                break;
            case "SQLite":
                store.UseSystemDataSqlite(SQLiteFactory.Instance, connectionString);
                break;
            case "Firebird":
                store.UseFirebird(FirebirdClientFactory.Instance, connectionString);
                break;
            case "OracleODPManaged":
                store.UseOracle(
                    OracleClientFactory.Instance,
                    connectionString,
                    configureCommand: command => ((OracleCommand) command).BindByName = true,
                    configureBinaryParameter: parameter => ((OracleParameter) parameter).OracleDbType = OracleDbType.Blob);
                break;
            default:
                throw new ArgumentException($"No factory registration for provider '{dbProvider}'", nameof(dbProvider));
        }
    }

    private static string LoadSqliteTableScript()
    {
        var path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    [Test]
    [Category("db-firebird")]
    [TestCaseSource(nameof(GetSmokeTestCases))]
    public Task TestFirebird(string serializerType, ProviderMode providerMode)
    {
        return RunAdoJobStoreTest("Firebird", "Firebird", serializerType, typeof(FirebirdDelegate), clustered: false, providerMode: providerMode);
    }

    [Test]
    [Category("db-oracle")]
    [TestCaseSource(nameof(GetSmokeTestCases))]
    public Task TestOracleODPManaged(string serializerType, ProviderMode providerMode)
    {
        return RunAdoJobStoreTest("OracleODPManaged", "Oracle", serializerType, typeof(OracleDelegate), providerMode: providerMode);
    }

    [Test]
    [Category("db-sqlite")]
    [TestCaseSource(nameof(GetSmokeTestCases))]
    public async Task TestSQLite(string serializerType, ProviderMode providerMode)
    {
        var dbFilename = $"test-sqlite-{serializerType}-{providerMode}.db";
        dbConnectionStrings["SQLite"] = $"Data Source={dbFilename};Version=3;";

        while (File.Exists(dbFilename))
        {
            File.Delete(dbFilename);
        }

        SQLiteConnection.CreateFile(dbFilename);

        using (var connection = new SQLiteConnection(GetConnectionString("SQLite")))
        {
            await connection.OpenAsync();
            var sql = LoadSqliteTableScript();

            var command = new SQLiteCommand(sql, connection);
            command.ExecuteNonQuery();

            connection.Close();
        }

        await RunAdoJobStoreTest("SQLite", "SQLite", serializerType, typeof(SQLiteDelegate), clustered: false, providerMode: providerMode);
    }

    public static string[] GetSerializerTypes() => ["stj", "newtonsoft"];

    /// <summary>
    /// How the driver is reached: by the name Quartz resolves its types from, or through the
    /// <see cref="System.Data.Common.DbProviderFactory" /> the driver ships.
    /// </summary>
    public enum ProviderMode
    {
        /// <summary>The provider name, which resolves the driver's types with <c>Type.GetType</c>.</summary>
        Name,

        /// <summary>The driver's own factory, which names no type — what a trimmed application uses.</summary>
        Factory,
    }

    /// <summary>
    /// Every dialect runs both ways round, and both serializers.
    /// </summary>
    /// <remarks>
    /// Not the full cross product. The serializer decides what goes into a blob and the provider decides
    /// how the blob is bound, so the two axes do not interact; crossing them would double the container
    /// time of every database leg to re-prove the serializer against a second way of making a command.
    /// </remarks>
    public static IEnumerable<TestCaseData> GetSmokeTestCases()
    {
        foreach (string serializerType in GetSerializerTypes())
        {
            yield return new TestCaseData(serializerType, ProviderMode.Name);
        }

        yield return new TestCaseData(TestConstants.DefaultSerializerType, ProviderMode.Factory);
    }

    /// <summary>
    /// Builds a scheduler through the typed API and runs the smoke workload against it.
    /// </summary>
    /// <remarks>
    /// The dialect delegate is a <see cref="Type" /> and anything else the store needs goes through
    /// <paramref name="configureStore" />. It used to be a <c>NameValueCollection</c> of flat keys, of
    /// which only <c>quartz.jobStore.driverDelegateType</c> was ever read — a bag that looks like
    /// configuration and mostly is not is how the memory-optimized test lost its lock handler (#3903).
    /// </remarks>
    private async Task RunAdoJobStoreTest(
        string dbProvider,
        string connectionStringId,
        string serializerType,
        Type driverDelegateType,
        bool clustered = true,
        ProviderMode providerMode = ProviderMode.Name,
        Action<IPersistentStoreBuilder> configureStore = null)
    {
        string schedulerInstanceId = $"instance_{dbProvider}_{connectionStringId}_{serializerType}_{providerMode}_{Guid.NewGuid():N}".Replace('-', '_');
        string schedulerName = $"TestScheduler_{dbProvider}_{connectionStringId}_{serializerType}_{providerMode}".Replace('-', '_');
        QuartzSchedulerBuilder config = QuartzSchedulerBuilder.Create(q => q
            .ConfigureScheduler(o =>
            {
                o.InstanceId = schedulerInstanceId;
                o.InstanceName = schedulerName;
            })
            .UseDefaultThreadPool(x => x.MaxConcurrency = 10)
            .UsePersistentStore(ConfigureStore));

        void ConfigureStore(IPersistentStoreBuilder store)
        {
            store.ConfigureStore(o =>
            {
                o.StoreJobDataAsStrings = false;
                o.SchemaProvisioning = SchemaProvisioning.Validate;
                o.MisfireThreshold = TimeSpan.FromSeconds(60);
            });

            if (clustered)
            {
                store.UseClustering(c =>
                {
                    c.CheckinInterval = TimeSpan.FromMilliseconds(1000);
                });
            }

            if (providerMode == ProviderMode.Factory)
            {
                UseDriverFactory(store, dbProvider, GetConnectionString(connectionStringId));
            }
            else
            {
                store.UseGenericDatabase(dbProvider, GetConnectionString(connectionStringId));
            }

            // Every dialect names its own delegate, whichever way the provider was registered above.
            store.Services.Replace(ServiceDescriptor.Singleton(typeof(IDriverDelegate), driverDelegateType));

            configureStore?.Invoke(store);

            if (serializerType == "stj")
            {
                store.UseSystemTextJsonSerializer(j =>
                {
                    j.AddCalendarSerializer<CustomCalendar>(new CustomSystemTextJsonCalendarSerializer());
                    j.AddTriggerSerializer<CustomTrigger>(new CustomSystemTextJsonTriggerSerializer());
                });
            }
            else if (serializerType == "newtonsoft")
            {
                store.UseNewtonsoftJsonSerializer(j =>
                {
                    j.AddCalendarSerializer<CustomCalendar>(new CustomNewtonsoftCalendarSerializer());
                    j.AddTriggerSerializer<CustomTrigger>(new CustomNewtonsoftTriggerSerializer());
                }, registerTriggerConverters: true);
            }
            else
            {
                throw new ArgumentException($"Cannot handle serializer type: {serializerType}", nameof(serializerType));
            }
        }

        // Clear any old errors from the log
        //testLoggerHelper.ClearLogs();

        // First we must get a reference to a scheduler
        IScheduler scheduler = await config.BuildScheduler();
        createdSchedulers.Add(scheduler);
        SmokeTestPerformer performer = new SmokeTestPerformer();
        await performer.Test(scheduler, clearJobs, scheduleJobs);

        //Assert.IsEmpty(testLoggerHelper.LogEntries.Where(le => le.LogLevel == LogLevel.Error), "Found error from logging output");
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task ShouldBeAbleToUseMixedProperties()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz";
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
        properties["quartz.jobStore.dataSource"] = "default";
        properties["quartz.jobStore.useProperties"] = false.ToString();
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;

        string connectionString = GetConnectionString("SQLServer");
        properties["quartz.dataSource.default.connectionString"] = connectionString;
        properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;

        ISchedulerFactory sf = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        IScheduler scheduler = await sf.GetScheduler();
        await scheduler.Clear();

        var jobWithData = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey("datajob", "jobgroup"))
            .UsingJobData("testkey", "testvalue")
            .Build();

        IOperableTrigger triggerWithData = new SimpleTriggerImpl
        {
            Key = new TriggerKey("datatrigger", "triggergroup"),
            StartTimeUtc = TimeProvider.System.GetUtcNow(),
            RepeatCount = 20,
            RepeatInterval = TimeSpan.FromSeconds(5)
        };
        triggerWithData.JobDataMap.Add("testkey", "testvalue");
        triggerWithData.EndTimeUtc = DateTime.UtcNow.AddYears(10);
        triggerWithData.StartTimeUtc = DateTime.Now.AddMilliseconds(1000L);
        await scheduler.ScheduleJob(jobWithData, triggerWithData);
        await scheduler.Shutdown();

        // try again with changing the useproperties against same set of data
        properties["quartz.jobStore.useProperties"] = true.ToString();
        sf = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        scheduler = await sf.GetScheduler();

        var triggerWithDataFromDb = await scheduler.GetTrigger(new TriggerKey("datatrigger", "triggergroup"));
        var jobWithDataFromDb = await scheduler.GetJobDetail(new JobKey("datajob", "jobgroup"));
        Assert.That(triggerWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));
        Assert.That(jobWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));

        // once more
        await scheduler.DeleteJob(jobWithData.Key);
        await scheduler.ScheduleJob(jobWithData, triggerWithData);
        await scheduler.Shutdown();

        properties["quartz.jobStore.useProperties"] = false.ToString();
        sf = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        scheduler = await sf.GetScheduler();
        createdSchedulers.Add(scheduler);

        triggerWithDataFromDb = await scheduler.GetTrigger(new TriggerKey("datatrigger", "triggergroup"));
        jobWithDataFromDb = await scheduler.GetJobDetail(new JobKey("datajob", "jobgroup"));
        Assert.That(triggerWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));
        Assert.That(jobWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));
    }

    [Test]
    [Explicit]
    [Category("db-sqlserver")]
    [TestCaseSource(nameof(GetSerializerTypes))]
    public async Task TestSqlServerStress(string serializerType)
    {
        await RunAdoJobStoreTest(TestConstants.DefaultSqlServerProvider, "SQLServer", serializerType, typeof(SqlServerDelegate));

        NameValueCollection properties = new NameValueCollection();

        properties["quartz.scheduler.instanceName"] = "TestScheduler";
        properties["quartz.scheduler.instanceId"] = "instance_one";
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz";
        properties["quartz.jobStore.useProperties"] = "false";
        properties["quartz.jobStore.dataSource"] = "default";
        properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
        properties["quartz.jobStore.clustered"] = true.ToString();
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";

        string connectionString = GetConnectionString("SQLServer");
        properties["quartz.dataSource.default.connectionString"] = connectionString;
        properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;

        // First we must get a reference to a scheduler
        ISchedulerFactory sf = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        IScheduler scheduler = await sf.GetScheduler();

        try
        {
            await scheduler.Clear();

            if (scheduleJobs)
            {
                ICalendar cronCalendar = new CronCalendar("0/5 * * * * ?");
                ICalendar holidayCalendar = new HolidayCalendar();

                for (int i = 0; i < 100000; ++i)
                {
                    ITrigger trigger = new SimpleTriggerImpl
                    {
                        Key = new TriggerKey("calendarsTrigger", "test"),
                        StartTimeUtc = TimeProvider.System.GetUtcNow(),
                        RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
                        RepeatInterval = TimeSpan.FromSeconds(1)
                    };
                    var jd = JobBuilder.Create<NoOpJob>()
                        .WithIdentity(new JobKey("testJob", "test"))
                        .Build();
                    await scheduler.ScheduleJob(jd, trigger);
                }
            }
            await scheduler.Start();
            await Task.Delay(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task TestGetTriggerKeysWithLike()
    {
        var scheduler = await CreateScheduler(null);

        await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith("foo"));
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task TestGetTriggerKeysWithEquals()
    {
        var scheduler = await CreateScheduler(null);

        await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("bar"));
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task TestGetJobKeysWithLike()
    {
        var scheduler = await CreateScheduler(null);

        await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupStartsWith("foo"));
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task TestGetJobKeysWithEquals()
    {
        var scheduler = await CreateScheduler(null);

        await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("bar"));
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task JobTypeNotFoundShouldNotBlock()
    {
        NameValueCollection properties = new NameValueCollection();
        properties.Add("quartz.scheduler.typeLoadHelper.type", typeof(SpecialClassLoadHelper).AssemblyQualifiedName);
        var scheduler = await CreateScheduler(properties);

        await scheduler.DeleteJobs([new JobKey("bad"), new JobKey("good")]);

        await scheduler.Start();

        var manualResetEvent = new ManualResetEventSlim(false);
        scheduler.Context[KeyResetEvent] = manualResetEvent;

        IJobDetail goodJob = JobBuilder.Create<GoodJob>().WithIdentity("good").Build();
        IJobDetail badJob = JobBuilder.Create<BadJob>().WithIdentity("bad").Build();

        var now = DateTimeOffset.UtcNow;
        ITrigger goodTrigger = TriggerBuilder.Create().WithIdentity("good").ForJob(goodJob)
            .StartAt(now.AddMilliseconds(1))
            .Build();

        ITrigger badTrigger = TriggerBuilder.Create().WithIdentity("bad").ForJob(badJob)
            .StartAt(now)
            .Build();

        var toSchedule = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>();
        toSchedule.Add(badJob, new List<ITrigger>
        {
            badTrigger
        });
        toSchedule.Add(goodJob, new List<ITrigger>
        {
            goodTrigger
        });
        await scheduler.ScheduleJobs(toSchedule, new ScheduleJobOptions { Replace = true });

        manualResetEvent.Wait(TimeSpan.FromSeconds(20));

        Assert.That(await scheduler.GetTriggerState(badTrigger.Key), Is.Not.EqualTo(TriggerState.Blocked));
    }

    private async Task<IScheduler> CreateScheduler(NameValueCollection properties)
    {
        properties ??= new NameValueCollection();

        properties["quartz.scheduler.instanceName"] = "TestScheduler";
        properties["quartz.scheduler.instanceId"] = "instance_one";
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz";
        properties["quartz.jobStore.useProperties"] = "false";
        properties["quartz.jobStore.dataSource"] = "default";
        properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
        properties["quartz.jobStore.clustered"] = "false";
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";

        properties["quartz.dataSource.default.connectionString"] = TestConstants.SqlServerConnectionString;
        properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;

        // First we must get a reference to a scheduler
        ISchedulerFactory sf = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        IScheduler scheduler = await sf.GetScheduler();
        createdSchedulers.Add(scheduler);
        return scheduler;
    }

    [Test]
    [Explicit]
    public async Task StressTest()
    {
        NameValueCollection properties = new NameValueCollection();

        properties["quartz.scheduler.instanceName"] = "TestScheduler";
        properties["quartz.scheduler.instanceId"] = "instance_one";
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        properties["quartz.jobStore.misfireThreshold"] = "60000";
        properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz";
        properties["quartz.jobStore.useProperties"] = "false";
        properties["quartz.jobStore.dataSource"] = "default";
        properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
        properties["quartz.jobStore.clustered"] = "false";
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";

        properties["quartz.dataSource.default.connectionString"] = TestConstants.SqlServerConnectionString;
        properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;

        // First we must get a reference to a scheduler
        ISchedulerFactory sf = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        IScheduler scheduler = await sf.GetScheduler();

        try
        {
            await scheduler.Clear();

            var lonelyJob = JobBuilder.Create()
                .OfType<SimpleRecoveryJob>()
                .WithIdentity(new JobKey("lonelyJob", "lonelyGroup"))
                .StoreDurably(true)
                .RequestRecovery(true)
                .Build();

            await scheduler.AddJob(lonelyJob);
            await scheduler.AddJob(lonelyJob, new AddJobOptions { Replace = true });

            string schedId = scheduler.SchedulerInstanceId;

            var job = JobBuilder.Create()
                .OfType<SimpleRecoveryJob>()
                .WithIdentity(new JobKey("job_to_use", schedId))
                .Build();

            for (int i = 0; i < 100000; ++i)
            {
                IOperableTrigger trigger = new SimpleTriggerImpl
                {
                    Key = new TriggerKey("stressing_simple"),
                    StartTimeUtc = TimeProvider.System.GetUtcNow(),
                    RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
                    RepeatInterval = TimeSpan.FromSeconds(1)
                };
                trigger.StartTimeUtc = DateTime.Now.AddMilliseconds(i);
                await scheduler.ScheduleJob(job, trigger);
            }

            for (int i = 0; i < 100000; ++i)
            {
                IOperableTrigger ct = new CronTriggerImpl("stressing_cron", TriggerKey.DefaultGroup, "0/1 * * * * ?");
                ct.StartTimeUtc = DateTime.Now.AddMilliseconds(i);
                await scheduler.ScheduleJob(job, ct);
            }

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            await scheduler.Start();
            await Task.Delay(TimeSpan.FromMinutes(3));
            stopwatch.Stop();
            Console.WriteLine("Took: " + stopwatch.Elapsed);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    public class BadJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    /// <summary>
    /// Completes the <see cref="TaskCompletionSource" /> the test left in the scheduler context, so the
    /// test knows a firing went all the way through the store's locks.
    /// </summary>
    public class SignalingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            ((TaskCompletionSource) context.Scheduler.Context[KeyJobFired]).TrySetResult();
            return default;
        }
    }

    public class GoodJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                ((ManualResetEventSlim) context.Scheduler.Context[KeyResetEvent]).Wait(TimeSpan.FromSeconds(20));
                return default;
            }
            catch (SchedulerException ex)
            {
                throw new JobExecutionException(ex);
            }
            catch (ThreadInterruptedException ex)
            {
                throw new JobExecutionException(ex);
            }
            catch (TimeoutException ex)
            {
                throw new JobExecutionException(ex);
            }
        }
    }

    public class SpecialClassLoadHelper : ITypeLoader
    {
        public Type LoadType(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (typeof(BadJob) == Type.GetType(name))
            {
                throw new TypeLoadException();
            }
            return Type.GetType(name, false);
        }
    }

    /// <summary>
    /// Shuts down whatever the test left running. Each scheduler here is built from a container of its
    /// own, so there is no shared repository to sweep — the ones to shut down are the ones this fixture
    /// created.
    /// </summary>
    [TearDown]
    public async Task ShutdownSchedulers()
    {
        foreach (var scheduler in createdSchedulers)
        {
            await scheduler.Shutdown();
        }

        createdSchedulers.Clear();
    }

    private static string GetConnectionString(string connectionStringId)
    {
        return connectionStringId switch
        {
            "Oracle" => Environment.GetEnvironmentVariable("ORACLE_CONNECTION_STRING")
                ?? "Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521)))(CONNECT_DATA=(SERVICE_NAME=xe)));User Id=system;Password=oracle;",
            "SQLServer" => TestConstants.SqlServerConnectionString,
            "SQLServerMOT" => TestConstants.SqlServerConnectionStringMOT,
            "MySQL" => Environment.GetEnvironmentVariable("MYSQL_CONNECTION_STRING")
                ?? "Server = localhost; Database = quartznet; Uid = quartznet; Pwd = quartznet",
            "PostgreSQL" => TestConstants.PostgresConnectionString,
            "SQLite" => dbConnectionStrings["SQLite"],
            "SQLite-Microsoft" => dbConnectionStrings["SQLite-Microsoft"],
            "Firebird" => Environment.GetEnvironmentVariable("FIREBIRD_CONNECTION_STRING")
                ?? "User=SYSDBA;Password=masterkey;Database=/firebird/data/quartz.fdb;DataSource=localhost;Port=3050;Dialect=3;Charset=NONE;Role=;Connection lifetime=15;Pooling=true;MinPoolSize=0;MaxPoolSize=50;Packet Size=8192;ServerType=0;",
            _ => throw new Exception("Unknown connection string id: " + connectionStringId)
        };
    }
}

public class SimpleRecoveryJob : IJob
{
    private const string Count = "count";

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a
    /// <see cref="ITrigger" /> fires that is associated with
    /// the <see cref="IJob" />.
    /// </summary>
    public virtual async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // delay for ten seconds
        await Task.Delay(TimeSpan.FromSeconds(10));

        JobDataMap data = context.JobDetail.JobDataMap;
        int count;
        if (data.ContainsKey(Count))
        {
            count = data.GetInt(Count);
        }
        else
        {
            count = 0;
        }
        count++;
        data[Count] = count;
    }
}

[DisallowConcurrentExecution]
[PersistJobDataAfterExecution]
public class SimpleRecoveryStatefulJob : SimpleRecoveryJob
{
}
