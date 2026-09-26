--
-- Quartz.NET schema migration -- add the execution log column
--
-- Introduced in Quartz.NET 4.3.0 (#3874)
--
-- SQL Server only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.3  OPTIONAL, and only for a database that has QRTZ_EXECUTION_HISTORY -- one that
--        ran ../4.2/add_execution_history_sqlServer.sql or was created by 4.2 or later.
--        A store configured with UsePersistentStore(s => s.UseExecutionHistory()) writes
--        this column with every history row, so it refuses to start without it. No other
--        scheduler reads that table.
--
--        Do NOT run it against a database without QRTZ_EXECUTION_HISTORY: the statement
--        alters that table, and fails when the table is not there.
--
--        Safe under a mixed cluster: a 4.2 node's history rows name their own columns and
--        leave this one NULL, which reads as nothing captured.
--
--   3.x  Not applicable.
--
-- EXECUTION_LOG holds the log lines a job wrote while it ran, captured by
-- UseExecutionLogCapture() and bounded by ExecutionLogCaptureOptions -- 200 lines and
-- 16 KB by default. It is NULL for an execution that logged nothing and for every
-- execution of a scheduler that does not capture.
--
-- The history listing never selects it; only the read of one entry does, so a page of
-- history costs what it did before.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

IF COL_LENGTH('QRTZ_EXECUTION_HISTORY','EXECUTION_LOG') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_EXECUTION_HISTORY] ADD [EXECUTION_LOG] nvarchar(max) NULL;
END
GO
