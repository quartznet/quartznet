--
-- Quartz.NET schema migration -- add QRTZ_TRIGGER_STATUS
--
-- Introduced in Quartz.NET 4.9.0 (#4243)
--
-- MySQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.9  OPTIONAL. Only UsePersistentStore(s => s.UseExecutionHistory()) reads or writes
--        it, and a store configured so refuses to start without it. No other scheduler
--        touches it.
--
--        Safe under a mixed cluster: a 4.8 node never touches it, so it counts only 4.9
--        nodes' firings until every node is 4.9.
--
--   4.4  A database without QRTZ_JOB_STATUS also needs
--        ../4.4/add_execution_outcome_mysql_innodb.sql to keep its history in the database.
--
--   3.x  Not applicable.
--
-- QRTZ_TRIGGER_STATUS keeps one row per trigger, with QRTZ_JOB_STATUS's columns: its first
-- and last firing, the last run's result, summary, run time and node, when it last
-- succeeded and last failed, and three counters. The store writes it with each history
-- row, in the same transaction, after the job's row, from every firing of the trigger's
-- own key but one after which the trigger is finalized: a one-off, the last firing of a
-- series, a manual run, a backfill and a recovery leave no row.
--
-- It has no foreign key: a 4.8 node deleting a trigger must not trip on a row it has
-- never heard of. The history's sweep deletes the row of a trigger that is gone once its
-- last run is older than the longest retention. The primary key is its only index.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

CREATE TABLE IF NOT EXISTS QRTZ_TRIGGER_STATUS (
  SCHED_NAME VARCHAR(120) NOT NULL,
  TRIGGER_GROUP VARCHAR(200) NOT NULL,
  TRIGGER_NAME VARCHAR(200) NOT NULL,
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
  PRIMARY KEY (SCHED_NAME,TRIGGER_GROUP,TRIGGER_NAME)
) ENGINE=InnoDB;
