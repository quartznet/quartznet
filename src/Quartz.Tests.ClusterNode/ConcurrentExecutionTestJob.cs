using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Quartz.Tests.ClusterNode;

/// <summary>
/// A test job that should never run concurrently across cluster nodes.
/// This job is used to detect violations of the DisallowConcurrentExecution attribute.
/// Uses database state to detect concurrent execution across multiple nodes.
/// </summary>
[DisallowConcurrentExecution]
[PersistJobDataAfterExecution]
public class ConcurrentExecutionTestJob : IJob
{
    // Local tracking for console output
    private static int _localExecutions;
    private readonly ILogger<ConcurrentExecutionTestJob> logger;

    // Constructor with DI
    public ConcurrentExecutionTestJob(ILogger<ConcurrentExecutionTestJob> logger)
    {
        this.logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var nodeId = context.Scheduler.SchedulerInstanceId;
        var executionId = Guid.NewGuid().ToString("N")[..8];
        var startTime = DateTime.UtcNow;

        // Get connection string, provider, and delay from job data
        var connectionString = context.JobDetail.JobDataMap.GetString("connectionString")
            ?? throw new InvalidOperationException("connectionString not found in JobDataMap");
        var databaseProviderName = context.JobDetail.JobDataMap.GetString("databaseProvider") ?? "SqlServer";
        var provider = DatabaseProviderFactory.GetProvider(databaseProviderName);
        var jobDelayMsText = context.JobDetail.JobDataMap.GetString("jobDelayMs");
        var jobDelayMs = int.TryParse(jobDelayMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDelay)
            ? parsedDelay
            : 100;

        await using var connection = provider.CreateConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        // Increment local counter for display
        var localCount = Interlocked.Increment(ref _localExecutions);

        // Start execution: Atomically check and record that we're executing
        var (currentCount, violationIncidentStarted, isInViolatedState) = await StartExecution(connection, provider, nodeId, executionId, startTime, logger).ConfigureAwait(false);

        if (violationIncidentStarted)
        {
            logger.LogError("*** VIOLATION DETECTED *** New concurrent execution incident | Concurrent count: {ConcurrentCount} | Node: {NodeId} | Execution: {ExecutionId}",
                currentCount, nodeId, executionId);
        }
        else if (isInViolatedState)
        {
            logger.LogDebug("Executing in already-violated state | Concurrent count: {ConcurrentCount} | Node: {NodeId} | Execution: {ExecutionId}",
                currentCount, nodeId, executionId);
        }
        else
        {
            logger.LogInformation("Job executing on Node: {NodeId} | Execution: {ExecutionId} | Local count: {LocalCount}",
                nodeId, executionId, localCount);
        }

        try
        {
            // Simulate work to increase the chance of concurrent execution detection
            await Task.Delay(jobDelayMs).ConfigureAwait(false);
            logger.LogDebug("-> Hello from {NodeId} (execution {ExecutionId})", nodeId, executionId);
        }
        finally
        {
            // End execution: Atomically decrement counter
            await EndExecution(connection, provider, executionId, logger).ConfigureAwait(false);

            // Print periodic summary
            var snapshot = Volatile.Read(ref _localExecutions);

            if (snapshot % 20 == 0)
            {
                await PrintSummary(connection, logger).ConfigureAwait(false);
            }
        }
    }

    private static async Task<(int currentCount, bool violationIncidentStarted, bool isInViolatedState)> StartExecution(
        DbConnection connection, IDatabaseProvider provider, string nodeId, string executionId, DateTime startTime, ILogger logger)
    {
        // Use a transaction to atomically check and update
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable).ConfigureAwait(false);

