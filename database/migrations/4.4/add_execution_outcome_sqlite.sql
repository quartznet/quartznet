--
-- Quartz.NET schema migration -- add the execution outcome columns and QRTZ_JOB_STATUS
--
-- Introduced in Quartz.NET 4.4.0 (#3958)
--
-- SQLite only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.4  OPTIONAL, and only for a database that has QRTZ_EXECUTION_HISTORY -- one that
--        ran ../4.2/add_execution_history_sqlite.sql or was created by 4.2 or later.
--        A store configured with UsePersistentStore(s => s.UseExecutionHistory()) writes
--        these columns with every history row and keeps QRTZ_JOB_STATUS, so it refuses to
--        start without them. No other scheduler reads either table.
--
--        Do NOT run it against a database without QRTZ_EXECUTION_HISTORY: the first
--        statement alters that table, and fails when the table is not there.
--
--        A 4.2 database also needs ../4.3/add_execution_log_sqlite.sql and
--        ../4.3/add_misfire_reason_sqlite.sql.
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
-- All five are nullable with no default. A row a 4.3 node wrote leaves them NULL, and its
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
-- NOT IDEMPOTENT: SQLite has no conditional DDL, so re-running this fails with a
-- duplicate-column error. Check PRAGMA table_info(<table>) before applying.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === 1. The outcome columns on QRTZ_EXECUTION_HISTORY ===

ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN RESULT INTEGER NULL;

ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN SUMMARY NVARCHAR(1000) NULL;

ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN METRICS TEXT NULL;

ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN MANUAL BIT NULL;

ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN FIRE_INSTANCE_ID NVARCHAR(140) NULL;

-- === 2. The index a job's own history reads ===

CREATE INDEX IF NOT EXISTS IDX_QRTZ_EH_JOB_TIME ON QRTZ_EXECUTION_HISTORY(SCHED_NAME,JOB_GROUP,JOB_NAME,FIRED_TIME);

-- === 3. QRTZ_JOB_STATUS ===

CREATE TABLE IF NOT EXISTS QRTZ_JOB_STATUS (
  SCHED_NAME NVARCHAR(120) NOT NULL,
  JOB_GROUP NVARCHAR(150) NOT NULL,
  JOB_NAME NVARCHAR(150) NOT NULL,
  FIRST_FIRED_TIME BIGINT NOT NULL,
  LAST_FIRED_TIME BIGINT NOT NULL,
  LAST_RESULT INTEGER NOT NULL,
  LAST_RUN_TIME BIGINT NOT NULL,
  LAST_INSTANCE_NAME NVARCHAR(200) NOT NULL,
  LAST_ENTRY_ID NVARCHAR(140) NULL,
  LAST_SUMMARY NVARCHAR(1000) NULL,
  LAST_SUCCESS_TIME BIGINT NULL,
  LAST_FAILURE_TIME BIGINT NULL,
  LAST_FAILURE_MESSAGE NVARCHAR(1000) NULL,
  CONSECUTIVE_FAILURES INTEGER NOT NULL DEFAULT 0,
  RUN_COUNT BIGINT NOT NULL DEFAULT 0,
  FAILURE_COUNT BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (SCHED_NAME,JOB_GROUP,JOB_NAME)
);
