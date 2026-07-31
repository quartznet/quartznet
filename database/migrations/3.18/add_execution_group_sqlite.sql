--
-- Quartz.NET schema migration -- add EXECUTION_GROUP
--
-- Introduced in Quartz.NET 3.18.0 (#3004)
--
-- SQLite only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   3.x  OPTIONAL. Without it execution groups still work, but the per-node limit is
--        applied by in-memory filtering after acquisition rather than in the acquire
--        query. The job store probes at startup.
--
--   4.x  REQUIRED. 4.x removed the startup probe and assumes the column exists. When
--        upgrading from 3.x run ../4.0/schema_30_to_40_upgrade_sqlite.sql instead -- it
--        folds this change in.
--
-- Carries the execution group tag that per-node thread limits are enforced against.
-- Both tables must be altered together.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- NOT IDEMPOTENT: SQLite has no conditional DDL, so re-running this fails with a
-- duplicate-column error. Check PRAGMA table_info(<table>) before applying.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

ALTER TABLE QRTZ_TRIGGERS ADD COLUMN EXECUTION_GROUP NVARCHAR(200) NULL;

ALTER TABLE QRTZ_FIRED_TRIGGERS ADD COLUMN EXECUTION_GROUP NVARCHAR(200) NULL;