        try
        {
            // Get the current execution count
            await using var getCurrentCountCmd = connection.CreateCommand();
            getCurrentCountCmd.Transaction = transaction;
            getCurrentCountCmd.CommandText = "SELECT COUNT(*) FROM ClusterTestExecutions WHERE EndTime IS NULL";
            var currentCountResult = await getCurrentCountCmd.ExecuteScalarAsync().ConfigureAwait(false);
            var currentCount = currentCountResult != null ? Convert.ToInt32(currentCountResult) : 0;

            logger.LogDebug("Current active executions before insert: {Count}", currentCount);

            // Record this execution
            var paramPrefix = provider.GetParameterPrefix();
            var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = $@"INSERT INTO ClusterTestExecutions (ExecutionId, NodeId, StartTime, EndTime)
                  VALUES ({paramPrefix}ExecutionId, {paramPrefix}NodeId, {paramPrefix}StartTime, NULL)";

            var executionIdParam = insertCmd.CreateParameter();
            executionIdParam.ParameterName = $"{paramPrefix}ExecutionId";
            executionIdParam.Value = executionId;
            insertCmd.Parameters.Add(executionIdParam);

            var nodeIdParam = insertCmd.CreateParameter();
            nodeIdParam.ParameterName = $"{paramPrefix}NodeId";
            nodeIdParam.Value = nodeId;
            insertCmd.Parameters.Add(nodeIdParam);

            var startTimeParam = insertCmd.CreateParameter();
            startTimeParam.ParameterName = $"{paramPrefix}StartTime";
            startTimeParam.Value = startTime;
            insertCmd.Parameters.Add(startTimeParam);

            await insertCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

            var concurrentCount = currentCount + 1;

            // Check if we were previously in a clean state (no active violations)
            // A violation "period" starts when we go from clean (0 executions) to violated (2+ executions)
            // and ends when we return to a clean state
            var wasCleanCmd = connection.CreateCommand();
            wasCleanCmd.Transaction = transaction;
            wasCleanCmd.CommandText = "SELECT COUNT(*) FROM ClusterTestViolations WHERE EndedAt IS NULL";
            var activeViolationPeriodsResult = await wasCleanCmd.ExecuteScalarAsync().ConfigureAwait(false);
            var activeViolationPeriods = activeViolationPeriodsResult != null ? Convert.ToInt32(activeViolationPeriodsResult) : 0;
            var wasInCleanState = activeViolationPeriods == 0;

            // Count distinct violation incidents, not every overlapping execution.
            // A new incident starts only when we transition from clean (0) to violated (2+)
            var violationIncidentStarted = currentCount == 1 && wasInCleanState;  // New violation period
            var isInViolatedState = currentCount > 0 && !wasInCleanState;          // Continuing violation period

            if (violationIncidentStarted)
            {
                logger.LogWarning("Recording NEW violation period - concurrent count: {Count}", concurrentCount);

                var recordViolationCmd = connection.CreateCommand();
                recordViolationCmd.Transaction = transaction;
                recordViolationCmd.CommandText = $@"INSERT INTO ClusterTestViolations (ExecutionId, NodeId, DetectedAt, ConcurrentCount, EndedAt)
                      VALUES ({paramPrefix}ExecutionId, {paramPrefix}NodeId, {paramPrefix}DetectedAt, {paramPrefix}ConcurrentCount, NULL)";

                var violationExecutionIdParam = recordViolationCmd.CreateParameter();
                violationExecutionIdParam.ParameterName = $"{paramPrefix}ExecutionId";
                violationExecutionIdParam.Value = executionId;
                recordViolationCmd.Parameters.Add(violationExecutionIdParam);

                var violationNodeIdParam = recordViolationCmd.CreateParameter();
                violationNodeIdParam.ParameterName = $"{paramPrefix}NodeId";
                violationNodeIdParam.Value = nodeId;
                recordViolationCmd.Parameters.Add(violationNodeIdParam);

                var detectedAtParam = recordViolationCmd.CreateParameter();
                detectedAtParam.ParameterName = $"{paramPrefix}DetectedAt";
                detectedAtParam.Value = DateTime.UtcNow;
                recordViolationCmd.Parameters.Add(detectedAtParam);

                var concurrentCountParam = recordViolationCmd.CreateParameter();
                concurrentCountParam.ParameterName = $"{paramPrefix}ConcurrentCount";
                concurrentCountParam.Value = concurrentCount;
                recordViolationCmd.Parameters.Add(concurrentCountParam);

                await recordViolationCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            else if (currentCount == 0 && !wasInCleanState)
            {
                // We've returned to clean state - end the violation period
                logger.LogInformation("Violation period ended - returning to clean state");

                var endViolationCmd = connection.CreateCommand();
                endViolationCmd.Transaction = transaction;
                endViolationCmd.CommandText = $@"UPDATE ClusterTestViolations
                      SET EndedAt = {paramPrefix}EndedAt
                      WHERE EndedAt IS NULL";

                var endedAtParam = endViolationCmd.CreateParameter();
                endedAtParam.ParameterName = $"{paramPrefix}EndedAt";
                endedAtParam.Value = DateTime.UtcNow;
                endViolationCmd.Parameters.Add(endedAtParam);

                await endViolationCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
            return (concurrentCount, violationIncidentStarted, isInViolatedState);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in StartExecution, rolling back transaction");
            await transaction.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EndExecution(DbConnection connection, IDatabaseProvider provider, string executionId, ILogger logger)
    {
        try
        {
            var paramPrefix = provider.GetParameterPrefix();
            await using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = $"UPDATE ClusterTestExecutions SET EndTime = {paramPrefix}EndTime WHERE ExecutionId = {paramPrefix}ExecutionId";

            var endTimeParam = updateCmd.CreateParameter();
            endTimeParam.ParameterName = $"{paramPrefix}EndTime";
            endTimeParam.Value = DateTime.UtcNow;
            updateCmd.Parameters.Add(endTimeParam);

            var executionIdParam = updateCmd.CreateParameter();
            executionIdParam.ParameterName = $"{paramPrefix}ExecutionId";
            executionIdParam.Value = executionId;
            updateCmd.Parameters.Add(executionIdParam);

            await updateCmd.ExecuteNonQueryAsync().ConfigureAwait(false);

            logger.LogDebug("Execution {ExecutionId} completed", executionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error ending execution {ExecutionId}", executionId);
            throw;
        }
    }

    private static async Task PrintSummary(DbConnection connection, ILogger logger)
    {
        try
        {
            await using var statsCmd = connection.CreateCommand();
            statsCmd.CommandText = @"SELECT
                    (SELECT COUNT(*) FROM ClusterTestExecutions) as TotalExecutions,
                    (SELECT COUNT(*) FROM ClusterTestViolations) as TotalViolations,
                    (SELECT COUNT(DISTINCT NodeId) FROM ClusterTestExecutions) as ActiveNodes";

            await using var reader = await statsCmd.ExecuteReaderAsync().ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                var totalExecutions = Convert.ToInt32(reader.GetValue(0));
                var totalViolationIncidents = Convert.ToInt32(reader.GetValue(1));
                var activeNodes = Convert.ToInt32(reader.GetValue(2));

                logger.LogInformation("=== Summary: Total Executions: {TotalExecutions} | Violation Periods: {ViolationPeriods} | Active Nodes: {ActiveNodes} ===",
                    totalExecutions, totalViolationIncidents, activeNodes);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error printing summary");
        }
    }
}
