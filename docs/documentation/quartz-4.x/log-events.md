---
title: Log Events
---

# Log Events

Every message Quartz.NET writes carries a **stable event id**. An id is what an operator filters, alerts
and dashboards on: a message can be reworded and a template can gain a placeholder, but the id that came
before goes on meaning the same event. This page is every id that ships, so a line in your log can be
looked up rather than guessed at.

Ids are allocated in ranges, one set per package, so two Quartz packages loaded into the same process
never disagree about what an id means:

| Range | Package | Area |
|---|---|---|
| 1000–1999 | `Quartz` | scheduler core |
| 2000–2999 | `Quartz` | in-memory store |
| 3000–3499 | `Quartz` | ADO.NET store |
| 3500–3599 | `Quartz` | clustering |
| 3600–3699 | `Quartz` | misfire handling |
| 3700–3799 | `Quartz` | lock handlers |
| 4000–4999 | `Quartz` | configuration, dependency injection and hosting |
| 5000–5999 | `Quartz` | serialization, type loading, triggers, calendars and utilities |
| 6000–6999 | `Quartz.Plugins` | plugins |
| 7000–7999 | `Quartz.Jobs` | jobs |
| 8000–8999 | `Quartz.Extensions.Redis` | Redis |
| 9000–9099 | `Quartz.AspNetCore` | the HTTP API |

