--
-- Quartz.NET schema migration -- add the execution outcome columns and QRTZ_JOB_STATUS
--
-- Introduced in Quartz.NET 4.4.0 (#3958)
--
-- SQL Server only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.4  OPTIONAL, and only for a database that has QRTZ_EXECUTION_HISTORY -- one that
--        ran ../4.2/add_execution_history_sqlServer.sql or was created by 4.2 or later.
--        A store configured with UsePersistentStore(s => s.UseExecutionHistory()) writes
--        these columns with every history row and keeps QRTZ_JOB_STATUS, so it refuses to
--        start without them. No other scheduler reads either table.
--
--        Do NOT run it against a database without QRTZ_EXECUTION_HISTORY: the first
--        statement alters that table, and fails when the table is not there.
--
--        A 4.2 database also needs ../4.3/add_execution_log_sqlServer.sql and
--        ../4.3/add_misfire_reason_sqlServer.sql.
--
--        Safe under a mixed cluster: a 4.3 node's history rows name their own columns and
--        leave these NULL, and a 4.3 node never touches QRTZ_JOB_STATUS. The rollup counts
--        only what 4.4 nodes ran until every node is 4.4.
--
--   3.x  Not applicable.
--
-- On QRTZ_EXECUTION_HISTORY, what the run reported through IJobExecutionContext.Result:
--   RESULT            the integer of JobRunResult
--   SUMMARY           one line, cut to 1,000 characters as ERROR_MESSAGE is
--   METRICS           the reported values, as JSON
--   MANUAL            whether IScheduler.TriggerJob fired it
--   FIRE_INSTANCE_ID  the firing's id, linking the row to its span and log scope
-- and what the run was given, kept only with ExecutionHistoryOptions.RecordInput:
--   JOB_INPUT            the run's input, the string stored under QRTZ_JOB_INPUT
--   JOB_INPUT_TOO_LARGE  whether the input was over MaxInputBytes, and so not kept
-- All seven are nullable with no default. A row a 4.3 node wrote leaves them NULL, and its
-- outcome is read from SUCCEEDED as before.
--
-- FIRE_INSTANCE_ID is neither unique nor indexed. A fire instance id is not durable across
-- a restart, and the history write is never retried, so there is nothing to deduplicate.
--
-- IDX_QRTZ_EH_JOB_TIME serves a read of one job's history by time. The table is bounded
-- by ExecutionHistoryOptions, so the index builds in moments.
--
-- QRTZ_JOB_STATUS keeps one row per job: its first and last firing, the last run's result,
-- summary, run time and node, when it last succeeded and last failed, and three counters.
-- The store writes it with each history row, so it outlives the history's retention. It
-- has no foreign key: a 4.3 node deleting a job must not trip on a row it has never heard
-- of.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === 1. The outcome columns on QRTZ_EXECUTION_HISTORY ===

IF COL_LENGTH('QRTZ_EXECUTION_HISTORY','RESULT') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_EXECUTION_HISTORY] ADD [RESULT] int NULL;
END
GO

IF COL_LENGTH('QRTZ_EXECUTION_HISTORY','SUMMARY') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_EXECUTION_HISTORY] ADD [SUMMARY] nvarchar(1000) NULL;
END
GO

IF COL_LENGTH('QRTZ_EXECUTION_HISTORY','METRICS') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_EXECUTION_HISTORY] ADD [METRICS] nvarchar(max) NULL;
END
GO

IF COL_LENGTH('QRTZ_EXECUTION_HISTORY','MANUAL') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_EXECUTION_HISTORY] ADD [MANUAL] bit NULL;
END
GO

IF COL_LENGTH('QRTZ_EXECUTION_HISTORY','FIRE_INSTANCE_ID') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_EXECUTION_HISTORY] ADD [FIRE_INSTANCE_ID] nvarchar(140) NULL;
END
GO

IF COL_LENGTH('QRTZ_EXECUTION_HISTORY','JOB_INPUT') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_EXECUTION_HISTORY] ADD [JOB_INPUT] nvarchar(max) NULL;
END
GO

IF COL_LENGTH('QRTZ_EXECUTION_HISTORY','JOB_INPUT_TOO_LARGE') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_EXECUTION_HISTORY] ADD [JOB_INPUT_TOO_LARGE] bit NULL;
END
GO

-- === 2. The index a job's own history reads ===

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_EH_JOB_TIME' AND object_id = OBJECT_ID('dbo.QRTZ_EXECUTION_HISTORY'))
BEGIN
  CREATE INDEX [IDX_QRTZ_EH_JOB_TIME] ON [dbo].[QRTZ_EXECUTION_HISTORY](SCHED_NAME, JOB_GROUP, JOB_NAME, FIRED_TIME);
END
GO

-- === 3. QRTZ_JOB_STATUS ===

IF OBJECT_ID(N'[dbo].[QRTZ_JOB_STATUS]', N'U') IS NULL
BEGIN
  CREATE TABLE [dbo].[QRTZ_JOB_STATUS] (
    SCHED_NAME nvarchar(120) NOT NULL,
    JOB_GROUP nvarchar(150) NOT NULL,
    JOB_NAME nvarchar(150) NOT NULL,
    FIRST_FIRED_TIME bigint NOT NULL,
    LAST_FIRED_TIME bigint NOT NULL,
    LAST_RESULT int NOT NULL,
    LAST_RUN_TIME bigint NOT NULL,
    LAST_INSTANCE_NAME nvarchar(200) NOT NULL,
    LAST_ENTRY_ID nvarchar(140) NULL,
    LAST_SUMMARY nvarchar(1000) NULL,
    LAST_SUCCESS_TIME bigint NULL,
    LAST_FAILURE_TIME bigint NULL,
    LAST_FAILURE_MESSAGE nvarchar(1000) NULL,
    CONSECUTIVE_FAILURES int NOT NULL DEFAULT 0,
    RUN_COUNT bigint NOT NULL DEFAULT 0,
    FAILURE_COUNT bigint NOT NULL DEFAULT 0,
    CONSTRAINT PK_QRTZ_JOB_STATUS PRIMARY KEY (SCHED_NAME,JOB_GROUP,JOB_NAME)
  );
END
GO
