--
-- Quartz.NET schema migration -- add the execution outcome columns and QRTZ_JOB_STATUS
--
-- Introduced in Quartz.NET 4.4.0 (#3958)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.4  OPTIONAL, and only for a database that has QRTZ_EXECUTION_HISTORY -- one that
--        ran ../4.2/add_execution_history_postgres.sql or was created by 4.2 or later.
--        A store configured with UsePersistentStore(s => s.UseExecutionHistory()) writes
--        these columns with every history row and keeps QRTZ_JOB_STATUS, so it refuses to
--        start without them. No other scheduler reads either table.
--
--        Do NOT run it against a database without QRTZ_EXECUTION_HISTORY: the first
--        statement alters that table, and fails when the table is not there.
--
--        A 4.2 database also needs ../4.3/add_execution_log_postgres.sql and
--        ../4.3/add_misfire_reason_postgres.sql.
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
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === 1. The outcome columns on QRTZ_EXECUTION_HISTORY ===

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_execution_history' AND column_name = 'result') THEN
    ALTER TABLE qrtz_execution_history ADD COLUMN result integer null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_execution_history' AND column_name = 'summary') THEN
    ALTER TABLE qrtz_execution_history ADD COLUMN summary text null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_execution_history' AND column_name = 'metrics') THEN
    ALTER TABLE qrtz_execution_history ADD COLUMN metrics text null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_execution_history' AND column_name = 'manual') THEN
    ALTER TABLE qrtz_execution_history ADD COLUMN manual bool null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_execution_history' AND column_name = 'fire_instance_id') THEN
    ALTER TABLE qrtz_execution_history ADD COLUMN fire_instance_id text null;
  END IF;
END $$;

-- === 2. The index a job's own history reads ===

CREATE INDEX IF NOT EXISTS idx_qrtz_eh_job_time ON qrtz_execution_history (sched_name, job_group, job_name, fired_time);

-- === 3. QRTZ_JOB_STATUS ===

CREATE TABLE IF NOT EXISTS qrtz_job_status (
  sched_name text not null,
  job_group text not null,
  job_name text not null,
  first_fired_time bigint not null,
  last_fired_time bigint not null,
  last_result integer not null,
  last_run_time bigint not null,
  last_instance_name text not null,
  last_entry_id text null,
  last_summary text null,
  last_success_time bigint null,
  last_failure_time bigint null,
  last_failure_message text null,
  consecutive_failures integer not null default 0,
  run_count bigint not null default 0,
  failure_count bigint not null default 0,
  primary key (sched_name,job_group,job_name)
);