Quartz logs through `Microsoft.Extensions.Logging` and writes nothing above `Error`, so a
`LogLevel.Critical` in your log is never Quartz's. Under a host it uses whatever the application already
configured; a standalone scheduler is told where to log with `LogProvider.SetLogProvider(loggerFactory)` —
see [Observability](packages/opentelemetry-integration.md#logging).

::: tip Filtering by id
`ILoggerFactory`'s own filters select by category and level, not by id, so filtering on an id is the
logging provider's job — Serilog's `EventId.Id` property, an OpenTelemetry log processor, or a query in
whatever collects the logs. The categories are the declaring types, which is why the table below names
each event's source as well.
:::

## Every event

Generated from the snapshots the log catalogue tests keep, so this table cannot drift from what the
packages emit. Adding an event, renumbering one or rewording a message is a reviewed diff in
`src/*/Verify/LogEventCatalogTest_*.verified.txt`, and `dotnet fallout DocsLogEvents` brings this page
along.

A `{Placeholder}` in a template is a structured-logging property, and its name is the property name a
structured sink records — which is what makes matching on the *shape* of a message reliable where
matching on its text is not.

<!-- logEvents -->

| Id | Level | Package | Message template |
|---|---|---|---|
| 1000 | Information | `Quartz` | `"JobFactory set to: {Value}"` |
| 1001 | Information | `Quartz` | `"Quartz Scheduler created"` |
| 1002 | Information | `Quartz` | `"Scheduler {SchedulerIdentifier} started."` |
| 1003 | Error | `Quartz` | `"Unable to start scheduler after startup delay."` |
| 1004 | Information | `Quartz` | `"Scheduler {SchedulerIdentifier} paused."` |
| 1005 | Information | `Quartz` | `"Scheduler {SchedulerIdentifier} shutting down."` |
| 1006 | Warning | `Quartz` | `"Scheduler {SchedulerIdentifier} gave up waiting for its running jobs, which are still executing. Their job store updates may not complete, and the store is about to be shut down under them."` |
| 1007 | Information | `Quartz` | `"Scheduler {SchedulerIdentifier} Shutdown complete."` |
| 1008 | Error | `Quartz` | `"Error while notifying SchedulerListener of error"` |
| 1009 | Error | `Quartz` | `"  Original error (for notification) was: {Message}"` |
| 1010 | Error | `Quartz` | `"Error while notifying SchedulerListener of unscheduled job. Trigger={TriggerKey}"` |
| 1011 | Error | `Quartz` | `"Error while notifying SchedulerListener of paused group: {Group}"` |
| 1012 | Error | `Quartz` | `"Error while notifying SchedulerListener of trigger in error state. Trigger={TriggerKey}"` |
| 1013 | Error | `Quartz` | `"Error while notifying SchedulerListener of job triggers in error state. Job={JobKey}"` |
| 1014 | Error | `Quartz` | `"Error while notifying SchedulerListener of paused trigger. Trigger={TriggerKey}"` |
| 1015 | Error | `Quartz` | `"Error while notifying SchedulerListener of resumed trigger. Trigger={TriggerKey}"` |
| 1016 | Error | `Quartz` | `"Error while notifying SchedulerListener of paused job. Job={JobKey}"` |
| 1017 | Error | `Quartz` | `"Error while notifying SchedulerListener of resumed job: {JobKey}"` |
| 1018 | Error | `Quartz` | `"Error while notifying SchedulerListener of resumed group: {Group}"` |
| 1019 | Error | `Quartz` | `"Error while notifying SchedulerListener of {Action}"` |
| 1030 | Debug | `Quartz` | `"Batch acquisition of {TriggerCount} triggers"` |
| 1031 | Error | `Quartz` | `"quartzSchedulerThreadLoop: RuntimeException {Message}"` |
| 1032 | Error | `Quartz` | `"DbException while firing trigger {Trigger}"` |
| 1033 | Debug | `Quartz` | `"ThreadPool.TryRun() returned false due to scheduler shutdown, completing trigger"` |
| 1034 | Error | `Quartz` | `"ThreadPool.TryRun() returned false"` |
| 1035 | Error | `Quartz` | `"Runtime error occurred in main trigger firing loop."` |
| 1036 | Error | `Quartz` | `"Error releasing acquired trigger '{TriggerKey}' {Context}"` |
| 1050 | Debug | `Quartz` | `"Calling Execute on job {JobKey}"` |
| 1051 | Information | `Quartz` | `"Job {JobDetailKey} was cancelled"` |
| 1052 | Error | `Quartz` | `"Job {JobDetailKey} threw a JobExecutionException: "` |
| 1053 | Error | `Quartz` | `"Job {JobDetailKey} threw an unhandled Exception: "` |
| 1054 | Debug | `Quartz` | `"Trigger instruction : {InstructionCode}"` |
| 1055 | Debug | `Quartz` | `"Rescheduling trigger to reexecute"` |
| 1056 | Information | `Quartz` | `"Job of trigger {TriggerKey} failed; retry {Attempt} of {MaxAttempts} scheduled for {RetryTimeUtc}"` |
| 1070 | Information | `Quartz` | `"Initialized Scheduler Signaller of type: {Type}"` |
| 1071 | Error | `Quartz` | `"Error notifying listeners of trigger misfire."` |
| 1080 | Error | `Quartz` | `"{Message} (scheduler: {SchedulerName})"` |
| 1081 | Error | `Quartz` | `"{Message} (scheduler: {SchedulerName}, trigger: {TriggerKey}, job: {JobKey}, fire instance: {FireInstanceId})"` |
| 1090 | Warning | `Quartz` | `"Job {JobKey} exceeded the {Budget} it was allowed; interrupting fire instance {FireInstanceId}"` |
| 1091 | Debug | `Quartz` | `"Job {JobKey} finished before the interrupt its timeout asked for reached fire instance {FireInstanceId}"` |
| 1092 | Error | `Quartz` | `"Interrupting timed out job {JobKey}, fire instance {FireInstanceId}, failed"` |
| 2000 | Information | `Quartz` | `"RAMJobStore initialized."` |
| 2001 | Warning | `Quartz` | `"Skipping trigger {TriggerKey}: its job {JobKey} no longer exists"` |
| 2002 | Warning | `Quartz` | `"Trigger {TriggerKey} references calendar '{CalendarName}', which does not exist - the fire was skipped and the trigger will not run until the calendar is added or the reference is cleared."` |
| 2003 | Debug | `Quartz` | `"Deleting trigger"` |
| 2004 | Debug | `Quartz` | `"Deleting cancelled - trigger still active"` |
| 2005 | Information | `Quartz` | `"Trigger {TriggerKey} set to ERROR state."` |
| 2006 | Information | `Quartz` | `"All triggers of Job {JobKey} set to ERROR state."` |
| 3000 | Information | `Quartz` | `"Detected SQLite usage, changing to use SqliteLockHandler for in-memory locking"` |
| 3001 | Information | `Quartz` | `"With SQLite we need to set AcquireTriggersWithinLock to true, changing"` |
| 3002 | Information | `Quartz` | `"Detected usage of SQLiteDelegate - forcing transaction isolation level to 'Serializable'"` |
| 3003 | Information | `Quartz` | `"With SQLite all operations must be serialized, setting LockAllOperations to true"` |
| 3004 | Warning | `Quartz` | `"The configured transaction isolation level applies only to connections the job store opens itself: an operation running on a connection enlisted by the application uses that transaction's isolation level instead."` |
| 3005 | Information | `Quartz` | `"Detected usage of SqlServerDelegate - defaulting 'selectWithLockSQL' to '{DefaultLockSql}'."` |
| 3006 | Information | `Quartz` | `"Using db table-based data access locking (synchronization) via {LockHandlerType}."` |
| 3007 | Information | `Quartz` | `"Using thread monitor-based data access locking (synchronization) via {LockHandlerType}."` |
| 3008 | Warning | `Quartz` | `"A row-lock statement is configured, but SQLite serializes callers in process rather than by locking a row, so the statement is ignored."` |
| 3009 | Warning | `Quartz` | `"A row-lock statement is configured, but lock handler {LockHandlerType} was supplied rather than built by the job store, so the statement is ignored. Pass it to the handler's constructor, or remove the lock handler and let the store choose one."` |
| 3010 | Warning | `Quartz` | `"Accepting enlisted transactions with SQLite keeps in-process locking: SqliteLockHandler releases the lock when the scheduling call returns, while the application transaction still holds the SQLite writer lock, so a concurrent scheduler operation can fail with 'database is locked' until that transaction completes. Keep enlisted transactions short, or use a database that supports row locking."` |
| 3011 | Warning | `Quartz` | `"Accepting enlisted transactions with lock handler {LockHandlerType}, which does not lock in the database. Its locks are released before the application commits its transaction, so concurrent callers can act on scheduling data that is not visible to them yet."` |
| 3012 | Warning | `Quartz` | `"Detected usage of SqlServerDelegate and UpdateRowLockHandler, removing 'quartz.jobStore.lockHandler.type' would allow more efficient SQL Server specific (UPDLOCK,ROWLOCK) row access"` |
| 3013 | Warning | `Quartz` | `"Detected usage of SQL Server provider without SqlServerDelegate, SqlServerDelegate would provide better performance"` |
| 3014 | Information | `Quartz` | `"Successfully validated presence of {SchemaObjectCount} schema objects"` |
| 3015 | Error | `Quartz` | `"Failure occurred during job recovery: {ExceptionMessage}"` |
| 3016 | Warning | `Quartz` | `"Database connection Shutdown unsuccessful."` |
| 3017 | Error | `Quartz` | `"Error returning lock: {ExceptionMessage}"` |
| 3018 | Information | `Quartz` | `"Freed {Count} triggers from 'acquired' / 'blocked' state."` |
| 3019 | Information | `Quartz` | `"Recovering {Count} jobs that were in-progress at the time of the last shut-down."` |
| 3020 | Information | `Quartz` | `"Recovery complete."` |
| 3021 | Information | `Quartz` | `"Removed {Count} 'complete' triggers."` |
| 3022 | Information | `Quartz` | `"Removed {Count} stale fired job entries."` |
| 3023 | Error | `Quartz` | `"Error recovering stale acquired trigger '{TriggerKey}'"` |
| 3024 | Information | `Quartz` | `"Recovered {RecoveredCount} trigger(s) stuck in ACQUIRED state (stale threshold: {StaleThreshold})"` |
| 3025 | Information | `Quartz` | `"Trigger {TriggerKey} reset from ERROR state to: {NewState}"` |
| 3026 | Error | `Quartz` | `"Error retrieving job, setting trigger state to ERROR."` |
| 3027 | Error | `Quartz` | `"Unable to set trigger state to ERROR."` |
| 3028 | Warning | `Quartz` | `"Trigger {TriggerKey} returned null on nextFireTime and yet still exists in DB!"` |
| 3029 | Error | `Quartz` | `"Caught job persistence exception: {ExceptionMessage}"` |
| 3030 | Error | `Quartz` | `"Caught exception: {ExceptionMessage}"` |
| 3031 | Information | `Quartz` | `"Not firing trigger {TriggerKey} for [DisallowConcurrentExecution] job {JobKey} - already executing on another node."` |
| 3032 | Warning | `Quartz` | `"Trigger {TriggerKey} references calendar '{CalendarName}', which does not exist - the fire was skipped and the trigger will not run until the calendar is added or the reference is cleared."` |
| 3033 | Information | `Quartz` | `"Trigger {Trigger} set to ERROR state."` |
| 3034 | Information | `Quartz` | `"All triggers of Job {Job} set to ERROR state."` |
| 3035 | Information | `Quartz` | `"ConnectionAndTransactionHolder passed to RollbackConnection was null, ignoring"` |
| 3036 | Debug | `Quartz` | `"ConnectionAndTransactionHolder passed to CommitConnection was null, ignoring"` |
| 3037 | Error | `Quartz` | `"RetryExecuteInLocalTransactionLock: RuntimeException {ExceptionMessage}"` |
| 3038 | Warning | `Quartz` | `"Transient exception on attempt {Attempt} of {TotalAttempts} in ExecuteInLocalTransactionLock, will retry after {RetryInterval}"` |
| 3039 | Information | `Quartz` | `"Created the schema objects missing under table prefix '{TablePrefix}'"` |
| 3040 | Debug | `Quartz` | `"Schema creation under table prefix '{TablePrefix}' failed, but the schema validates, so another node created it first"` |
| 3041 | Debug | `Quartz` | `"Schema creation under table prefix '{TablePrefix}' failed on attempt {Attempt} of {Attempts} and the schema does not validate yet, retrying"` |
| 3042 | Warning | `Quartz` | `"Lock handler {LockHandlerType} failed to shut down, so whatever it opened is still open."` |
| 3100 | Debug | `Quartz` | `"Prepared SQL: {Sql}"` |
| 3110 | Error | `Quartz` | `"Unexpected exception closing Connection.  This is often due to a Connection being returned after or during shutdown."` |
| 3111 | Debug | `Quartz` | `"Exception disposing connection or transaction. This is often due to a connection being returned after or during shutdown."` |
| 3112 | Debug | `Quartz` | `"Rollback skipped - transaction is no longer connected, database will have aborted it"` |
| 3113 | Debug | `Quartz` | `"Rollback failed due to transient error"` |
| 3114 | Error | `Quartz` | `"Couldn't rollback ADO.NET connection. {ExceptionMessage}"` |
| 3130 | Information | `Quartz` | `"ExternalTransactionJobStore initialized."` |
| 3131 | Warning | `Quartz` | `"Database connection shutdown unsuccessful."` |
| 3140 | Information | `Quartz` | `"LocalTransactionJobStore initialized."` |
| 3150 | Warning | `Quartz` | `"Couldn't find calendar with name '{CalendarName}'"` |
| 3151 | Debug | `Quartz` | `"Deleting job: {JobKey}"` |
| 3152 | Debug | `Quartz` | `"No job for trigger '{TriggerKey}'"` |
| 3153 | Debug | `Quartz` | `"Adding TriggerPersistenceDelegate of type: {Type}"` |
| 3154 | Warning | `Quartz` | `"Misfired trigger '{TriggerKey}' has no {TriggerType} row and is skipped"` |
| 3155 | Warning | `Quartz` | `"Batched statement execution failed, retrying {StatementCount} statement(s) individually"` |
| 3500 | Warning | `Quartz` | `"Transient exception on attempt {Attempt} of {TotalAttempts} of the cluster check-in, will retry after {RetryInterval}"` |
| 3501 | Warning | `Quartz` | `"This scheduler instance ({InstanceId}) is still active but was recovered by another instance in the cluster.  This may cause inconsistent behavior."` |
| 3502 | Warning | `Quartz` | `"Found orphaned fired triggers for instance: {SchedulerInstanceId}"` |
| 3503 | Warning | `Quartz` | `"ClusterManager: detected {Count} failed or restarted instances."` |
| 3504 | Information | `Quartz` | `"ClusterManager: Scanning for instance {SchedulerInstanceId}'s failed in-progress jobs."` |
| 3505 | Information | `Quartz` | `"ClusterManager: Deferring recovery of [DisallowConcurrentExecution] job {JobKey} (fired trigger {FireInstanceId}) — may still be executing on instance {SchedulerInstanceId}."` |
| 3506 | Warning | `Quartz` | `"ClusterManager: failed job {JobKey} no longer exists, cannot schedule recovery."` |
| 3507 | Warning | `Quartz` | `"ClusterManager: ......Freed {Count} acquired trigger(s)."` |
| 3508 | Warning | `Quartz` | `"ClusterManager: ......Deleted {Count} complete trigger(s)."` |
| 3509 | Warning | `Quartz` | `"ClusterManager: ......Scheduled {Count} recoverable job(s) for recovery."` |
| 3510 | Warning | `Quartz` | `"ClusterManager: ......Cleaned-up {Count} other failed job(s)."` |
| 3511 | Warning | `Quartz` | `"ClusterManager: ......Deferred recovery of {Count} executing [DisallowConcurrentExecution] job(s)."` |
| 3512 | Information | `Quartz` | `"ClusterManager: Released {Count} auto-pinned trigger(s) from dead node '{InstanceId}' for re-acquisition."` |
| 3513 | Debug | `Quartz` | `"Check-in complete."` |
| 3514 | Error | `Quartz` | `"Error managing cluster: {ExceptionMessage}"` |
| 3515 | Warning | `Quartz` | `"Scheduler instance ({InstanceId}) was failed out by instance {RecoveringInstanceId}, the only other instance with a scheduler state row and so the only one that could have. Its own row has been written back; the work that instance recovered is not recovered again here."` |
| 3516 | Warning | `Quartz` | `"Scheduler instance ({InstanceId}) was failed out by another instance, which cannot be identified: {OtherInstanceCount} other instances have scheduler state rows and no row records which of them recovered this one. Its own row has been written back; the work that instance recovered is not recovered again here."` |
| 3600 | Information | `Quartz` | `"Handling the first {Count} triggers that missed their scheduled fire-time. More misfired triggers remain to be processed."` |
| 3601 | Information | `Quartz` | `"Handling {Count} trigger(s) that missed their scheduled fire-time."` |
| 3602 | Debug | `Quartz` | `"Found 0 triggers that missed their scheduled fire-time."` |
| 3603 | Error | `Quartz` | `"Error preparing misfire update for trigger: '{TriggerKey}'"` |
| 3604 | Error | `Quartz` | `"Error updating {Count} misfired trigger(s)"` |
| 3605 | Debug | `Quartz` | `"Found {MisfireCount} triggers that missed their scheduled fire-time."` |
| 3606 | Debug | `Quartz` | `"Scanning for misfires..."` |
| 3607 | Error | `Quartz` | `"Error handling misfires: {ExceptionMessage}"` |
| 3700 | Debug | `Quartz` | `"Lock '{LockName}' is desired by: {RequestorId}"` |
| 3701 | Debug | `Quartz` | `"Lock '{LockName}' given to: {RequestorId}"` |
| 3702 | Debug | `Quartz` | `"Lock '{LockName}' Is already owned by: {RequestorId}"` |
| 3703 | Debug | `Quartz` | `"Lock '{LockName}' returned by: {RequestorId}"` |
| 3704 | Warning | `Quartz` | `"Lock '{LockName}' attempt to return by: {RequestorId} -- but not owner!"` |
| 3705 | Warning | `Quartz` | `"stack-trace of wrongful returner: {Stacktrace}"` |
| 3706 | Debug | `Quartz` | `"Lock '{LockName}' is being obtained: {RequestorId}"` |
| 3707 | Debug | `Quartz` | `"Inserting new lock row for lock: '{LockName}' being obtained by thread: {RequestorId}"` |
| 3708 | Debug | `Quartz` | `"Lock '{LockName}' was not obtained by: {RequestorId}{RetryMessage}"` |
| 3709 | Debug | `Quartz` | `"Lock '{LockName}' already owned by: {RequestorId} -- but not owner!"` |
| 3710 | Debug | `Quartz` | `"stack-trace of wrongful returner: {StackTrace}"` |
| 3711 | Debug | `Quartz` | `"Lock '{LockName}' was not obtained by: {RequestorId}"` |
| 3712 | Debug | `Quartz` | `"Lock '{LockName}' reentrant acquisition by: {RequestorId} (count: {LockCount})"` |
| 3713 | Debug | `Quartz` | `"Lock '{LockName}' reentrant release by: {RequestorId} (remaining: {LockCount})"` |
| 3714 | Debug | `Quartz` | `"Lock '{LockName}' was not obtained by: {RequestorId} - will try again."` |
| 3715 | Debug | `Quartz` | `"Inserting new lock row for lock: '{LockName}' being obtained: {RequestorId}"` |
| 4000 | Debug | `Quartz` | `"The shared-database check could not read what database scheduler '{SchedulerName}' talks to."` |
| 4001 | Warning | `Quartz` | `"Scheduler '{SchedulerName}' (data source '{DataSource}', table prefix '{TablePrefix}') and scheduler '{OtherSchedulerName}' (data source '{OtherDataSource}', table prefix '{OtherTablePrefix}') use the same database with different table prefixes, so neither can see the other's rows. Schedulers sharing a database are normally told apart by SCHED_NAME and share one table prefix; separate table sets are legal, and if that is what you meant this warning is expected. If it is not, the scheduler with the wrong prefix starts cleanly, passes schema validation against the tables it was pointed at, and never sees its own data."` |
| 4002 | Information | `Quartz` | `"Quartz Scheduler {Version} - '{SchedulerName}' with instanceId '{SchedulerInstanceId}' initialized"` |
| 4003 | Information | `Quartz` | `"Using thread pool '{ThreadPoolType}', size: {ThreadPoolSize}"` |
| 4004 | Information | `Quartz` | `"Using job store '{JobStoreType}', supports persistence: {SupportsPersistence}, clustered: {Clustered}"` |
| 4005 | Error | `Quartz` | `"Couldn't generate instance id"` |
| 4006 | Error | `Quartz` | `"Got another exception while shutting down after instantiation exception"` |
| 4007 | Information | `Quartz` | `"Host name '{HostName}' was too long, shortened to '{Newname}'"` |
| 4008 | Warning | `Quartz` | `"Failed to return a job after its creation failed; the original error follows"` |
| 4009 | Warning | `Quartz` | `"{Problem}"` |
| 4010 | Debug | `Quartz` | `"Producing instance of Job '{JobKey}', class={JobFullName}"` |
| 4011 | Debug | `Quartz` | `"TaskSchedulingThreadPool configured with max concurrency of {MaxConcurrency} and TaskScheduler {SchedulerName}."` |
| 4012 | Error | `Quartz` | `"A task handed to the thread pool faulted."` |
| 4013 | Debug | `Quartz` | `"Shutting down threadpool..."` |
| 4014 | Debug | `Quartz` | `"No executing jobs remaining, all threads stopped."` |
| 4015 | Debug | `Quartz` | `"Draining threadpool..."` |
| 4016 | Debug | `Quartz` | `"Gave up waiting for the thread pool to drain; work is still running."` |
| 4017 | Debug | `Quartz` | `"Thread pool closed to new work with {RunningTaskCount} running tasks remaining."` |
| 4018 | Debug | `Quartz` | `"Shutdown of threadpool complete."` |
| 4019 | Debug | `Quartz` | `"Shutdown complete"` |
| 5000 | Information | `Quartz` | `"Parsing XML file: {FileName} with systemId: {SystemId}"` |
| 5001 | Information | `Quartz` | `"Parsing XML from stream with systemId: {SystemId}"` |
| 5002 | Debug | `Quartz` | `"Found {JobGroupCount} delete job group commands."` |
| 5003 | Debug | `Quartz` | `"Found {TriggerGroupDeleteCount} delete trigger group commands."` |
| 5004 | Debug | `Quartz` | `"Found {JobsToDeleteCount} delete job commands."` |
| 5005 | Debug | `Quartz` | `"Found {TriggersToDelete} delete trigger commands."` |
| 5006 | Debug | `Quartz` | `"Directive 'overwrite-existing-data' specified as: {Overwrite}"` |
| 5007 | Debug | `Quartz` | `"Directive 'ignore-duplicates' specified as: {IgnoreDuplicates}"` |
| 5008 | Debug | `Quartz` | `"Directive 'schedule-trigger-relative-to-replaced-trigger' specified as: {ScheduleRelative}"` |
| 5009 | Debug | `Quartz` | `"Directive 'overwrite-existing-data' not specified, defaulting to {Overwrite}"` |
| 5010 | Debug | `Quartz` | `"Directive 'ignore-duplicates' not specified, defaulting to {IgnoreDuplicates}"` |
| 5011 | Debug | `Quartz` | `"Directive 'schedule-trigger-relative-to-replaced-trigger' not specified, defaulting to {ScheduleTriggerRelativeToReplacedTrigger}"` |
| 5012 | Debug | `Quartz` | `"Found {Count} job definitions."` |
| 5013 | Debug | `Quartz` | `"Parsed job definition: {JobDetail}"` |
| 5014 | Debug | `Quartz` | `"Found {TriggerCount} trigger definitions."` |
| 5015 | Debug | `Quartz` | `"Parsed trigger definition: {Trigger}"` |
| 5016 | Warning | `Quartz` | `"Unable to validate XML with schema: {Message}"` |
| 5017 | Warning | `Quartz` | `"{ValidationMessage}"` |
| 5018 | Information | `Quartz` | `"Adding {JobCount} jobs, {TriggerCount} triggers"` |
| 5019 | Information | `Quartz` | `"Removing job: {JobKey}"` |
| 5020 | Information | `Quartz` | `"Not overwriting existing job: {JobKey}"` |
| 5021 | Information | `Quartz` | `"Replacing job: {JobKey}"` |
| 5022 | Information | `Quartz` | `"Adding job: {JobKey}"` |
| 5023 | Debug | `Quartz` | `"Rescheduling job: {JobKey} with updated trigger: {TriggerKey}"` |
| 5024 | Information | `Quartz` | `"Not overwriting existing trigger: {Key}"` |
| 5025 | Debug | `Quartz` | `"Scheduling job: {JobKey} with trigger: {TriggerKey}"` |
| 5026 | Debug | `Quartz` | `"Adding trigger: {TriggerKey} for job: {JobKey} failed because the trigger already existed. This is likely due to a race condition between multiple instances in the cluster. Will try to reschedule instead."` |
| 5028 | Information | `Quartz` | `"Not overwriting existing trigger: {JobKey}"` |
| 5029 | Warning | `Quartz` | `"Possibly duplicately named ({TriggerKey}) trigger in configuration, this can be caused by not having a fixed job key for targeted jobs"` |
| 5030 | Debug | `Quartz` | `"Using relative scheduling for trigger with key {TriggerKey}"` |
| 5031 | Information | `Quartz` | `"Deleting all jobs in ALL groups."` |
| 5032 | Information | `Quartz` | `"Deleting all jobs in group: {Group}"` |
| 5033 | Information | `Quartz` | `"Deleting all triggers in ALL groups."` |
| 5034 | Information | `Quartz` | `"Deleting all triggers in group: {Group}"` |
| 5035 | Information | `Quartz` | `"Deleting job: {Key}"` |
| 5036 | Information | `Quartz` | `"Deleting trigger: {Key}"` |
| 5100 | Warning | `Quartz` | `"Misfire instruction '{MisfireInstruction}' is not one of the {Family} trigger names. It resolves to code {Code}, which for this trigger means {Policy}; spell it '{Canonical}'"` |
| 5102 | Warning | `Quartz` | `"Type '{OldName}' was found as '{NewName}'; the type moved in Quartz 4.0. Update the configuration, as this fallback will not last forever."` |
| 5103 | Warning | `Quartz` | `"Unrecognized misfire policy {MisfireInstruction}. Derived builder will use the default cron trigger behavior (FireOnceNow)"` |
| 5104 | Error | `Quartz` | `"Listener {ListenerName} - method {MethodName} raised an exception: {ExceptionMessage}"` |
| 5105 | Error | `Quartz` | `"Listener method {MethodName} raised an exception: {ExceptionMessage}"` |
| 5106 | Information | `Quartz` | `"Job '{JobKey}' will now chain to Job '{Job}'"` |
| 5107 | Error | `Quartz` | `"Error encountered during chaining to Job '{Job}'"` |
| 5108 | Warning | `Quartz` | `"Unable to resolve file path '{FileName}' due to security exception, probably running under medium trust"` |
| 5109 | Warning | `Quartz` | `"Unable to read environment variable '{Key}' due to security exception, probably running under medium trust"` |
| 5110 | Warning | `Quartz` | `"Unable to read environment variables due to security exception, probably running under medium trust"` |
| 5111 | Debug | `Quartz` | `"Type '{OldName}' was resolved as '{NewName}' through a declared type loader alias."` |
| 6000 | Information | `Quartz.Plugins` | `"{Message}"` |
| 6001 | Information | `Quartz.Plugins` | `"{Message}"` |
| 6002 | Warning | `Quartz.Plugins` | `"{Message}"` |
| 6003 | Information | `Quartz.Plugins` | `"{Message}"` |
| 6010 | Information | `Quartz.Plugins` | `"{Message}"` |
| 6011 | Information | `Quartz.Plugins` | `"{Message}"` |
| 6012 | Information | `Quartz.Plugins` | `"{Message}"` |
| 6200 | Information | `Quartz.Plugins` | `"Registering Quartz Job Initialization Plug-in."` |
| 6201 | Debug | `Quartz.Plugins` | `"Scheduled file scan job for data file: {FileName}, at interval: {ScanInterval}"` |
| 6202 | Error | `Quartz.Plugins` | `"Error starting background-task for watching jobs file."` |
| 6203 | Error | `Quartz.Plugins` | `"Error while notifying SchedulerListener of error"` |
| 6204 | Error | `Quartz.Plugins` | `"Original error while notifying scheduler listeners: {Message}"` |
| 6205 | Error | `Quartz.Plugins` | `"Could not schedule jobs and triggers from file {FileName}: {Message}"` |
| 6206 | Warning | `Quartz.Plugins` | `"File named '{FileName}' does not exist."` |
| 6207 | Warning | `Quartz.Plugins` | `"Error closing jobs file {FileName}"` |
| 6300 | Information | `Quartz.Plugins` | `"Parsing JSON file: {FileName}"` |
| 6301 | Information | `Quartz.Plugins` | `"Deleting all jobs in ALL groups"` |
| 6302 | Information | `Quartz.Plugins` | `"Deleting all jobs in group: {Group}"` |
| 6303 | Information | `Quartz.Plugins` | `"Deleting all triggers in ALL groups"` |
| 6304 | Information | `Quartz.Plugins` | `"Deleting all triggers in group: {Group}"` |
| 6305 | Information | `Quartz.Plugins` | `"Deleting job: {JobKey}"` |
| 6306 | Information | `Quartz.Plugins` | `"Deleting trigger: {TriggerKey}"` |
| 6320 | Information | `Quartz.Plugins` | `"Registering Quartz JSON Job Initialization Plug-in"` |
| 6321 | Debug | `Quartz.Plugins` | `"Scheduled file scan job for data file: {FileName}, at interval: {ScanInterval}"` |
| 6322 | Error | `Quartz.Plugins` | `"Error starting background-task for watching JSON jobs file"` |
| 6323 | Error | `Quartz.Plugins` | `"Could not schedule jobs and triggers from JSON file {FileName}"` |
| 6324 | Error | `Quartz.Plugins` | `"Error while notifying SchedulerListener of error"` |
| 6325 | Warning | `Quartz.Plugins` | `"File named '{FileName}' does not exist"` |
| 6326 | Warning | `Quartz.Plugins` | `"Error closing jobs file {FileName}"` |
| 7000 | Information | `Quartz.Jobs` | `"Directory {DirectoryName} contents updated, notifying listener."` |
| 7001 | Debug | `Quartz.Jobs` | `"Directory '{Directory}' contents unchanged."` |
| 7002 | Warning | `Quartz.Jobs` | `"Directory '{DirectoryName}' does not exist."` |
| 7100 | Warning | `Quartz.Jobs` | `"File '{FileName}' does not exist."` |
| 7101 | Information | `Quartz.Jobs` | `"File '{FileName}' updated, notifying listener."` |
| 7102 | Debug | `Quartz.Jobs` | `"File '{FileName}' unchanged."` |
| 7200 | Information | `Quartz.Jobs` | `"About to run {Command} {Temp}..."` |
| 7201 | Information | `Quartz.Jobs` | `"stdout>{Line}"` |
| 7202 | Warning | `Quartz.Jobs` | `"stderr>{Line}"` |
| 7203 | Error | `Quartz.Jobs` | `"Error consuming {Type} stream of spawned process."` |
| 7300 | Warning | `Quartz.Jobs` | `"SMTP credentials are being read from job data ('{UserNameKey}' / '{PasswordKey}'), which a persistent job store writes to the database and replicates to every node in the cluster. Register an ICredentialsByHost with the container instead."` |
| 7301 | Information | `Quartz.Jobs` | `"Sending message {MailMessage}"` |
| 8000 | Debug | `Quartz.Extensions.Redis` | `"Lock '{LockName}' is desired by: {RequestorId}"` |
| 8001 | Debug | `Quartz.Extensions.Redis` | `"Lock '{LockName}' already owned by: {RequestorId}"` |
| 8002 | Debug | `Quartz.Extensions.Redis` | `"Lock '{LockName}' is being obtained: {RequestorId}"` |
| 8003 | Debug | `Quartz.Extensions.Redis` | `"Lock '{LockName}' was not obtained by: {RequestorId} - cancelled"` |
| 8004 | Debug | `Quartz.Extensions.Redis` | `"Lock '{LockName}' given to: {RequestorId}"` |
| 8005 | Warning | `Quartz.Extensions.Redis` | `"Lock '{LockName}' attempt to return by: {RequestorId} -- but not owner!"` |
| 8006 | Warning | `Quartz.Extensions.Redis` | `"stack-trace of wrongful returner: {StackTrace}"` |
| 8007 | Debug | `Quartz.Extensions.Redis` | `"Lock '{LockName}' returned by: {RequestorId}"` |
| 8008 | Warning | `Quartz.Extensions.Redis` | `"Failed to release Redis lock '{LockName}'"` |
| 8009 | Information | `Quartz.Extensions.Redis` | `"Connecting to Redis"` |
| 8010 | Information | `Quartz.Extensions.Redis` | `"Closing the Redis connection"` |
| 9000 | Debug | `Quartz.AspNetCore` | `"BadHttpRequestException thrown"` |
| 9001 | Debug | `Quartz.AspNetCore` | `"Failed to deserialize request"` |
| 9002 | Debug | `Quartz.AspNetCore` | `"NotFoundException thrown"` |
| 9003 | Warning | `Quartz.AspNetCore` | `"SchedulerException thrown when handling api request to url {Url}"` |
| 9004 | Error | `Quartz.AspNetCore` | `"Exception thrown when handling api request to url {Url}"` |

<!-- endLogEvents -->

## See also

- [Operating a Cluster](operations.md) — what to do about the events that mean something is wrong
- [Observability](packages/opentelemetry-integration.md) — traces and metrics, which answer the questions
  a log cannot
- [Troubleshooting](../troubleshooting.md) — the symptoms these events show up in
