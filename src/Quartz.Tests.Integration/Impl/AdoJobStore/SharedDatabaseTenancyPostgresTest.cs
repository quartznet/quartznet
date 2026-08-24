using System.Data.Common;

using Npgsql;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Two tenants sharing the assembly-wide PostgreSQL database, which is where a real deployment of this
/// arrangement would put them.
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class SharedDatabaseTenancyPostgresTest : SharedDatabaseTenancyTestBase
{
    protected override void UseDatabase(IPersistentStoreBuilder store)
    {
        store.UsePostgres(TestConstants.PostgresConnectionString);
    }

    protected override async Task CleanUpDatabase()
    {
        // The tenants share every table with the rest of this assembly's fixtures, so the cleanup names
        // the two SCHED_NAMEs rather than truncating anything.
        await using DbConnection connection = new NpgsqlConnection(TestConstants.PostgresConnectionString);
        await connection.OpenAsync();

        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME IN (@acme, @initech);"
            + "DELETE FROM QRTZ_SIMPLE_TRIGGERS WHERE SCHED_NAME IN (@acme, @initech);"
            + "DELETE FROM QRTZ_TRIGGERS WHERE SCHED_NAME IN (@acme, @initech);"
            + "DELETE FROM QRTZ_JOB_DETAILS WHERE SCHED_NAME IN (@acme, @initech);"
            + "DELETE FROM QRTZ_PAUSED_TRIGGER_GRPS WHERE SCHED_NAME IN (@acme, @initech);"
            + "DELETE FROM QRTZ_SCHEDULER_STATE WHERE SCHED_NAME IN (@acme, @initech);";
        AddParameter(command, "acme", Acme);
        AddParameter(command, "initech", Initech);

        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
