--
-- Quartz.NET schema migration -- add the execution outcome columns and QRTZ_JOB_STATUS
--
-- Introduced in Quartz.NET 4.4.0 (#3958)
--
-- Oracle only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.4  OPTIONAL, and only for a database that has QRTZ_EXECUTION_HISTORY -- one that
--        ran ../4.2/add_execution_history_oracle.sql or was created by 4.2 or later.
--        A store configured with UsePersistentStore(s => s.UseExecutionHistory()) writes
--        these columns with every history row and keeps QRTZ_JOB_STATUS, so it refuses to
--        start without them. No other scheduler reads either table.
--
--        Do NOT run it against a database without QRTZ_EXECUTION_HISTORY: the first
--        statement alters that table, and fails when the table is not there.
--
--        A 4.2 database also needs ../4.3/add_execution_log_oracle.sql and
--        ../4.3/add_misfire_reason_oracle.sql.
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
-- Oracle only: SUMMARY, LAST_SUMMARY and LAST_FAILURE_MESSAGE are VARCHAR2(4000), four
-- times the 1,000 characters they hold, because VARCHAR2 counts bytes.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === 1. The outcome columns on QRTZ_EXECUTION_HISTORY ===

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_EXECUTION_HISTORY' AND column_name = 'RESULT';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD (RESULT NUMBER(13) NULL)';
  END IF;
END;
/

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_EXECUTION_HISTORY' AND column_name = 'SUMMARY';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD (SUMMARY VARCHAR2(4000) NULL)';
  END IF;
END;
/

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_EXECUTION_HISTORY' AND column_name = 'METRICS';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD (METRICS CLOB NULL)';
  END IF;
END;
/

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_EXECUTION_HISTORY' AND column_name = 'MANUAL';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD (MANUAL VARCHAR2(1) NULL)';
  END IF;
END;
/

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_EXECUTION_HISTORY' AND column_name = 'FIRE_INSTANCE_ID';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD (FIRE_INSTANCE_ID VARCHAR2(140) NULL)';
  END IF;
END;
/

-- === 2. The index a job's own history reads ===

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_EH_JOB_TIME';
  IF index_exists = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IDX_QRTZ_EH_JOB_TIME ON QRTZ_EXECUTION_HISTORY(SCHED_NAME,JOB_GROUP,JOB_NAME,FIRED_TIME)';
  END IF;
END;
/

-- === 3. QRTZ_JOB_STATUS ===

DECLARE
  table_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO table_exists FROM user_tables
  WHERE table_name = 'QRTZ_JOB_STATUS';
  IF table_exists = 0 THEN
    EXECUTE IMMEDIATE 'CREATE TABLE QRTZ_JOB_STATUS (SCHED_NAME VARCHAR2(120) NOT NULL, JOB_GROUP VARCHAR2(200) NOT NULL, JOB_NAME VARCHAR2(200) NOT NULL, FIRST_FIRED_TIME NUMBER(19) NOT NULL, LAST_FIRED_TIME NUMBER(19) NOT NULL, LAST_RESULT NUMBER(13) NOT NULL, LAST_RUN_TIME NUMBER(19) NOT NULL, LAST_INSTANCE_NAME VARCHAR2(200) NOT NULL, LAST_ENTRY_ID VARCHAR2(140) NULL, LAST_SUMMARY VARCHAR2(4000) NULL, LAST_SUCCESS_TIME NUMBER(19) NULL, LAST_FAILURE_TIME NUMBER(19) NULL, LAST_FAILURE_MESSAGE VARCHAR2(4000) NULL, CONSECUTIVE_FAILURES NUMBER(13) DEFAULT 0 NOT NULL, RUN_COUNT NUMBER(19) DEFAULT 0 NOT NULL, FAILURE_COUNT NUMBER(19) DEFAULT 0 NOT NULL, CONSTRAINT QRTZ_JOB_STATUS_PK PRIMARY KEY (SCHED_NAME,JOB_GROUP,JOB_NAME))';
  END IF;
END;
/
