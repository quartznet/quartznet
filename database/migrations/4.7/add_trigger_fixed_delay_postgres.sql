--
-- Quartz.NET schema migration -- add the trigger fixed-delay column
--
-- Introduced in Quartz.NET 4.7.0 (#4140)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.7  REQUIRED. A 4.7 node reads and writes this column on every trigger it stores,
--        and its firing rounds read it, so it refuses to start against a database
--        without it.
--
--        Safe to run while 4.6 nodes are still up: the column is nullable with no default,
--        so every existing row is already valid, and a 4.6 node never names it. What a
--        4.6 node cannot do is measure from completion -- it fires such a trigger at a
--        fixed rate, beside a running firing of it -- so migrate, roll every node, and only
--        then make a trigger measure from completion.
--
--   4.1  Run ../4.2/add_continuations_postgres.sql first on a database created by
--        4.0 or 4.1, and every later required migration.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_postgres.sql and every later migration first;
--        this file is what 4.7 adds on top of them.
--
-- FIXED_DELAY says a simple trigger measures its interval from the previous run's
-- completion rather than from its scheduled time. NULL is a fixed rate, which is what
-- every row a node older than 4.7 writes reads as. A 4.6 node that updates a row
-- leaves the column as it found it, because it never names it.
--
-- No index is added. Acquisition reads it as a residual beside NEXT_FIRE_TIME, and
-- nothing searches on it.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'fixed_delay') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN fixed_delay bool null;
  END IF;
END $$;
