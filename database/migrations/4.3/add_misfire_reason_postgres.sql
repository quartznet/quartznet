--
-- Quartz.NET schema migration -- add the misfire reason column
--
-- Introduced in Quartz.NET 4.3.0 (#3875)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.3  OPTIONAL, and only for a database that has QRTZ_MISFIRE_HISTORY -- one that
--        ran ../4.2/add_execution_history_postgres.sql or was created by 4.2 or later.
--        A store configured with UsePersistentStore(s => s.UseExecutionHistory()) writes
--        this column with every misfire row, so it refuses to start without it. No other
--        scheduler reads that table.
--
--        Do NOT run it against a database without QRTZ_MISFIRE_HISTORY: the statement
--        alters that table, and fails when the table is not there.
--
--        Safe under a mixed cluster: a 4.2 node's misfire rows name their own columns and
--        leave this one NULL, which reads as a misfire.
--
--   3.x  Not applicable.
--
-- REASON is the integer of MisfireReason: NULL and 0 are Missed -- the scheduler could
-- not fire the trigger in time and applied its misfire instruction -- and 1 is Overlap:
-- the trigger's OverlapPolicy.Skip dropped the firing because the previous one was still
-- running.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_misfire_history' AND column_name = 'reason') THEN
    ALTER TABLE qrtz_misfire_history ADD COLUMN reason integer null;
  END IF;
END $$;
