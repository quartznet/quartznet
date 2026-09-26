--
-- Quartz.NET schema migration -- add the fire progress columns
--
-- Introduced in Quartz.NET 4.3.0 (#3874)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.3  REQUIRED. A 4.3 node writes these two columns when a running job reports its
--        progress and reads them whenever it lists what is executing, so it refuses to
--        start against a database without them.
--
--        Safe to run while 4.2 nodes are still up: the columns are nullable with no
--        default, so every existing row is already valid, and a 4.2 node never names them.
--        A firing a 4.2 node runs reads as one that has reported no progress.
--
--   4.1  Run ../4.2/add_continuations_postgres.sql first on a database created by
--        4.0 or 4.1.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_postgres.sql and every later migration first;
--        this file is what 4.3 adds on top of them.
--
-- PROGRESS is the percentage, 0 to 100, a running job last passed to
-- IJobExecutionContext.ReportProgress; PROGRESS_MESSAGE is the message it passed with it,
-- truncated to 250 characters. The scheduler writes them at most once a second per firing
-- and only when they change, by ENTRY_ID -- the row belongs to the node writing it, so no
-- lock is taken. The row is deleted when the firing completes, as it always was.
--
-- No index is added. The write is by primary key, and the read is the fire-instance listing,
-- which already reads this table.
--
-- BOTH COLUMNS MUST BE ADDED TOGETHER.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_fired_triggers' AND column_name = 'progress') THEN
    ALTER TABLE qrtz_fired_triggers ADD COLUMN progress integer null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_fired_triggers' AND column_name = 'progress_message') THEN
    ALTER TABLE qrtz_fired_triggers ADD COLUMN progress_message varchar(250) null;
  END IF;
END $$;
