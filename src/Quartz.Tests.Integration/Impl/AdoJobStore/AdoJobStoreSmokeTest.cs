using System.Collections.Specialized;
using System.Data.SQLite;
using System.Diagnostics;

using Microsoft.Data.Sqlite;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

public class AdoJobStoreSmokeTest
{
    private static readonly Dictionary<string, string> dbConnectionStrings = new();
    private readonly bool clearJobs = true;
    private readonly bool scheduleJobs = true;

    private const string KeyResetEvent = "ResetEvent";

    static AdoJobStoreSmokeTest()
    {
        dbConnectionStrings["Oracle"] = "Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521)))(CONNECT_DATA=(SERVICE_NAME=xe)));User Id=system;Password=oracle;";
        dbConnectionStrings["SQLServer"] = TestConstants.SqlServerConnectionString;
        dbConnectionStrings["SQLServerMOT"] = TestConstants.SqlServerConnectionStringMOT;
        dbConnectionStrings["MySQL"] = "Server = localhost; Database = quartznet; Uid = quartznet; Pwd = quartznet";
        dbConnectionStrings["PostgreSQL"] = TestConstants.PostgresConnectionString;
        dbConnectionStrings["SQLite"] = "Data Source=test.db;Version=3;";
        dbConnectionStrings["SQLite-Microsoft"] = "Data Source=test.db;";
        dbConnectionStrings["Firebird"] = "User=SYSDBA;Password=masterkey;Database=/firebird/data/quartz.fdb;DataSource=localhost;Port=3050;Dialect=3;Charset=NONE;Role=;Connection lifetime=15;Pooling=true;MinPoolSize=0;MaxPoolSize=50;Packet Size=8192;ServerType=0;";
    }

    [Test]
    [Category("db-sqlserver")]
    [TestCaseSource(nameof(GetSerializerTypes))]
    public Task TestSqlServer(string serializerType)
    {
        var properties = new NameValueCollection
        {
            ["quartz.jobStore.driverDelegateType"] = typeof(Quartz.Impl.AdoJobStore.SqlServerDelegate).AssemblyQualifiedNameWithoutVersion()
        };
        return RunAdoJobStoreTest(TestConstants.DefaultSqlServerProvider, "SQLServer", serializerType, properties);
    }

    [Test]
    [Category("db-sqlserver")]
    [TestCaseSource(nameof(GetSerializerTypes))]
    public Task TestSqlServerMemoryOptimizedTables(string serializerType)
    {
        var properties = new NameValueCollection
        {
            ["quartz.jobStore.driverDelegateType"] = typeof(Quartz.Impl.AdoJobStore.SqlServerDelegate).AssemblyQualifiedNameWithoutVersion(),
            ["quartz.jobStore.lockHandler.type"] = typeof(Quartz.Impl.AdoJobStore.UpdateLockRowSemaphoreMOT).AssemblyQualifiedNameWithoutVersion()
        };
        return RunAdoJobStoreTest(TestConstants.DefaultSqlServerProvider, "SQLServerMOT", serializerType, properties);
    }

