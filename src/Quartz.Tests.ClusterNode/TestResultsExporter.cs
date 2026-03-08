using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Quartz.Tests.ClusterNode;

/// <summary>
/// Exports test results to JSON format for consumption by integration tests.
/// </summary>
public static class TestResultsExporter
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Exports test results from the database to a JSON file.
    /// </summary>
    public static async Task ExportTestResultsAsync(
        string connectionString,
        IDatabaseProvider provider,
        string nodeId,
        string outputPath,
        ILoggerFactory? loggerFactory = null)
    {
        var logger = loggerFactory?.CreateLogger("TestResultsExporter");

        try
        {
            logger?.LogDebug("Exporting test results for node {NodeId} to {OutputPath}", nodeId, outputPath);

            await using var connection = provider.CreateConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            // Query statistics
            await using var statsCmd = connection.CreateCommand();
            statsCmd.CommandText = @"SELECT
                    (SELECT COUNT(*) FROM ClusterTestExecutions) as TotalExecutions,
                    (SELECT COUNT(*) FROM ClusterTestViolations) as TotalViolations,
                    (SELECT COUNT(DISTINCT NodeId) FROM ClusterTestExecutions) as NodeCount";

            TestStatistics statistics;
            await using (var reader = await statsCmd.ExecuteReaderAsync().ConfigureAwait(false))
            {
                if (!await reader.ReadAsync().ConfigureAwait(false))
                {
                    throw new InvalidOperationException("No statistics data found in database");
                }
                statistics = new TestStatistics(
                    Convert.ToInt32(reader.GetValue(0)),
                    Convert.ToInt32(reader.GetValue(1)),
                    Convert.ToInt32(reader.GetValue(2))
                );
            }

            // Query all executions
            await using var executionsCmd = connection.CreateCommand();
            executionsCmd.CommandText = @"SELECT NodeId, ExecutionId, StartTime, EndTime
                  FROM ClusterTestExecutions
                  ORDER BY StartTime";

            var executions = new List<ExecutionRecord>();
            await using (var reader = await executionsCmd.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var endTime = await reader.IsDBNullAsync(3).ConfigureAwait(false) ? null : (DateTime?) reader.GetDateTime(3);
                    executions.Add(new ExecutionRecord(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetDateTime(2),
                        endTime
                    ));
                }
            }

            // Query all violations
            var violationsCmd = connection.CreateCommand();
            violationsCmd.CommandText = @"SELECT NodeId, DetectedAt, ConcurrentCount, ExecutionId, EndedAt
                  FROM ClusterTestViolations
                  ORDER BY DetectedAt";

            var violations = new List<ViolationRecord>();
            await using (var reader = await violationsCmd.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var endedAt = await reader.IsDBNullAsync(4).ConfigureAwait(false) ? null : (DateTime?) reader.GetDateTime(4);
                    violations.Add(new ViolationRecord(
                        reader.GetString(0),
                        reader.GetDateTime(1),
                        Convert.ToInt32(reader.GetValue(2)),
                        reader.GetString(3),
                        endedAt
                    ));
                }
            }

            // Query node execution breakdown
            var nodeBreakdownCmd = connection.CreateCommand();
            nodeBreakdownCmd.CommandText = @"SELECT NodeId, COUNT(*) as ExecutionCount, MIN(StartTime) as FirstExecution, MAX(StartTime) as LastExecution
                  FROM ClusterTestExecutions
                  GROUP BY NodeId
                  ORDER BY NodeId";

            var nodeBreakdown = new List<NodeExecutionBreakdown>();
            await using (var reader = await nodeBreakdownCmd.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    nodeBreakdown.Add(new NodeExecutionBreakdown(
                        reader.GetString(0),
                        Convert.ToInt32(reader.GetValue(1)),
                        reader.GetDateTime(2),
                        reader.GetDateTime(3)
                    ));
                }
            }

            // Build results object
            var results = new NodeTestResults(
                nodeId,
                Environment.ProcessId,
                DateTime.UtcNow, // StartTime - approximate since we don't track it
                DateTime.UtcNow,
                statistics,
                executions,
                violations,
                nodeBreakdown
            );

            // Ensure directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to temp file, then move (atomic)
            var tempPath = outputPath + ".tmp";
            await using (var fileStream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(fileStream, results, jsonOptions).ConfigureAwait(false);
            }

            File.Move(tempPath, outputPath, overwrite: true);

            logger?.LogInformation("Test results exported successfully to {OutputPath}", outputPath);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to export test results");
            throw;
        }
    }
}

/// <summary>
/// Test results for a single node.
/// </summary>
public record NodeTestResults(
    string NodeId,
    int ProcessId,
    DateTime StartTime,
    DateTime EndTime,
    TestStatistics Statistics,
    List<ExecutionRecord> Executions,
    List<ViolationRecord> Violations,
    List<NodeExecutionBreakdown> NodeBreakdown
);

/// <summary>
/// Overall test statistics.
/// </summary>
public record TestStatistics(
    int TotalExecutions,
    int TotalViolationPeriods,
    int NodeCount
);

/// <summary>
/// Record of a single job execution.
/// </summary>
public record ExecutionRecord(
    string NodeId,
    string ExecutionId,
    DateTime StartTime,
    DateTime? EndTime
);

/// <summary>
/// Record of a concurrent execution violation.
/// </summary>
public record ViolationRecord(
    string NodeId,
    DateTime DetectedAt,
    int ConcurrentCount,
    string ExecutionId,
    DateTime? EndedAt
);

/// <summary>
/// Execution breakdown by node.
/// </summary>
public record NodeExecutionBreakdown(
    string NodeId,
    int ExecutionCount,
    DateTime FirstExecution,
    DateTime LastExecution
);