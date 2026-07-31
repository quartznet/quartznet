--
-- Quartz.NET schema migration -- add PREFERRED_NODE and PREFERRED_NODE_AUTO
--
-- Introduced in Quartz.NET 3.19.0 (#3013, #3144)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   3.x  OPTIONAL. Without the columns node affinity is unavailable; the scheduler
--        logs a warning at startup and otherwise behaves exactly as before 3.19.
--
--   4.x  REQUIRED. 4.x removed the startup probe and assumes the column exists. When
--        upgrading from 3.x run ../4.0/schema_30_to_40_upgrade_postgres.sql instead -- it
--        folds this change in.
--
-- These back node affinity (pinning a trigger to a preferred cluster node).
--
-- PREFERRED_NODE holds the target node's instance id verbatim, or the "*" sentinel
-- requesting auto-pin. PREFERRED_NODE_AUTO records whether that pin was claimed
-- automatically by the node that first fired the trigger -- auto-claimed pins are
-- released back to "*" when their node dies, explicit pins are preserved.
--
-- BOTH COLUMNS MUST BE ADDED TOGETHER. Quartz probes for both and only enables node
-- affinity when both are present, so adding just one leaves the feature off.
--
-- The 3.x and 4.x representations are identical, so no data migration is needed.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'preferred_node') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN preferred_node varchar(200) null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'preferred_node_auto') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN preferred_node_auto bool not null default false;
  END IF;
END $$;
