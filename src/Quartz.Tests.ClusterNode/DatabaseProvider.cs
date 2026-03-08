using System;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace Quartz.Tests.ClusterNode;

/// <summary>
/// Abstraction for database-specific operations.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>
    /// Creates a database connection for the provider.
    /// </summary>
    DbConnection CreateConnection(string connectionString);

    /// <summary>
    /// Gets the parameter prefix for the provider (@ for SQL Server, : for PostgreSQL).
    /// </summary>
    string GetParameterPrefix();

    /// <summary>
    /// Gets the SQL for creating the ClusterTestExecutions table.
    /// </summary>
    string GetCreateExecutionsTableSql();

    /// <summary>
    /// Gets the SQL for creating the ClusterTestViolations table.
    /// </summary>
    string GetCreateViolationsTableSql();

    /// <summary>
    /// Gets the SQL for truncating the ClusterTestExecutions table.
    /// </summary>
    string GetTruncateExecutionsTableSql();

    /// <summary>
    /// Gets the SQL for clearing the ClusterTestViolations table.
    /// </summary>
    string GetClearViolationsTableSql();

    /// <summary>
    /// Gets the SQL for cleaning up all Quartz.NET tables.
    /// </summary>
    string GetCleanupQuartzTablesSql();

    /// <summary>
    /// Gets the SQL for counting violation records.
    /// </summary>
    string GetViolationCountSql() => "SELECT COUNT(*) FROM ClusterTestViolations";
}

/// <summary>
/// SQL Server implementation of database provider.
/// </summary>
public class SqlServerProvider : IDatabaseProvider
{
    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    public string GetParameterPrefix() => "@";

    public string GetCreateExecutionsTableSql() =>
        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ClusterTestExecutions')
          BEGIN
              CREATE TABLE ClusterTestExecutions (
                  ExecutionId NVARCHAR(50) PRIMARY KEY,
                  NodeId NVARCHAR(200) NOT NULL,
                  StartTime DATETIME2 NOT NULL,
                  EndTime DATETIME2 NULL,
                  INDEX IX_EndTime (EndTime)
              )
          END";

    public string GetCreateViolationsTableSql() =>
        @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ClusterTestViolations')
          BEGIN
              CREATE TABLE ClusterTestViolations (
                  Id INT IDENTITY(1,1) PRIMARY KEY,
                  ExecutionId NVARCHAR(50) NOT NULL,
                  NodeId NVARCHAR(200) NOT NULL,
                  DetectedAt DATETIME2 NOT NULL,
                  ConcurrentCount INT NOT NULL,
                  EndedAt DATETIME2 NULL,
                  INDEX IX_EndedAt (EndedAt)
              )
          END
          ELSE IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('ClusterTestViolations') AND name = 'EndedAt')
          BEGIN
              ALTER TABLE ClusterTestViolations ADD EndedAt DATETIME2 NULL
              CREATE INDEX IX_EndedAt ON ClusterTestViolations(EndedAt)
          END";

    public string GetTruncateExecutionsTableSql() => "TRUNCATE TABLE ClusterTestExecutions";

    public string GetClearViolationsTableSql() => "DELETE FROM ClusterTestViolations";

    public string GetCleanupQuartzTablesSql() =>
        @"DELETE FROM QRTZ_FIRED_TRIGGERS;
          DELETE FROM QRTZ_PAUSED_TRIGGER_GRPS;
          DELETE FROM QRTZ_SCHEDULER_STATE;
          DELETE FROM QRTZ_LOCKS;
          DELETE FROM QRTZ_SIMPLE_TRIGGERS;
          DELETE FROM QRTZ_SIMPROP_TRIGGERS;
          DELETE FROM QRTZ_CRON_TRIGGERS;
          DELETE FROM QRTZ_BLOB_TRIGGERS;
          DELETE FROM QRTZ_TRIGGERS;
          DELETE FROM QRTZ_JOB_DETAILS;
          DELETE FROM QRTZ_CALENDARS;";
}

