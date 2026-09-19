--
-- Quartz.NET schema migration -- add the continuation columns
--
-- Introduced in Quartz.NET 4.2.0 (#3805)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.2  REQUIRED. A 4.2 node reads and writes these three columns on every trigger
--        it stores, so it refuses to start against a database without them.
--
--        Safe to run while 4.0 and 4.1 nodes are still up: the columns are nullable
--        with no default, so every existing row is already valid, and those nodes
--        never read them. What a 4.0 or 4.1 node cannot do is settle a continuation,
--        so migrate, roll every node, and only then start scheduling them.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_postgres.sql first; this file is what 4.2 adds
--        on top of it.
--
-- A continuation is a trigger that waits, in the store, for another trigger's
-- firing to end. It is held in the new TRIGGER_STATE value 'AWAITING' and is never
-- acquired while it is there; the parent's completion settles it inside the
-- parent's own transaction, so whichever node ran the parent is the node that
-- releases it and a crash cannot lose one.
--
-- CONTINUES_TRIGGER_NAME and CONTINUES_TRIGGER_GROUP name the trigger waited for;
-- they carry the same declaration as TRIGGER_NAME and TRIGGER_GROUP, made
-- nullable, because what they hold is a trigger key.
--
-- CONTINUATION_CONDITION is the integer of the ContinuationCondition flags that
-- release the wait: 1 OnSuccess, 2 OnFailure, 4 OnCancellation, 8 OnVeto, and
-- 15 OnAnyOutcome. A row that names a parent but has no condition reads as 15.
--
-- No index is added. The settlement lookup filters SCHED_NAME and TRIGGER_STATE by
-- equality, which is what IDX_QRTZ_T_NFT_ST already leads with, and 'AWAITING' is a
-- small partition even of a schedule that uses continuations heavily. Measure
-- before adding one.
--
-- ALL THREE COLUMNS MUST BE ADDED TOGETHER.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'continues_trigger_name') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN continues_trigger_name text null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'continues_trigger_group') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN continues_trigger_group text null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'continuation_condition') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN continuation_condition integer null;
  END IF;
END $$;
