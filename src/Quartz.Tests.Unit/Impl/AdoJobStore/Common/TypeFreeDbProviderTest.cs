#nullable enable

using System.Data.Common;
using System.Reflection;

using Microsoft.Data.Sqlite;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore.Common;

/// <summary>
/// A whole scheduler over a driver description that names no type at all, once through a
/// <see cref="DbProviderFactory"/> and once through a <see cref="DbDataSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the shape a trimmed application has to be able to run in. Naming a built-in driver resolves
/// its connection, command, parameter and exception types with <c>Type.GetType</c>, and a trimmer that
/// cannot see that call removes what it thinks is unused — issue #3341 step 7 watched a
/// <c>TrimMode=full</c> publish die in <see cref="DbProvider"/>'s constructor with "Cannot instantiate
/// type which has no empty constructor". A factory and a data source both hand over instances, so
/// nothing here needs a type name, and the description says only how parameters are spelled.
/// </para>
/// <para>
/// The types being null is the assertion. It is checked before the scheduler runs, so a description
/// that quietly grew a type would fail here rather than pass by using it.
/// </para>
/// </remarks>
public sealed class TypeFreeDbProviderTest
{
    /// <summary>
    /// Microsoft.Data.Sqlite, described without naming <c>SqliteConnection</c>, <c>SqliteCommand</c>,
    /// <c>SqliteParameter</c> or <c>SqliteException</c>.
    /// </summary>
    private static DbMetadata TypeFreeSqlite => new()
    {
        ProductName = "SQLite",
        ParameterNamePrefix = "@",
        UseParameterNamePrefixInParameterCollection = true,
        BindByName = true,
    };

    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public async Task CreateSchema()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-type-free-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();

        SqliteCommand command = connection.CreateCommand();
        command.CommandText = SqliteSchema();
        await command.ExecuteNonQueryAsync();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databaseFile);
    }

    [Test]
    public async Task AFactoryProviderRunsAScheduleWithNoTypeNamed()
    {
        DbMetadata metadata = TypeFreeSqlite;
        IDbProvider provider = new ProviderFactoryDbProvider(metadata, SqliteFactory.Instance, connectionString);

        await ScheduleFireAndReadBack(provider, nameof(AFactoryProviderRunsAScheduleWithNoTypeNamed));
    }

    [Test]
    public async Task ADataSourceProviderRunsAScheduleWithNoTypeNamed()
    {
        DbMetadata metadata = TypeFreeSqlite;
        await using SqliteDataSource dataSource = new(connectionString);
        IDbProvider provider = new DataSourceDbProvider(metadata, dataSource);

        await ScheduleFireAndReadBack(provider, nameof(ADataSourceProviderRunsAScheduleWithNoTypeNamed));
    }

    /// <summary>
    /// Neither provider may go anywhere near the reflective constructor: a provider built over a
    /// description with no types has nothing to construct, and used to inherit one that tried anyway.
    /// </summary>
    [Test]
    public void NeitherProviderIsBuiltOnTheReflectiveOne()
    {
        using SqliteDataSource dataSource = new(connectionString);

        typeof(ProviderFactoryDbProvider).Should().NotBeAssignableTo<DbProvider>();
        typeof(DataSourceDbProvider).Should().NotBeAssignableTo<DbProvider>(
            "DbProvider's constructor resolves a default constructor on the described connection and command "
            + "types, which a description that names none does not have and a trimmed application may not "
            + "have kept");
    }

    private async Task ScheduleFireAndReadBack(IDbProvider provider, string schedulerName)
    {
        provider.Metadata.ConnectionType.Should().BeNull();
        provider.Metadata.CommandType.Should().BeNull();
        provider.Metadata.ParameterType.Should().BeNull();
        provider.Metadata.ExceptionType.Should().BeNull();

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = schedulerName;
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseConnectionProvider(_ => provider);
                store.UseDriverDelegate<SQLiteDelegate>();
            });
        });

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        TaskCompletionSource fired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Context[SignallingJob.SignalKey] = fired;

        JobKey jobKey = new("job", schedulerName);
        TriggerKey triggerKey = new("trigger", schedulerName);

        await scheduler.ScheduleJob(
            JobBuilder.Create<SignallingJob>().WithIdentity(jobKey).UsingJobData("blob", new string('x', 512)).Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartNow()
                // Repeating, so that reading it back afterwards reads a trigger rather than finding the
                // row a completed one-shot trigger takes with it.
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .Build());

        await scheduler.Start();

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(30));

        IJobDetail? storedJob = await scheduler.GetJobDetail(jobKey);
        storedJob.Should().NotBeNull();
        storedJob!.JobDataMap.GetString("blob").Should().Be(new string('x', 512),
            "the job data map is written as a blob parameter, which is the one parameter type a driver "
            + "description would otherwise have to name a type to bind");

        ITrigger? storedTrigger = await scheduler.GetTrigger(triggerKey);
        storedTrigger.Should().NotBeNull();
        storedTrigger!.JobKey.Should().Be(jobKey);

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    private static string SqliteSchema()
    {
        using Stream stream = typeof(TypeFreeDbProviderTest).Assembly.GetManifestResourceStream("tables_sqlite.sql")
            ?? throw new InvalidOperationException("The SQLite schema is embedded by Quartz.Tests.Unit.csproj.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Microsoft.Data.Sqlite ships no <see cref="DbDataSource"/>, so this is the smallest one that
    /// hands out its connections.
    /// </summary>
    private sealed class SqliteDataSource : DbDataSource
    {
        public SqliteDataSource(string connectionString) => ConnectionString = connectionString;

        public override string ConnectionString { get; }

        protected override DbConnection CreateDbConnection() => new SqliteConnection(ConnectionString);
    }

    public sealed class SignallingJob : IJob
    {
        internal const string SignalKey = "fired";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            ((TaskCompletionSource) context.Scheduler.Context[SignalKey]!).TrySetResult();
            return default;
        }
    }
}
