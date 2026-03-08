#if NETCORE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Quartz.Tests.Integration;

/// <summary>
/// Aggregates test results from multiple cluster nodes for verification.
/// </summary>
public static class TestResultsAggregator
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Reads test results from a JSON file exported by a cluster node.
    /// </summary>
    public static NodeTestResults ReadNodeResults(string filePath)
    {
        NodeTestResults results;
        try
        {
            var json = File.ReadAllText(filePath);
            results = System.Text.Json.JsonSerializer.Deserialize<NodeTestResults>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read node results from {filePath}: {ex.Message}", ex);
        }

        if (results == null)
        {
            throw new InvalidOperationException($"Failed to deserialize results from {filePath}");
        }

        return results;
    }

    /// <summary>
    /// Aggregates results from multiple cluster nodes.
    /// </summary>
    public static AggregatedTestResults AggregateResults(IEnumerable<NodeTestResults> nodeResults)
    {
        var resultsList = nodeResults.ToList();

        if (resultsList.Count == 0)
        {
            throw new ArgumentException("No node results provided", nameof(nodeResults));
        }

        // Take statistics from any node (they should all see the same database state)
        var sampleStats = resultsList[0].Statistics;

        // Collect all violations (should be same across nodes, but collect from first)
        var allViolations = resultsList[0].Violations;

        // Build node breakdown dictionary
        var nodeBreakdown = new Dictionary<string, NodeExecutionBreakdown>();
        foreach (var breakdown in resultsList[0].NodeBreakdown)
        {
            nodeBreakdown[breakdown.NodeId] = breakdown;
        }

        return new AggregatedTestResults(
            sampleStats.TotalViolationPeriods,
            sampleStats.TotalExecutions,
            sampleStats.NodeCount,
            allViolations,
            nodeBreakdown
        );
    }

    /// <summary>
    /// Formats aggregated results for display in test output.
    /// </summary>
    public static string FormatStatistics(AggregatedTestResults results)
    {
        var separator = new string('=', 80);
        var sb = new StringBuilder();
        sb.AppendLine().AppendLine(separator);
        sb.AppendLine("FINAL STATISTICS (AGGREGATED FROM CLUSTER NODES)");
        sb.AppendLine(separator);
        sb.AppendLine($"Total Executions: {results.TotalExecutions}");
        sb.AppendLine($"Concurrent Execution Violation Periods: {results.TotalViolationPeriods}");
        sb.AppendLine($"Active Nodes: {results.NodeCount}");

        if (results.TotalViolationPeriods > 0)
        {
            sb.AppendLine().AppendLine("Violation Period Details:");
            foreach (var violation in results.AllViolations.OrderBy(v => v.DetectedAt))
            {
                var duration = violation.EndedAt.HasValue
                    ? $"{(violation.EndedAt.Value - violation.DetectedAt).TotalMilliseconds:F0}ms"
                    : "ongoing";
                sb.AppendLine($"  [{violation.DetectedAt:HH:mm:ss.fff}] Node: {violation.NodeId} | Count: {violation.ConcurrentCount} | Duration: {duration} | Execution: {violation.ExecutionId}");
            }
        }

        sb.AppendLine().AppendLine("Node Execution Breakdown:");
        foreach (var node in results.NodeBreakdown.Values.OrderBy(n => n.NodeId))
        {
            sb.AppendLine($"  {node.NodeId}: {node.ExecutionCount} executions (first: {node.FirstExecution:HH:mm:ss.fff}, last: {node.LastExecution:HH:mm:ss.fff})");
        }

        sb.AppendLine(separator);

        return sb.ToString();
    }
}

/// <summary>
/// Aggregated test results from all cluster nodes.
/// </summary>
public record AggregatedTestResults(
    int TotalViolationPeriods,
    int TotalExecutions,
    int NodeCount,
    List<ViolationRecord> AllViolations,
    Dictionary<string, NodeExecutionBreakdown> NodeBreakdown
);

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
#endif
