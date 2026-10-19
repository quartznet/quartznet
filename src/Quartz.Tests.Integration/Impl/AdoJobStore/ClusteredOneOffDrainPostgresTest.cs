using System.Globalization;

using Npgsql;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The clustered one-off drain (<see cref="ClusteredOneOffDrainTestBase" />) on PostgreSQL.
/// </summary>
/// <remarks>
/// Run it against a PostgreSQL at its shipped durability — the Testcontainers one runs with <c>fsync</c>
/// off, where a commit costs nothing and a batch has nothing to save:
/// <code>
/// $env:QUARTZ_TEST_DATABASE = 'basic'   # start no container
/// $env:PG_CONNECTION_STRING = 'Host=localhost;Port=55432;Database=quartznet;Username=quartznet;Password=quartznet'
/// dotnet test --project src/Quartz.Tests.Integration/Quartz.Tests.Integration.csproj `
///   --filter 'FullyQualifiedName~ClusteredOneOffDrainPostgresTest' `
///   --output Detailed
/// </code>
/// </remarks>
[Category("db-postgres")]
[Category("LongRunning")]
[NonParallelizable]
public sealed class ClusteredOneOffDrainPostgresTest : ClusteredOneOffDrainTestBase
{
    public ClusteredOneOffDrainPostgresTest() : base(ClusteredTestDatabase.Postgres)
    {
    }

    protected override string Engine => "PostgreSQL";

    /// <inheritdoc />
    /// <remarks>
    /// Testcontainers starts PostgreSQL with <c>fsync</c> and <c>synchronous_commit</c> off, so a commit
    /// there costs next to nothing and the gate would be weighing a batch's lock against savings that
    /// do not exist. Run it against a server at its shipped durability for a verdict that means
    /// anything: set <c>QUARTZ_TEST_DATABASE=basic</c>, so no container starts, and
    /// <c>PG_CONNECTION_STRING</c> to a database created from <c>database/tables/tables_postgres.sql</c>.
    /// </remarks>
    protected override async Task<string> Durability()
    {
        await using NpgsqlConnection connection = new(Database.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand fsync = new("SHOW fsync", connection);
        string fsyncValue = (string) await fsync.ExecuteScalarAsync();
        await using NpgsqlCommand synchronousCommit = new("SHOW synchronous_commit", connection);
        string synchronousCommitValue = (string) await synchronousCommit.ExecuteScalarAsync();

        return $"fsync = {fsyncValue}, synchronous_commit = {synchronousCommitValue}";
    }

    /// <summary>
    /// <c>xact_commit</c> once every backend has published it.
    /// </summary>
    /// <remarks>
    /// PostgreSQL 15 publishes a backend's commits when it goes idle, at most once a second, and holds them
    /// for ten seconds when it goes idle within a second of its last publication (#3861). A backend
    /// publishes as it exits, so the pools are cleared and the read waits for the backends to go.
    /// </remarks>
    protected override async Task<long?> PublishedCommits()
    {
        await using NpgsqlConnection connection = new(Database.ConnectionString);
        await connection.OpenAsync();

        List<int> backends = [];
        await using (NpgsqlCommand command = new(
                         "SELECT pid FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid() AND backend_type = 'client backend'",
                         connection))
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                backends.Add(reader.GetInt32(0));
            }
        }

        NpgsqlConnection.ClearAllPools();

        if (backends.Count > 0)
        {
            string remaining = "SELECT count(*) FROM pg_stat_activity WHERE pid IN ("
                               + string.Join(",", backends.Select(pid => pid.ToString(CultureInfo.InvariantCulture))) + ")";
            DateTimeOffset giveUp = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < giveUp)
            {
                await using NpgsqlCommand count = new(remaining, connection);
                if (Convert.ToInt64(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
                {
                    break;
                }

                await Task.Delay(20);
            }
        }

        await using NpgsqlCommand commits = new("SELECT xact_commit FROM pg_stat_database WHERE datname = current_database()", connection);
        return Convert.ToInt64(await commits.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
