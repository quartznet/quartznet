using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Quartz.Tests.ClusterNode;

/// <summary>
/// Helper methods for initializing and querying the cluster test database tables.
/// </summary>
public static class ClusterTestHelper
{
    /// <summary>
    /// Initializes the test database tables (ClusterTestExecutions and ClusterTestViolations).
    /// Creates tables if they don't exist and truncates existing data.
    /// </summary>
    public static async Task InitializeDatabase(string connectionString, IDatabaseProvider provider, ILoggerFactory? loggerFactory = null)
    {
        var logger = loggerFactory?.CreateLogger("ClusterTestHelper");

        try
        {
            await using var connection = provider.CreateConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            logger?.LogDebug("Creating ClusterTestExecutions table if not exists");

            // Create executions table
            var createExecutionsCmd = connection.CreateCommand();
            createExecutionsCmd.CommandText = provider.GetCreateExecutionsTableSql();
            await createExecutionsCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

            logger?.LogDebug("Creating ClusterTestViolations table if not exists");

            // Create a violations table
            await using (var createViolationsCmd = connection.CreateCommand())
            {
                createViolationsCmd.CommandText = provider.GetCreateViolationsTableSql();
                await createViolationsCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            logger?.LogInformation("Clearing previous test data");

            // Clear any previous test data
            await using (var truncateExecutionsCmd = connection.CreateCommand())
            {
                truncateExecutionsCmd.CommandText = provider.GetTruncateExecutionsTableSql();
                await truncateExecutionsCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await using (var clearViolationsCmd = connection.CreateCommand())
            {
                clearViolationsCmd.CommandText = provider.GetClearViolationsTableSql();
                await clearViolationsCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            logger?.LogInformation("Database initialization complete");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error initializing database");
            throw;
        }
    }

    /// <summary>
    /// Retrieves detailed statistics from the test execution for reporting.
    /// </summary>
    public static async Task<string> GetFinalStatistics(string connectionString, IDatabaseProvider provider, ILoggerFactory? loggerFactory = null)
    {
        var logger = loggerFactory?.CreateLogger("ClusterTestHelper");

        try
        {
            await using var connection = provider.CreateConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            int totalExecutions, totalViolationIncidents, nodeCount;
            await using (var statsCmd = connection.CreateCommand())
            {
                statsCmd.CommandText = @"SELECT
                    (SELECT COUNT(*) FROM ClusterTestExecutions) as TotalExecutions,
                    (SELECT COUNT(*) FROM ClusterTestViolations) as TotalViolations,
                    (SELECT COUNT(DISTINCT NodeId) FROM ClusterTestExecutions) as NodeCount";

                await using var reader = await statsCmd.ExecuteReaderAsync().ConfigureAwait(false);
                if (!await reader.ReadAsync().ConfigureAwait(false))
                {
                    return "No statistics data available.\n";
                }

                totalExecutions = Convert.ToInt32(reader.GetValue(0));
                totalViolationIncidents = Convert.ToInt32(reader.GetValue(1));
                nodeCount = Convert.ToInt32(reader.GetValue(2));
            }

            logger?.LogInformation("Retrieving final statistics: {TotalExecutions} executions, {ViolationPeriods} violation periods, {NodeCount} nodes",
                totalExecutions, totalViolationIncidents, nodeCount);

            var sb = new StringBuilder();
            sb.Append('\n').Append('=', 80).Append('\n');
            sb.Append("FINAL STATISTICS (DATABASE STATE)\n");
            sb.Append('=', 80).Append('\n');
            sb.Append($"Total Executions: {totalExecutions}\n");
            sb.Append($"Concurrent Execution Violation Periods: {totalViolationIncidents}\n");
            sb.Append($"Active Nodes: {nodeCount}\n");

            if (totalViolationIncidents > 0)
            {
                sb.Append("\nViolation Period Details:\n");
                await using (var violationCmd = connection.CreateCommand())
                {
                    violationCmd.CommandText = @"SELECT NodeId, DetectedAt, ConcurrentCount, ExecutionId, EndedAt
                      FROM ClusterTestViolations
                      ORDER BY DetectedAt";

                    await using var violationReader = await violationCmd.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await violationReader.ReadAsync().ConfigureAwait(false))
                    {
                        var nodeId = violationReader.GetString(0);
                        var detectedAt = violationReader.GetDateTime(1);
                        var concurrentCount = Convert.ToInt32(violationReader.GetValue(2));
                        var executionId = violationReader.GetString(3);
                        var endedAt = await violationReader.IsDBNullAsync(4).ConfigureAwait(false) ? (DateTime?)null : violationReader.GetDateTime(4);
                        var duration = endedAt.HasValue ? $"{(endedAt.Value - detectedAt).TotalMilliseconds:F0}ms" : "ongoing";
                        sb.Append($"  [{detectedAt:HH:mm:ss.fff}] Node: {nodeId} | Count: {concurrentCount} | Duration: {duration} | Execution: {executionId}\n");
                    }
                }
            }

            // Show node execution breakdown
            sb.Append("\nNode Execution Breakdown:\n");
            await using (var nodeStatsCmd = connection.CreateCommand())
            {
                nodeStatsCmd.CommandText = @"SELECT NodeId, COUNT(*) as ExecutionCount, MIN(StartTime) as FirstExecution, MAX(StartTime) as LastExecution
                  FROM ClusterTestExecutions
                  GROUP BY NodeId
                  ORDER BY NodeId";

                await using var nodeReader = await nodeStatsCmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await nodeReader.ReadAsync().ConfigureAwait(false))
                {
                    var nodeId = nodeReader.GetString(0);
                    var execCount = Convert.ToInt32(nodeReader.GetValue(1));
                    var firstExec = nodeReader.GetDateTime(2);
                    var lastExec = nodeReader.GetDateTime(3);
                    sb.Append($"  {nodeId}: {execCount} executions (first: {firstExec:HH:mm:ss.fff}, last: {lastExec:HH:mm:ss.fff})\n");
                }
            }

            sb.Append('=', 80).Append('\n');

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error retrieving final statistics");
            return $"Error retrieving statistics: {ex.Message}\n";
        }
    }

    /// <summary>
    /// Clears all Quartz.NET tables for a fresh cluster test run.
    /// This removes all jobs, triggers, locks, and fired triggers.
    /// </summary>
    public static async Task CleanupQuartzTables(string connectionString, IDatabaseProvider provider, ILoggerFactory? loggerFactory = null)
    {
        var logger = loggerFactory?.CreateLogger("ClusterTestHelper");

        try
        {
            await using var connection = provider.CreateConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            logger?.LogInformation("Cleaning Quartz.NET tables...");

            await using (var cleanupCmd = connection.CreateCommand())
            {
                cleanupCmd.CommandText = provider.GetCleanupQuartzTablesSql();
                await cleanupCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            logger?.LogInformation("Quartz.NET tables cleaned successfully");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not clean Quartz.NET tables (this is OK if first run)");
        }
    }
}
