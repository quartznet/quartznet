#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

using Microsoft.Extensions.Logging;

using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Every event the ADO.NET job store logs outside clustering, misfire handling and lock handling, as
/// source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 3000-3499 belong to this area and are allocated in file order: the store itself
/// (3000-3099), <c>AdoUtil</c> (3100-3109), the connection holder (3110-3129), the two store flavours
/// (3130-3149) and the driver delegate (3150-3179). Clustering is 3500-3599 in <see cref="ClusterLog" />,
/// misfire handling 3600-3699 in <see cref="MisfireLog" />, and lock handling 3700-3799 in
/// <see cref="LockHandlerLog" />.
/// </para>
/// <para>
/// An id, once given out, is what an operator filters and alerts on, so it is never reused for a
/// different event and never renumbered; <c>LogEventCatalogTest</c> makes a change to one a reviewed
/// diff.
/// </para>
/// </remarks>
internal static partial class AdoJobStoreLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Detected SQLite usage, changing to use SqliteLockHandler for in-memory locking")]
    public static partial void SqliteLockHandlerSubstituted(this ILogger logger);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "With SQLite we need to set AcquireTriggersWithinLock to true, changing")]
    public static partial void SqliteAcquireTriggersWithinLockForced(this ILogger logger);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Detected usage of SQLiteDelegate - forcing transaction isolation level to 'Serializable'")]
    public static partial void SqliteSerializableIsolationForced(this ILogger logger);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information, Message = "With SQLite all operations must be serialized, setting LockAllOperations to true")]
    public static partial void SqliteLockAllOperationsForced(this ILogger logger);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning, Message = "The configured transaction isolation level applies only to connections the job store opens itself: an operation running on a connection enlisted by the application uses that transaction's isolation level instead.")]
    public static partial void IsolationLevelIgnoredForEnlistedTransactions(this ILogger logger);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information, Message = "Detected usage of SqlServerDelegate - defaulting 'selectWithLockSQL' to '{DefaultLockSql}'.")]
    public static partial void SqlServerLockSqlDefaulted(this ILogger logger, string defaultLockSql);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Information, Message = "Using db table-based data access locking (synchronization) via {LockHandlerType}.")]
    public static partial void UsingDatabaseLocking(this ILogger logger, string lockHandlerType);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Information, Message = "Using thread monitor-based data access locking (synchronization) via {LockHandlerType}.")]
    public static partial void UsingMonitorLocking(this ILogger logger, string lockHandlerType);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Warning, Message = "A row-lock statement is configured, but SQLite serializes callers in process rather than by locking a row, so the statement is ignored.")]
    public static partial void RowLockStatementIgnoredBySqlite(this ILogger logger);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Warning, Message = "A row-lock statement is configured, but lock handler {LockHandlerType} was supplied rather than built by the job store, so the statement is ignored. Pass it to the handler's constructor, or remove the lock handler and let the store choose one.")]
    public static partial void RowLockStatementIgnoredBySuppliedHandler(this ILogger logger, string lockHandlerType);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Warning, Message = "Accepting enlisted transactions with SQLite keeps in-process locking: SqliteLockHandler releases the lock when the scheduling call returns, while the application transaction still holds the SQLite writer lock, so a concurrent scheduler operation can fail with 'database is locked' until that transaction completes. Keep enlisted transactions short, or use a database that supports row locking.")]
    public static partial void EnlistedTransactionsWithSqlite(this ILogger logger);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Warning, Message = "Accepting enlisted transactions with lock handler {LockHandlerType}, which does not lock in the database. Its locks are released before the application commits its transaction, so concurrent callers can act on scheduling data that is not visible to them yet.")]
    public static partial void EnlistedTransactionsWithInProcessLocking(this ILogger logger, string lockHandlerType);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Warning, Message = "Detected usage of SqlServerDelegate and UpdateRowLockHandler, removing 'quartz.jobStore.lockHandler.type' would allow more efficient SQL Server specific (UPDLOCK,ROWLOCK) row access")]
    public static partial void SqlServerCouldUseRowLocking(this ILogger logger);

    [LoggerMessage(EventId = 3013, Level = LogLevel.Warning, Message = "Detected usage of SQL Server provider without SqlServerDelegate, SqlServerDelegate would provide better performance")]
    public static partial void SqlServerProviderWithoutSqlServerDelegate(this ILogger logger);

    [LoggerMessage(EventId = 3014, Level = LogLevel.Information, Message = "Successfully validated presence of {SchemaObjectCount} schema objects")]
    public static partial void SchemaValidated(this ILogger logger, int schemaObjectCount);

    [LoggerMessage(EventId = 3039, Level = LogLevel.Information, Message = "Created the schema objects missing under table prefix '{TablePrefix}'")]
    public static partial void SchemaCreated(this ILogger logger, string tablePrefix);

    // Debug: on a cluster starting together this is every node but one, every time, and it is not a
    // problem -- the schema is there and the store is about to validate it.
    [LoggerMessage(EventId = 3040, Level = LogLevel.Debug, Message = "Schema creation under table prefix '{TablePrefix}' failed, but the schema validates, so another node created it first")]
    public static partial void SchemaCreatedByAnotherNode(this ILogger logger, string tablePrefix, Exception exception);

    // Debug: on a cluster starting together this is the ordinary way round a race, and the attempt
    // after it usually succeeds. It is here so that a few seconds spent converging is visible rather
    // than being a stall nobody can account for.
    [LoggerMessage(EventId = 3041, Level = LogLevel.Debug, Message = "Schema creation under table prefix '{TablePrefix}' failed on attempt {Attempt} of {Attempts} and the schema does not validate yet, retrying")]
    public static partial void SchemaCreationRetrying(this ILogger logger, string tablePrefix, int attempt, int attempts, Exception exception);

    [LoggerMessage(EventId = 3015, Level = LogLevel.Error, Message = "Failure occurred during job recovery: {ExceptionMessage}")]
    public static partial void JobRecoveryFailed(this ILogger logger, string exceptionMessage, Exception exception);

    [LoggerMessage(EventId = 3016, Level = LogLevel.Warning, Message = "Database connection Shutdown unsuccessful.")]
    public static partial void DatabaseShutdownFailed(this ILogger logger, Exception exception);

    // Warning rather than Error: whatever the handler failed to release is leaked for the life of the
    // process, which an operator wants to know about, but the scheduler is down either way.
    [LoggerMessage(EventId = 3042, Level = LogLevel.Warning, Message = "Lock handler {LockHandlerType} failed to shut down, so whatever it opened is still open.")]
    public static partial void LockHandlerShutdownFailed(this ILogger logger, string lockHandlerType, Exception exception);

    [LoggerMessage(EventId = 3017, Level = LogLevel.Error, Message = "Error returning lock: {ExceptionMessage}")]
    public static partial void LockReleaseFailed(this ILogger logger, string exceptionMessage, Exception exception);

    [LoggerMessage(EventId = 3018, Level = LogLevel.Information, Message = "Freed {Count} triggers from 'acquired' / 'blocked' state.")]
    public static partial void TriggersFreedFromAcquiredOrBlocked(this ILogger logger, int count);

    [LoggerMessage(EventId = 3019, Level = LogLevel.Information, Message = "Recovering {Count} jobs that were in-progress at the time of the last shut-down.")]
    public static partial void RecoveringInProgressJobs(this ILogger logger, int count);

    [LoggerMessage(EventId = 3020, Level = LogLevel.Information, Message = "Recovery complete.")]
    public static partial void RecoveryComplete(this ILogger logger);

    [LoggerMessage(EventId = 3021, Level = LogLevel.Information, Message = "Removed {Count} 'complete' triggers.")]
    public static partial void CompleteTriggersRemoved(this ILogger logger, int count);

    [LoggerMessage(EventId = 3022, Level = LogLevel.Information, Message = "Removed {Count} stale fired job entries.")]
    public static partial void StaleFiredJobEntriesRemoved(this ILogger logger, int count);

    [LoggerMessage(EventId = 3023, Level = LogLevel.Error, Message = "Error recovering stale acquired trigger '{TriggerKey}'")]
    public static partial void StaleAcquiredTriggerRecoveryFailed(this ILogger logger, TriggerKey triggerKey, Exception exception);

    [LoggerMessage(EventId = 3024, Level = LogLevel.Information, Message = "Recovered {RecoveredCount} trigger(s) stuck in ACQUIRED state (stale threshold: {StaleThreshold})")]
    public static partial void StaleAcquiredTriggersRecovered(this ILogger logger, int recoveredCount, TimeSpan staleThreshold);

    [LoggerMessage(EventId = 3025, Level = LogLevel.Information, Message = "Trigger {TriggerKey} reset from ERROR state to: {NewState}")]
    public static partial void TriggerResetFromError(this ILogger logger, TriggerKey triggerKey, StoredTriggerState newState);

    [LoggerMessage(EventId = 3026, Level = LogLevel.Error, Message = "Error retrieving job, setting trigger state to ERROR.")]
    public static partial void JobRetrievalFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3027, Level = LogLevel.Error, Message = "Unable to set trigger state to ERROR.")]
    public static partial void TriggerErrorStateUpdateFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3028, Level = LogLevel.Warning, Message = "Trigger {TriggerKey} returned null on nextFireTime and yet still exists in DB!")]
    public static partial void TriggerHasNoNextFireTime(this ILogger logger, TriggerKey triggerKey);

    [LoggerMessage(EventId = 3029, Level = LogLevel.Error, Message = "Caught job persistence exception: {ExceptionMessage}")]
    public static partial void JobPersistenceExceptionCaught(this ILogger logger, string exceptionMessage, Exception exception);

    [LoggerMessage(EventId = 3030, Level = LogLevel.Error, Message = "Caught exception: {ExceptionMessage}")]
    public static partial void ExceptionCaught(this ILogger logger, string exceptionMessage, Exception exception);

    [LoggerMessage(EventId = 3031, Level = LogLevel.Information, Message = "Not firing trigger {TriggerKey} for [DisallowConcurrentExecution] job {JobKey} - already executing on another node.")]
    public static partial void ConcurrentExecutionDeclined(this ILogger logger, TriggerKey triggerKey, JobKey jobKey);

    [LoggerMessage(EventId = 3032, Level = LogLevel.Warning, Message = "Trigger {TriggerKey} references calendar '{CalendarName}', which does not exist - the fire was skipped and the trigger will not run until the calendar is added or the reference is cleared.")]
    public static partial void TriggerReferencesMissingCalendar(this ILogger logger, TriggerKey triggerKey, string calendarName);

    [LoggerMessage(EventId = 3033, Level = LogLevel.Information, Message = "Trigger {Trigger} set to ERROR state.")]
    public static partial void TriggerSetToError(this ILogger logger, TriggerKey trigger);

    [LoggerMessage(EventId = 3034, Level = LogLevel.Information, Message = "All triggers of Job {Job} set to ERROR state.")]
    public static partial void JobTriggersSetToError(this ILogger logger, JobKey job);

    [LoggerMessage(EventId = 3035, Level = LogLevel.Information, Message = "ConnectionAndTransactionHolder passed to RollbackConnection was null, ignoring")]
    public static partial void RollbackWithoutConnectionHolder(this ILogger logger);

    [LoggerMessage(EventId = 3036, Level = LogLevel.Debug, Message = "ConnectionAndTransactionHolder passed to CommitConnection was null, ignoring")]
    public static partial void CommitWithoutConnectionHolder(this ILogger logger);

    [LoggerMessage(EventId = 3037, Level = LogLevel.Error, Message = "RetryExecuteInLocalTransactionLock: RuntimeException {ExceptionMessage}")]
    public static partial void RetryInLocalTransactionLockFailed(this ILogger logger, string exceptionMessage, Exception exception);

    [LoggerMessage(EventId = 3038, Level = LogLevel.Warning, Message = "Transient exception on attempt {Attempt} of {TotalAttempts} in ExecuteInLocalTransactionLock, will retry after {RetryInterval}")]
    public static partial void TransientFailureInLocalTransactionLock(this ILogger logger, int attempt, int totalAttempts, TimeSpan retryInterval, Exception exception);

    [LoggerMessage(EventId = 3100, Level = LogLevel.Debug, Message = "Prepared SQL: {Sql}")]
    public static partial void SqlPrepared(this ILogger logger, string sql);

    [LoggerMessage(EventId = 3110, Level = LogLevel.Error, Message = "Unexpected exception closing Connection.  This is often due to a Connection being returned after or during shutdown.")]
    public static partial void ConnectionCloseFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3111, Level = LogLevel.Debug, Message = "Exception disposing connection or transaction. This is often due to a connection being returned after or during shutdown.")]
    public static partial void ConnectionDisposeFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3112, Level = LogLevel.Debug, Message = "Rollback skipped - transaction is no longer connected, database will have aborted it")]
    public static partial void RollbackSkippedTransactionDisconnected(this ILogger logger);

    [LoggerMessage(EventId = 3113, Level = LogLevel.Debug, Message = "Rollback failed due to transient error")]
    public static partial void RollbackFailedTransiently(this ILogger logger);

    [LoggerMessage(EventId = 3114, Level = LogLevel.Error, Message = "Couldn't rollback ADO.NET connection. {ExceptionMessage}")]
    public static partial void RollbackFailed(this ILogger logger, string exceptionMessage, Exception exception);

    [LoggerMessage(EventId = 3130, Level = LogLevel.Information, Message = "ExternalTransactionJobStore initialized.")]
    public static partial void ExternalTransactionStoreInitialized(this ILogger logger);

    [LoggerMessage(EventId = 3131, Level = LogLevel.Warning, Message = "Database connection shutdown unsuccessful.")]
    public static partial void ExternalTransactionStoreShutdownFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3140, Level = LogLevel.Information, Message = "LocalTransactionJobStore initialized.")]
    public static partial void LocalTransactionStoreInitialized(this ILogger logger);

    [LoggerMessage(EventId = 3150, Level = LogLevel.Warning, Message = "Couldn't find calendar with name '{CalendarName}'")]
    public static partial void CalendarNotFound(this ILogger logger, string calendarName);

    [LoggerMessage(EventId = 3151, Level = LogLevel.Debug, Message = "Deleting job: {JobKey}")]
    public static partial void JobDeleting(this ILogger logger, JobKey jobKey);

    [LoggerMessage(EventId = 3152, Level = LogLevel.Debug, Message = "No job for trigger '{TriggerKey}'")]
    public static partial void NoJobForTrigger(this ILogger logger, TriggerKey triggerKey);

    [LoggerMessage(EventId = 3153, Level = LogLevel.Debug, Message = "Adding TriggerPersistenceDelegate of type: {Type}")]
    public static partial void TriggerPersistenceDelegateAdded(this ILogger logger, Type type);

    [LoggerMessage(EventId = 3154, Level = LogLevel.Warning, Message = "Misfired trigger '{TriggerKey}' has no {TriggerType} row and is skipped")]
    public static partial void MisfiredTriggerHasNoTypeRow(this ILogger logger, TriggerKey triggerKey, string triggerType);

    [LoggerMessage(EventId = 3155, Level = LogLevel.Warning, Message = "Batched statement execution failed, retrying {StatementCount} statement(s) individually")]
    public static partial void BatchedStatementExecutionFailed(this ILogger logger, int statementCount, Exception exception);
}
