using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

using Quartz.Benchmark.Competitors.Database;

using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;

namespace Quartz.Benchmark.Competitors.Engines;

/// <summary>
/// The same three engines, pointed at a real database.
/// </summary>
/// <remarks>
/// Each library is given the persistence its own documentation teaches: Quartz its ADO store, TickerQ
/// its EF Core operational store, Hangfire its PostgreSQL storage. Each ends up in its own schema of
/// the one database at its own defaults — Quartz's shipped DDL is unqualified so it is in
/// <c>public</c>, TickerQ's model defaults to <c>ticker</c>, Hangfire's storage to <c>hangfire</c>.
/// </remarks>
internal static class PersistentEngines
{
    /// <summary>
    /// Hangfire's poll interval on the database rows, as #3802's S2 settings say.
    /// </summary>
    private static readonly TimeSpan hangfirePoll = TimeSpan.FromMilliseconds(100);

    public static IEngine Quartz(QuartzProfile profile, string connectionString)
    {
        return new QuartzEngine(
            profile,
            profile == QuartzProfile.Defaults ? "S2Defaults" : "S2Tuned",
            quartz => quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);
                store.UseSystemTextJsonSerializer();
            }));
    }

    public static IEngine TickerQ(string connectionString, TimeSpan minPollingInterval)
    {
        return new TickerQEngine(
            "TickerQ",
            minPollingInterval,
            configureStore: options => options.AddOperationalStore(ef =>
                ef.UseTickerQDbContext<TickerQDbContext>(builder => builder.UseNpgsql(connectionString))));
    }

    public static IEngine Hangfire(string connectionString)
    {
        return new HangfireEngine("Hangfire", () => PostgresStorage(connectionString), hangfirePoll);
    }

    /// <summary>
    /// Creates TickerQ's three tables in its own schema.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called once per case, after the schema has been dropped, rather than from the engine — the
    /// engine is rebuilt every iteration and a second attempt to create the tables would fail.
    /// </para>
    /// <para>
    /// <c>IRelationalDatabaseCreator.CreateTables</c> rather than <c>EnsureCreated</c>:
    /// <c>EnsureCreated</c> creates the tables only when it has to create the <em>database</em>, and
    /// this database already exists because Quartz's schema is in it. It returned false and created
    /// nothing, and the first TickerQ case failed on <c>relation "ticker.CronTickers" does not exist</c>.
    /// A migration would be the deployment story rather than the fire path, and TickerQ's are generated
    /// into the application's assembly rather than shipped.
    /// </para>
    /// </remarks>
    public static async ValueTask ProvisionTickerQ(string connectionString, CancellationToken cancellationToken = default)
    {
        DbContextOptionsBuilder<TickerQDbContext> options = new();
        options.UseNpgsql(connectionString);

        await using TickerQDbContext context = new(options.Options);
        await context.Database.GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static JobStorage PostgresStorage(string connectionString)
    {
        PostgreSqlStorageOptions options = new()
        {
            QueuePollInterval = hangfirePoll,
            SchemaName = PostgresFixture.HangfireSchema,
            PrepareSchemaIfNecessary = true,
        };

        return new PostgreSqlStorage(new NpgsqlConnectionFactory(connectionString, options), options);
    }
}
