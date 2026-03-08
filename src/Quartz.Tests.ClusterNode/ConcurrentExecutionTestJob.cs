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
    private sealed record ViolationCheckContext(
        DbConnection Connection,
        DbTransaction Transaction,
        IDatabaseProvider Provider,
        int CurrentCount,
        int ConcurrentCount,
        string ExecutionId,
        string NodeId,
        ILogger Logger);

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
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable).ConfigureAwait(false);

        try
        {
            var currentCount = await GetActiveExecutionCount(connection, transaction).ConfigureAwait(false);
            logger.LogDebug("Current active executions before insert: {Count}", currentCount);

            await InsertExecution(connection, transaction, provider, executionId, nodeId, startTime).ConfigureAwait(false);

            var concurrentCount = currentCount + 1;
            var ctx = new ViolationCheckContext(connection, transaction, provider, currentCount, concurrentCount, executionId, nodeId, logger);
            var (violationIncidentStarted, isInViolatedState) =
                await UpdateViolationState(ctx).ConfigureAwait(false);

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

    private static async Task<int> GetActiveExecutionCount(DbConnection connection, DbTransaction transaction)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT COUNT(*) FROM ClusterTestExecutions WHERE EndTime IS NULL";
        var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        return result != null ? Convert.ToInt32(result) : 0;
    }

    private static async Task InsertExecution(
        DbConnection connection, DbTransaction transaction, IDatabaseProvider provider,
        string executionId, string nodeId, DateTime startTime)
    {
        var paramPrefix = provider.GetParameterPrefix();
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO ClusterTestExecutions (ExecutionId, NodeId, StartTime, EndTime)
                  VALUES ({paramPrefix}ExecutionId, {paramPrefix}NodeId, {paramPrefix}StartTime, NULL)";

        AddParameter(cmd, paramPrefix, "ExecutionId", executionId);
        AddParameter(cmd, paramPrefix, "NodeId", nodeId);
        AddParameter(cmd, paramPrefix, "StartTime", startTime);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<(bool violationIncidentStarted, bool isInViolatedState)> UpdateViolationState(ViolationCheckContext ctx)
    {
        var paramPrefix = ctx.Provider.GetParameterPrefix();

        // Check if we were previously in a clean state (no active violations)
        await using var checkCmd = ctx.Connection.CreateCommand();
        checkCmd.Transaction = ctx.Transaction;
        checkCmd.CommandText = "SELECT COUNT(*) FROM ClusterTestViolations WHERE EndedAt IS NULL";
        var result = await checkCmd.ExecuteScalarAsync().ConfigureAwait(false);
        var wasInCleanState = (result != null ? Convert.ToInt32(result) : 0) == 0;

        // A new incident starts only when we transition from clean (0) to violated (2+)
        var violationIncidentStarted = ctx.CurrentCount == 1 && wasInCleanState;
        var isInViolatedState = ctx.CurrentCount > 0 && !wasInCleanState;

        if (violationIncidentStarted)
        {
            ctx.Logger.LogWarning("Recording NEW violation period - concurrent count: {Count}", ctx.ConcurrentCount);
            await RecordViolation(ctx.Connection, ctx.Transaction, paramPrefix, ctx.ExecutionId, ctx.NodeId, ctx.ConcurrentCount).ConfigureAwait(false);
        }
        else if (ctx.CurrentCount == 0 && !wasInCleanState)
        {
            ctx.Logger.LogInformation("Violation period ended - returning to clean state");
            await EndActiveViolations(ctx.Connection, ctx.Transaction, paramPrefix).ConfigureAwait(false);
        }

        return (violationIncidentStarted, isInViolatedState);
    }

    private static async Task RecordViolation(
        DbConnection connection, DbTransaction transaction, string paramPrefix,
        string executionId, string nodeId, int concurrentCount)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"INSERT INTO ClusterTestViolations (ExecutionId, NodeId, DetectedAt, ConcurrentCount, EndedAt)
                      VALUES ({paramPrefix}ExecutionId, {paramPrefix}NodeId, {paramPrefix}DetectedAt, {paramPrefix}ConcurrentCount, NULL)";

        AddParameter(cmd, paramPrefix, "ExecutionId", executionId);
        AddParameter(cmd, paramPrefix, "NodeId", nodeId);
        AddParameter(cmd, paramPrefix, "DetectedAt", DateTime.UtcNow);
        AddParameter(cmd, paramPrefix, "ConcurrentCount", concurrentCount);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task EndActiveViolations(DbConnection connection, DbTransaction transaction, string paramPrefix)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $@"UPDATE ClusterTestViolations
                      SET EndedAt = {paramPrefix}EndedAt
                      WHERE EndedAt IS NULL";

        AddParameter(cmd, paramPrefix, "EndedAt", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand cmd, string paramPrefix, string name, object value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = $"{paramPrefix}{name}";
        param.Value = value;
        cmd.Parameters.Add(param);
    }

    private static async Task EndExecution(DbConnection connection, IDatabaseProvider provider, string executionId, ILogger logger)
    {
        try
        {
            var paramPrefix = provider.GetParameterPrefix();
            await using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = $"UPDATE ClusterTestExecutions SET EndTime = {paramPrefix}EndTime WHERE ExecutionId = {paramPrefix}ExecutionId";

            AddParameter(updateCmd, paramPrefix, "EndTime", DateTime.UtcNow);
            AddParameter(updateCmd, paramPrefix, "ExecutionId", executionId);

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
