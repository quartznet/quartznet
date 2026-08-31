#nullable enable

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A job type that was renamed says so as a map, rather than as a type loader the application has to
/// write.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SimpleTypeLoaderTest"/> covers how one alias is matched and substituted. What is under
/// test here is the seam: that the map reaches the loader the container hands its schedulers, from the
/// builder and from configuration alike, that an alias naming nothing is a startup failure, and that a
/// job whose stored <c>JOB_CLASS_NAME</c> is a dead name fires anyway — without the row being rewritten.
/// </para>
/// <para>
/// The round trip runs against a real SQLite file, which is what makes the last of those a fact about
/// the ADO.NET store rather than about the loader on its own: the name is read back out of a column, by
/// the store, on the acquisition path.
/// </para>
/// </remarks>
public sealed class TypeLoaderAliasTest
{
    /// <summary>
    /// The name a deployment before this one stored, naming a type nothing is called any more.
    /// </summary>
    private const string StoredName = "Acme.Jobs.NightlyReport, Acme.Jobs";

    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-alias-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    [Test]
    public void AnAliasDeclaredOnTheBuilderReachesTheSchedulersTypeLoader()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UseTypeLoader(loader => loader.Map(StoredName, typeof(NightlyRollupJob))));

        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<ITypeLoader>().LoadType(StoredName).Should().Be<NightlyRollupJob>(
            "the map is the declarative form of the rename-aware loader, so it has to reach the one place "
            + "every stored and configured type name is resolved");
    }

    [Test]
    public void AnAliasBindsFromConfiguration()
    {
        ServiceCollection services = new();
        services.AddQuartz(Section(
            ($"Quartz:TypeLoader:Aliases:{StoredName}", typeof(NightlyRollupJob).AssemblyQualifiedNameWithoutVersion())));

        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<ITypeLoader>().LoadType(StoredName).Should().Be<NightlyRollupJob>(
            "a rename has to be able to ship in appsettings with the deployment that performs it, without "
            + "a rebuild");
    }

    [Test]
    public void AliasesDeclaredForSeveralSchedulersMakeOneTable()
    {
        ServiceCollection services = new();
        services.AddQuartz("reporting", Section(
            ($"Quartz:TypeLoader:Aliases:{StoredName}", typeof(NightlyRollupJob).AssemblyQualifiedNameWithoutVersion())));
        services.AddQuartz("billing", q => q.UseTypeLoader(
            loader => loader.Map("Acme.Jobs.Invoicing, Acme.Jobs", typeof(NightlyRollupJob))));

        using ServiceProvider container = services.BuildServiceProvider();
        ITypeLoader loader = container.GetRequiredService<ITypeLoader>();

        loader.LoadType(StoredName).Should().Be<NightlyRollupJob>();
        loader.LoadType("Acme.Jobs.Invoicing, Acme.Jobs").Should().Be<NightlyRollupJob>(
            "there is one type loader per container, so its aliases are the container's — a rename "
            + "declared through either scheduler is in force for both");
    }

    [Test]
    public void AnAliasNamingATypeThatCannotBeLoadedFailsAtStartup()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UseTypeLoader(
            loader => loader.Aliases[StoredName] = "Acme.Jobs.NightlyRollupJob, Acme.Jobs"));

        using ServiceProvider container = services.BuildServiceProvider();

        var act = () => container.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{StoredName}*")
            .WithMessage("*Acme.Jobs.NightlyRollupJob, Acme.Jobs*",
                "an alias is only consulted when a name is about to become a type, so one that resolves to "
                + "nothing would otherwise surface as a TypeLoadException naming the dead name and nothing "
                + "about the mapping meant to save it");
    }

    [Test]
    public void AnAliasNamingATypeThatCannotBeLoadedAlsoFailsWhenTheLoaderIsBuilt()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UseTypeLoader(
            loader => loader.Aliases[StoredName] = "Acme.Jobs.NightlyRollupJob, Acme.Jobs"));

        using ServiceProvider container = services.BuildServiceProvider();

        var act = () => container.GetRequiredService<ITypeLoader>();

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{StoredName}*",
            "the standalone builder's container runs no startup validation, so building the loader has to "
            + "report the same mistake rather than starting with an alias that does nothing");
    }

    [Test]
    public void ABlankAliasFailsAtStartup()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UseTypeLoader(loader => loader.Aliases[""] = "Quartz.Jobs.NoOpJob, Quartz.Jobs"));

        using ServiceProvider container = services.BuildServiceProvider();

        var act = () => container.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*blank name*",
            "an alias is matched against the start of every name the loader resolves, so a blank one "
            + "would claim all of them");
    }

    [Test]
    public void AnAliasWithNoTargetFailsAtStartup()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UseTypeLoader(loader => loader.Aliases[StoredName] = " "));

        using ServiceProvider container = services.BuildServiceProvider();

        var act = () => container.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{StoredName}*")
            .WithMessage("*blank type name*");
    }

    [Test]
    public void AnAliasWhoseTargetIsSpelledWithAPre40NameIsAccepted()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UseTypeLoader(
            loader => loader.Aliases["Acme.Threading.Pool, Acme"] = "Quartz.Simpl.DefaultThreadPool, Quartz"));

        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<IStartupValidator>().Validate();

        container.GetRequiredService<ITypeLoader>().LoadType("Acme.Threading.Pool, Acme")
            .Should().Be<Quartz.Impl.DefaultThreadPool>(
                "the target is resolved the way the loader resolves anything, so Quartz's own 3.x "
                + "fallbacks apply to it too");
    }

    [Test]
    public async Task AJobStoredUnderItsOldNameFiresAsTheTypeTheAliasNames()
    {
        const string SchedulerName = "alias-round-trip";

        // The deployment before the rename: the schema is created and the job is stored under the name
        // this build knows the type by.
        await using (ServiceProvider before = Container(SchedulerName))
        {
            IScheduler scheduler = await before.GetRequiredService<ISchedulerFactory>().GetScheduler();

            await scheduler.ScheduleJob(
                JobBuilder.Create<NightlyRollupJob>().WithIdentity("job", SchedulerName).Build(),
                TriggerBuilder.Create()
                    .WithIdentity("trigger", SchedulerName)
                    .StartNow()
                    // Repeating, so the trigger is still there to fire again on the second start.
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                    .Build());

            await scheduler.Shutdown();

            var act = () => before.GetRequiredService<ITypeLoader>().LoadType(StoredName);
            act.Should().Throw<TypeLoadException>(
                "without the alias the stored name resolves to nothing, which is what makes the run below "
                + "a test of the alias rather than of the job");
        }

        // The rename itself, standing in for the JOB_CLASS_NAME an older build wrote.
        (await Execute($"UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = '{StoredName}'")).Should().Be(1);

        await using (ServiceProvider after = Container(
            SchedulerName,
            q => q.UseTypeLoader(loader => loader.Map(StoredName, typeof(NightlyRollupJob)))))
        {
            IScheduler scheduler = await after.GetRequiredService<ISchedulerFactory>().GetScheduler();

            TaskCompletionSource fired = new(TaskCreationOptions.RunContinuationsAsynchronously);
            scheduler.Context[NightlyRollupJob.SignalKey] = fired;

            await scheduler.Start();
            await fired.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        (await Scalar("SELECT JOB_CLASS_NAME FROM QRTZ_JOB_DETAILS")).Should().Be(StoredName,
            "the loader translates the name it is given and never writes one back, so the SQL UPDATE in "
            + "the troubleshooting page stays the way an alias is retired");
    }

    private ServiceProvider Container(string schedulerName, Action<IQuartzBuilder>? configure = null)
    {
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
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
            });

            configure?.Invoke(q);
        });

        return services.BuildServiceProvider();
    }

    private static IConfiguration Section(params (string Key, string? Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(entry => entry.Key, entry => entry.Value))
            .Build()
            .GetSection("Quartz");
    }

    private async Task<int> Execute(string sql)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> Scalar(string sql)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return (string?) await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// Public with a public constructor, because the store hands the job factory nothing but the type
    /// the alias resolved <c>JOB_CLASS_NAME</c> to.
    /// </summary>
    public sealed class NightlyRollupJob : IJob
    {
        internal const string SignalKey = "fired";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            ((TaskCompletionSource) context.Scheduler.Context[SignalKey]!).TrySetResult();
            return default;
        }
    }
}