    [Test]
    [Category("db-postgres")]
    [TestCaseSource(nameof(GetSerializerTypes))]
    public Task TestPostgreSql(string serializerType)
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz";
        return RunAdoJobStoreTest("Npgsql", "PostgreSQL", serializerType, properties);
    }

    [Test]
    [Category("db-mysql")]
    [TestCaseSource(nameof(GetSerializerTypes))]
    public Task TestMySql(string serializerType)
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz";
        return RunAdoJobStoreTest("MySqlConnector", "MySQL", serializerType, properties);
    }

    [Test]
    [Category("db-sqlite")]
    [TestCaseSource(nameof(GetSerializerTypes))]
    public async Task TestSQLiteMicrosoft(string serializerType)
    {
        var dbFilename = $"test-sqlite-ms-{serializerType}.db";

        if (File.Exists(dbFilename))
        {
            File.Delete(dbFilename);
        }

        using (var connection = new SqliteConnection(dbConnectionStrings["SQLite-Microsoft"]))
        {
            await connection.OpenAsync();
            var sql = LoadSqliteTableScript();

            var command = new SqliteCommand(sql, connection);
            command.ExecuteNonQuery();

            connection.Close();
        }

        NameValueCollection properties = new NameValueCollection();
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SQLiteDelegate, Quartz";
        await RunAdoJobStoreTest("SQLite-Microsoft", "SQLite-Microsoft", serializerType, properties, clustered: false);
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
    [TestCaseSource(nameof(GetSerializerTypes))]
    public Task TestFirebird(string serializerType)
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.FirebirdDelegate, Quartz";
        return RunAdoJobStoreTest("Firebird", "Firebird", serializerType, properties);
    }

    [Test]
    [Category("db-oracle")]
    [TestCaseSource(nameof(GetSerializerTypes))]
    public Task TestOracleODPManaged(string serializerType)
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.OracleDelegate, Quartz";
        return RunAdoJobStoreTest("OracleODPManaged", "Oracle", serializerType, properties);
    }

    [Test]
    [Category("db-sqlite")]
    [TestCaseSource(nameof(GetSerializerTypes))]
    public async Task TestSQLite(string serializerType)
    {
        var dbFilename = $"test-sqlite-{serializerType}.db";

        while (File.Exists(dbFilename))
        {
            File.Delete(dbFilename);
        }

        SQLiteConnection.CreateFile(dbFilename);

        using (var connection = new SQLiteConnection(dbConnectionStrings["SQLite"]))
        {
            await connection.OpenAsync();
            var sql = LoadSqliteTableScript();

            var command = new SQLiteCommand(sql, connection);
            command.ExecuteNonQuery();

            connection.Close();
        }

        NameValueCollection properties = new NameValueCollection();
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SQLiteDelegate, Quartz";
        await RunAdoJobStoreTest("SQLite", "SQLite", serializerType, properties, clustered: false);
    }

    public static string[] GetSerializerTypes() => ["stj", "newtonsoft"];

    private Task RunAdoJobStoreTest(string dbProvider, string connectionStringId, string serializerType)
    {
        return RunAdoJobStoreTest(dbProvider, connectionStringId, serializerType, null);
    }

    private async Task RunAdoJobStoreTest(
        string dbProvider,
        string connectionStringId,
        string serializerType,
        NameValueCollection extraProperties,
        bool clustered = true)
    {
        var config = SchedulerBuilder.Create("instance_one", "TestScheduler");
        config.UseDefaultThreadPool(x =>
        {
            x.MaxConcurrency = 10;
        });
        config.MisfireThreshold = TimeSpan.FromSeconds(60);

        config.UsePersistentStore(store =>
        {
            store.UseProperties = false;
            store.PerformSchemaValidation = true;

            if (clustered)
            {
                store.UseClustering(c =>
                {
                    c.CheckinInterval = TimeSpan.FromMilliseconds(1000);
                });
            }

            store.UseGenericDatabase(dbProvider, "server-01", db =>
                db.ConnectionString = dbConnectionStrings[connectionStringId]
            );

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

                    j.RegisterTriggerConverters = true;
                    j.AddTriggerSerializer<CustomTrigger>(new CustomNewtonsoftTriggerSerializer());
                });
            }
            else
            {
                throw new ArgumentException($"Cannot handle serializer type: {serializerType}", nameof(serializerType));
            }
        });

        if (extraProperties is not null)
        {
            foreach (string key in extraProperties.Keys)
            {
                config.SetProperty(key, extraProperties[key]);
            }
        }

        // Clear any old errors from the log
        //testLoggerHelper.ClearLogs();

        // First we must get a reference to a scheduler
        IScheduler sched = await config.BuildScheduler();
        SmokeTestPerformer performer = new SmokeTestPerformer();
        await performer.Test(sched, clearJobs, scheduleJobs);

        //Assert.IsEmpty(testLoggerHelper.LogEntries.Where(le => le.LogLevel == LogLevel.Error), "Found error from logging output");
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task ShouldBeAbleToUseMixedProperties()
    {
        NameValueCollection properties = new NameValueCollection();
        properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz";
        properties["quartz.jobStore.dataSource"] = "default";
        properties["quartz.jobStore.useProperties"] = false.ToString();
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;

        dbConnectionStrings.TryGetValue("SQLServer", out var connectionString);
        properties["quartz.dataSource.default.connectionString"] = connectionString;
        properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;

        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler sched = await sf.GetScheduler();
        await sched.Clear();

        var jobWithData = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey("datajob", "jobgroup"))
            .UsingJobData("testkey", "testvalue")
            .Build();

        IOperableTrigger triggerWithData = new SimpleTriggerImpl("datatrigger", "triggergroup", 20, TimeSpan.FromSeconds(5));
        triggerWithData.JobDataMap.Add("testkey", "testvalue");
        triggerWithData.EndTimeUtc = DateTime.UtcNow.AddYears(10);
        triggerWithData.StartTimeUtc = DateTime.Now.AddMilliseconds(1000L);
        await sched.ScheduleJob(jobWithData, triggerWithData);
        await sched.Shutdown();

        // try again with changing the useproperties against same set of data
        properties["quartz.jobStore.useProperties"] = true.ToString();
        sf = new StdSchedulerFactory(properties);
        sched = await sf.GetScheduler();

        var triggerWithDataFromDb = await sched.GetTrigger(new TriggerKey("datatrigger", "triggergroup"));
        var jobWithDataFromDb = await sched.GetJobDetail(new JobKey("datajob", "jobgroup"));
        Assert.That(triggerWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));
        Assert.That(jobWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));

        // once more
        await sched.DeleteJob(jobWithData.Key);
        await sched.ScheduleJob(jobWithData, triggerWithData);
        await sched.Shutdown();

        properties["quartz.jobStore.useProperties"] = false.ToString();
        sf = new StdSchedulerFactory(properties);
        sched = await sf.GetScheduler();

        triggerWithDataFromDb = await sched.GetTrigger(new TriggerKey("datatrigger", "triggergroup"));
        jobWithDataFromDb = await sched.GetJobDetail(new JobKey("datajob", "jobgroup"));
        Assert.That(triggerWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));
        Assert.That(jobWithDataFromDb.JobDataMap["testkey"], Is.EqualTo("testvalue"));
    }

    [Test]
    [Explicit]
    [Category("db-sqlserver")]
    [TestCaseSource(nameof(GetSerializerTypes))]
    public async Task TestSqlServerStress(string serializerType)
    {
        NameValueCollection properties = new NameValueCollection();

        properties["quartz.scheduler.instanceName"] = "TestScheduler";
        properties["quartz.scheduler.instanceId"] = "instance_one";
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
        properties["quartz.jobStore.useProperties"] = "false";
        properties["quartz.jobStore.dataSource"] = "default";
        properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
        properties["quartz.jobStore.clustered"] = true.ToString();

        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";
        await RunAdoJobStoreTest(TestConstants.DefaultSqlServerProvider, "SQLServer", serializerType, properties);

        if (!dbConnectionStrings.TryGetValue("SQLServer", out var connectionString))
        {
            throw new Exception("Unknown connection string id: " + "SQLServer");
        }
        properties["quartz.dataSource.default.connectionString"] = connectionString;
        properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;

        // First we must get a reference to a scheduler
        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler sched = await sf.GetScheduler();

        try
        {
            await sched.Clear();

            if (scheduleJobs)
            {
                ICalendar cronCalendar = new CronCalendar("0/5 * * * * ?");
                ICalendar holidayCalendar = new HolidayCalendar();

                for (int i = 0; i < 100000; ++i)
                {
                    ITrigger trigger = new SimpleTriggerImpl("calendarsTrigger", "test", SimpleTriggerImpl.RepeatIndefinitely, TimeSpan.FromSeconds(1));
                    var jd = JobBuilder.Create<NoOpJob>()
                        .WithIdentity(new JobKey("testJob", "test"))
                        .Build();
                    await sched.ScheduleJob(jd, trigger);
                }
            }
            await sched.Start();
            await Task.Delay(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await sched.Shutdown(false);
        }
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task TestGetTriggerKeysWithLike()
    {
        var sched = await CreateScheduler(null);

        await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith("foo"));
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task TestGetTriggerKeysWithEquals()
    {
        var sched = await CreateScheduler(null);

        await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("bar"));
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task TestGetJobKeysWithLike()
    {
        var sched = await CreateScheduler(null);

        await sched.GetJobKeys(GroupMatcher<JobKey>.GroupStartsWith("foo"));
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task TestGetJobKeysWithEquals()
    {
        var sched = await CreateScheduler(null);

        await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("bar"));
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task JobTypeNotFoundShouldNotBlock()
    {
        NameValueCollection properties = new NameValueCollection();
        properties.Add(StdSchedulerFactory.PropertySchedulerTypeLoadHelperType, typeof(SpecialClassLoadHelper).AssemblyQualifiedName);
        var scheduler = await CreateScheduler(properties);

        await scheduler.DeleteJobs(new[] { JobKey.Create("bad"), JobKey.Create("good") });

        await scheduler.Start();

        var manualResetEvent = new ManualResetEventSlim(false);
        scheduler.Context.Put(KeyResetEvent, manualResetEvent);

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
        await scheduler.ScheduleJobs(toSchedule, true);

        manualResetEvent.Wait(TimeSpan.FromSeconds(20));

        Assert.That(await scheduler.GetTriggerState(badTrigger.Key), Is.Not.EqualTo(TriggerState.Blocked));
    }

    private static async Task<IScheduler> CreateScheduler(NameValueCollection properties)
    {
        properties ??= new NameValueCollection();

        properties["quartz.scheduler.instanceName"] = "TestScheduler";
        properties["quartz.scheduler.instanceId"] = "instance_one";
        properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
        properties["quartz.jobStore.useProperties"] = "false";
        properties["quartz.jobStore.dataSource"] = "default";
        properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
        properties["quartz.jobStore.clustered"] = "false";
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";

        properties["quartz.dataSource.default.connectionString"] = TestConstants.SqlServerConnectionString;
        properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;

        // First we must get a reference to a scheduler
        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler sched = await sf.GetScheduler();
        return sched;
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
        properties["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz";
        properties["quartz.jobStore.useProperties"] = "false";
        properties["quartz.jobStore.dataSource"] = "default";
        properties["quartz.jobStore.tablePrefix"] = "QRTZ_";
        properties["quartz.jobStore.clustered"] = "false";
        properties["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SqlServerDelegate, Quartz";

        properties["quartz.dataSource.default.connectionString"] = TestConstants.SqlServerConnectionString;
        properties["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider;

        // First we must get a reference to a scheduler
        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler sched = await sf.GetScheduler();

        try
        {
            await sched.Clear();

            var lonelyJob = JobBuilder.Create()
                .OfType<SimpleRecoveryJob>()
                .WithIdentity(new JobKey("lonelyJob", "lonelyGroup"))
                .StoreDurably(true)
                .RequestRecovery(true)
                .Build();

            await sched.AddJob(lonelyJob, false);
            await sched.AddJob(lonelyJob, true);

            string schedId = sched.SchedulerInstanceId;

            var job = JobBuilder.Create()
                .OfType<SimpleRecoveryJob>()
                .WithIdentity(new JobKey("job_to_use", schedId))
                .Build();

            for (int i = 0; i < 100000; ++i)
            {
                IOperableTrigger trigger = new SimpleTriggerImpl("stressing_simple", SimpleTriggerImpl.RepeatIndefinitely, TimeSpan.FromSeconds(1));
                trigger.StartTimeUtc = DateTime.Now.AddMilliseconds(i);
                await sched.ScheduleJob(job, trigger);
            }

            for (int i = 0; i < 100000; ++i)
            {
                IOperableTrigger ct = new CronTriggerImpl("stressing_cron", "0/1 * * * * ?");
                ct.StartTimeUtc = DateTime.Now.AddMilliseconds(i);
                await sched.ScheduleJob(job, ct);
            }

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            await sched.Start();
            await Task.Delay(TimeSpan.FromMinutes(3));
            stopwatch.Stop();
            Console.WriteLine("Took: " + stopwatch.Elapsed);
        }
        finally
        {
            await sched.Shutdown(false);
        }
    }

    public class BadJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return default;
        }
    }

    public class GoodJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
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

    public class SpecialClassLoadHelper : ITypeLoadHelper
    {
        public void Initialize()
        {
        }

        public Type LoadType(string name)
        {
            if (!string.IsNullOrEmpty(name) && typeof(BadJob) == Type.GetType(name))
            {
                throw new TypeLoadException();
            }
            return Type.GetType(name, false);
        }
    }

    [TearDown]
    public async Task ShutdownSchedulers()
    {
        foreach (var scheduler in SchedulerRepository.Instance.LookupAll())
        {
            await scheduler.Shutdown(CancellationToken.None);
        }
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
    public virtual async ValueTask Execute(IJobExecutionContext context)
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
        data.Put(Count, count);
    }
}

[DisallowConcurrentExecution]
[PersistJobDataAfterExecution]
public class SimpleRecoveryStatefulJob : SimpleRecoveryJob
{
}