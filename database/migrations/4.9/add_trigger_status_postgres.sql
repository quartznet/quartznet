--
-- Quartz.NET schema migration -- add QRTZ_TRIGGER_STATUS
--
-- Introduced in Quartz.NET 4.9.0 (#4243)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
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
--        ../4.4/add_execution_outcome_postgres.sql to keep its history in the database.
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

CREATE TABLE IF NOT EXISTS qrtz_trigger_status (
  sched_name text not null,
  trigger_group text not null,
  trigger_name text not null,
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
  primary key (sched_name,trigger_group,trigger_name)
);