/// <summary>
/// PostgreSQL implementation of database provider.
/// </summary>
public class NpgsqlProvider : IDatabaseProvider
{
    public DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);

    public string GetParameterPrefix() => ":";

    public string GetCreateExecutionsTableSql() =>
        @"CREATE TABLE IF NOT EXISTS ClusterTestExecutions (
              ExecutionId VARCHAR(50) PRIMARY KEY,
              NodeId VARCHAR(200) NOT NULL,
              StartTime TIMESTAMP NOT NULL,
              EndTime TIMESTAMP NULL
          );
          CREATE INDEX IF NOT EXISTS IX_ClusterTestExecutions_EndTime ON ClusterTestExecutions(EndTime);";

    public string GetCreateViolationsTableSql() =>
        @"CREATE TABLE IF NOT EXISTS ClusterTestViolations (
              Id SERIAL PRIMARY KEY,
              ExecutionId VARCHAR(50) NOT NULL,
              NodeId VARCHAR(200) NOT NULL,
              DetectedAt TIMESTAMP NOT NULL,
              ConcurrentCount INT NOT NULL,
              EndedAt TIMESTAMP NULL
          );
          CREATE INDEX IF NOT EXISTS IX_ClusterTestViolations_EndedAt ON ClusterTestViolations(EndedAt);

          -- Add EndedAt column if it doesn't exist (for backward compatibility)
          DO $$
          BEGIN
              IF NOT EXISTS (
                  SELECT 1 FROM information_schema.columns
                  WHERE table_name = 'clustertestviolations' AND column_name = 'endedat'
              ) THEN
                  ALTER TABLE ClusterTestViolations ADD COLUMN EndedAt TIMESTAMP NULL;
                  CREATE INDEX IF NOT EXISTS IX_ClusterTestViolations_EndedAt ON ClusterTestViolations(EndedAt);
              END IF;
          END $$;";

    public string GetTruncateExecutionsTableSql() => "TRUNCATE TABLE ClusterTestExecutions";

    public string GetClearViolationsTableSql() => "DELETE FROM ClusterTestViolations";

    public string GetCleanupQuartzTablesSql() =>
        @"DELETE FROM qrtz_fired_triggers;
          DELETE FROM qrtz_paused_trigger_grps;
          DELETE FROM qrtz_scheduler_state;
          DELETE FROM qrtz_locks;
          DELETE FROM qrtz_simple_triggers;
          DELETE FROM qrtz_simprop_triggers;
          DELETE FROM qrtz_cron_triggers;
          DELETE FROM qrtz_blob_triggers;
          DELETE FROM qrtz_triggers;
          DELETE FROM qrtz_job_details;
          DELETE FROM qrtz_calendars;";
}

/// <summary>
/// MySQL implementation of database provider.
/// </summary>
public class MySqlProvider : IDatabaseProvider
{
    public DbConnection CreateConnection(string connectionString) => new MySqlConnection(connectionString);

    public string GetParameterPrefix() => "@";

    public string GetCreateExecutionsTableSql() =>
        @"CREATE TABLE IF NOT EXISTS ClusterTestExecutions (
              ExecutionId VARCHAR(50) PRIMARY KEY,
              NodeId VARCHAR(200) NOT NULL,
              StartTime DATETIME(3) NOT NULL,
              EndTime DATETIME(3) NULL,
              INDEX IX_ClusterTestExecutions_EndTime (EndTime)
          )";

    public string GetCreateViolationsTableSql() =>
        @"CREATE TABLE IF NOT EXISTS ClusterTestViolations (
              Id INT AUTO_INCREMENT PRIMARY KEY,
              ExecutionId VARCHAR(50) NOT NULL,
              NodeId VARCHAR(200) NOT NULL,
              DetectedAt DATETIME(3) NOT NULL,
              ConcurrentCount INT NOT NULL,
              EndedAt DATETIME(3) NULL,
              INDEX IX_ClusterTestViolations_EndedAt (EndedAt)
          )";

    public string GetTruncateExecutionsTableSql() => "TRUNCATE TABLE ClusterTestExecutions";

    public string GetClearViolationsTableSql() => "DELETE FROM ClusterTestViolations";

    public string GetCleanupQuartzTablesSql() =>
        @"DELETE FROM qrtz_fired_triggers;
          DELETE FROM qrtz_paused_trigger_grps;
          DELETE FROM qrtz_scheduler_state;
          DELETE FROM qrtz_locks;
          DELETE FROM qrtz_simple_triggers;
          DELETE FROM qrtz_simprop_triggers;
          DELETE FROM qrtz_cron_triggers;
          DELETE FROM qrtz_blob_triggers;
          DELETE FROM qrtz_triggers;
          DELETE FROM qrtz_job_details;
          DELETE FROM qrtz_calendars;";
}

/// <summary>
/// Factory for creating database provider instances.
/// </summary>
public static class DatabaseProviderFactory
{
    /// <summary>
    /// Gets a database provider by name.
    /// </summary>
    /// <param name="providerName">The provider name (SqlServer, Npgsql, or MySql)</param>
    /// <returns>The database provider instance</returns>
    /// <exception cref="ArgumentException">Thrown when the provider name is not recognized</exception>
    public static IDatabaseProvider GetProvider(string providerName)
    {
        return providerName switch
        {
            "SqlServer" => new SqlServerProvider(),
            "PostgreSQL" => new NpgsqlProvider(),
            "MySQL" => new MySqlProvider(),
            _ => throw new ArgumentException($"Unknown database provider: {providerName}", nameof(providerName))
        };
    }
}
