--
-- Quartz.NET schema migration -- add the execution outcome columns and QRTZ_JOB_STATUS
--
-- Introduced in Quartz.NET 4.4.0 (#3958)
--
-- MySQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.4  OPTIONAL, and only for a database that has QRTZ_EXECUTION_HISTORY -- one that
--        ran ../4.2/add_execution_history_mysql_innodb.sql or was created by 4.2 or later.
--        A store configured with UsePersistentStore(s => s.UseExecutionHistory()) writes
--        these columns with every history row and keeps QRTZ_JOB_STATUS, so it refuses to
--        start without them. No other scheduler reads either table.
--
--        Do NOT run it against a database without QRTZ_EXECUTION_HISTORY: the first
--        statement alters that table, and fails when the table is not there.
--
--        A 4.2 database also needs ../4.3/add_execution_log_mysql_innodb.sql and
--        ../4.3/add_misfire_reason_mysql_innodb.sql.
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

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_EXECUTION_HISTORY' AND COLUMN_NAME = 'RESULT') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN RESULT INTEGER NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_EXECUTION_HISTORY' AND COLUMN_NAME = 'SUMMARY') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN SUMMARY VARCHAR(1000) NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_EXECUTION_HISTORY' AND COLUMN_NAME = 'METRICS') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN METRICS LONGTEXT NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_EXECUTION_HISTORY' AND COLUMN_NAME = 'MANUAL') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN MANUAL BOOLEAN NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_EXECUTION_HISTORY' AND COLUMN_NAME = 'FIRE_INSTANCE_ID') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN FIRE_INSTANCE_ID VARCHAR(140) NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_EXECUTION_HISTORY' AND COLUMN_NAME = 'JOB_INPUT') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN JOB_INPUT LONGTEXT NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_EXECUTION_HISTORY' AND COLUMN_NAME = 'JOB_INPUT_TOO_LARGE') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN JOB_INPUT_TOO_LARGE BOOLEAN NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

-- === 2. The index a job's own history reads ===

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_EXECUTION_HISTORY' AND INDEX_NAME = 'IDX_QRTZ_EH_JOB_TIME') > 0,
  'SELECT 1',
  'CREATE INDEX IDX_QRTZ_EH_JOB_TIME ON QRTZ_EXECUTION_HISTORY(SCHED_NAME,JOB_GROUP,JOB_NAME,FIRED_TIME)'
));
PREPARE stmt FROM @preparedStatement;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- === 3. QRTZ_JOB_STATUS ===

CREATE TABLE IF NOT EXISTS QRTZ_JOB_STATUS (
  SCHED_NAME VARCHAR(120) NOT NULL,
  JOB_GROUP VARCHAR(200) NOT NULL,
  JOB_NAME VARCHAR(200) NOT NULL,
  FIRST_FIRED_TIME BIGINT NOT NULL,
  LAST_FIRED_TIME BIGINT NOT NULL,
  LAST_RESULT INTEGER NOT NULL,
  LAST_RUN_TIME BIGINT NOT NULL,
  LAST_INSTANCE_NAME VARCHAR(200) NOT NULL,
  LAST_ENTRY_ID VARCHAR(140) NULL,
  LAST_SUMMARY VARCHAR(1000) NULL,
  LAST_SUCCESS_TIME BIGINT NULL,
  LAST_FAILURE_TIME BIGINT NULL,
  LAST_FAILURE_MESSAGE VARCHAR(1000) NULL,
  CONSECUTIVE_FAILURES INTEGER NOT NULL DEFAULT 0,
  RUN_COUNT BIGINT NOT NULL DEFAULT 0,
  FAILURE_COUNT BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (SCHED_NAME,JOB_GROUP,JOB_NAME)
) ENGINE=InnoDB;
