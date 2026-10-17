--
-- Quartz.NET schema migration -- add the execution history lookup indexes
--
-- Introduced in Quartz.NET 4.6.0 (#4072)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.6  OPTIONAL, performance only. A store configured with
--        UsePersistentStore(s => s.UseExecutionHistory()) runs without them; they turn a
--        read of one firing's outcome, or of one trigger's history, from a scan into a seek.
--
--        Safe on a database without QRTZ_EXECUTION_HISTORY: each statement checks for the
--        table, and for the index, before it creates anything, and changes nothing there.
--
--        A history created before 4.4 needs ../4.4/add_execution_outcome_postgres.sql first:
--        FIRE_INSTANCE_ID is one of its columns.
--
--        Safe under a mixed cluster: an index changes a statement's plan, never its result.
--
--   3.x  Not applicable.
--
-- IDX_QRTZ_EH_FIRE_INST is (SCHED_NAME, FIRE_INSTANCE_ID): the read a caller holding a
-- firing's id makes for its outcome, ExecutionHistoryQuery.FireInstanceId. Without it that
-- read scans every row of the scheduler the age bound keeps.
--
-- IDX_QRTZ_EH_TRIG_TIME is (SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME, FIRED_TIME), the twin of
-- IDX_QRTZ_EH_JOB_TIME: ExecutionHistoryQuery.Trigger, read newest first. Without it a page
-- of a trigger that fires rarely reads the scheduler's rows until it is full.
--
-- The history is bounded by ExecutionHistoryOptions, so both build in moments. Each costs one
-- more index write per recorded run.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === The indexes a lookup by firing and by trigger read ===

DO $$
BEGIN
  IF to_regclass('qrtz_execution_history') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS idx_qrtz_eh_fire_inst ON qrtz_execution_history (sched_name, fire_instance_id);
  END IF;
END $$;

DO $$
BEGIN
  IF to_regclass('qrtz_execution_history') IS NOT NULL THEN
    CREATE INDEX IF NOT EXISTS idx_qrtz_eh_trig_time ON qrtz_execution_history (sched_name, trigger_group, trigger_name, fired_time);
  END IF;
END $$;
