using System.Globalization;

using Microsoft.Data.SqlClient;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The clustered one-off drain (<see cref="ClusteredOneOffDrainTestBase" />) on SQL Server with
/// <c>READ_COMMITTED_SNAPSHOT</c> on, where a round without the lock claims in one transaction and fires in
/// a second (#4117). The arms with <c>LockFreeFiringRounds</c> off are 4.5's rounds on the same database.
/// </summary>
/// <remarks>
/// Against the Testcontainers SQL Server, which keeps full durability:
/// <code>
/// $env:QUARTZ_TEST_DATABASE = 'sqlserver'
/// dotnet test --project src/Quartz.Tests.Integration/Quartz.Tests.Integration.csproj `
///   --filter 'FullyQualifiedName~ClusteredOneOffDrainSqlServerTest' `
///   --output Detailed
/// </code>
/// </remarks>
[Category("db-sqlserver")]
[Category("LongRunning")]
[NonParallelizable]
public sealed class ClusteredOneOffDrainSqlServerTest : ClusteredOneOffDrainTestBase
{
    public ClusteredOneOffDrainSqlServerTest() : base(ClusteredTestDatabase.SqlServerSnapshot)
    {
    }

    protected override string Engine => "SQL Server";

    /// <summary>
    /// The database's <c>Transactions/sec</c> counter, which counts every transaction started, as
    /// PostgreSQL's <c>xact_commit</c> counts every one committed.
    /// </summary>
    protected override string CommitsColumn => "Transactions/firing";

    protected override async Task<string> Durability()
    {
        await using SqlConnection connection = new(Database.ConnectionString);
        await connection.OpenAsync();

        await using SqlCommand command = new(
            "SELECT delayed_durability_desc, is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()",
            connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return $"DELAYED_DURABILITY = {reader.GetString(0)}, READ_COMMITTED_SNAPSHOT = {(reader.GetBoolean(1) ? "ON" : "OFF")}";
    }

    /// <summary>
    /// The database's cumulative <c>Transactions/sec</c> counter, or <see langword="null" /> for a login
    /// that may not read the server's counters.
    /// </summary>
    /// <remarks>
    /// The counter is the server's own and counts as a transaction starts, so nothing has to be published
    /// first, as PostgreSQL's commit count does.
    /// </remarks>
    protected override async Task<long?> PublishedCommits()
    {
        await using SqlConnection connection = new(Database.ConnectionString);
        await connection.OpenAsync();

        await using SqlCommand command = new(
            "SELECT cntr_value FROM sys.dm_os_performance_counters "
            + "WHERE counter_name = 'Transactions/sec' AND object_name LIKE '%:Databases%' AND instance_name = DB_NAME()",
            connection);

        try
        {
            object value = await command.ExecuteScalarAsync();
            return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (SqlException)
        {
            return null;
        }
    }
}
