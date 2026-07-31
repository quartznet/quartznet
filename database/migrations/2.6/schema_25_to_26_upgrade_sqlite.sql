--
-- Quartz.NET schema migration -- 2.5 to 2.6
--
-- SQLite only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   REQUIRED when upgrading from 2.5 or earlier to 2.6 or later with AdoJobStore.
--
-- Adds TIME_ZONE_ID to QRTZ_SIMPROP_TRIGGERS and QRTZ_CRON_TRIGGERS so a trigger's
-- time zone survives a restart (#136). Both tables need it (#1985).
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- NOT IDEMPOTENT: SQLite has no conditional DDL, so re-running this fails with a
-- duplicate-column error. Check PRAGMA table_info(<table>) before applying.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

ALTER TABLE QRTZ_SIMPROP_TRIGGERS ADD COLUMN TIME_ZONE_ID NVARCHAR(80) NULL;

ALTER TABLE QRTZ_CRON_TRIGGERS ADD COLUMN TIME_ZONE_ID NVARCHAR(80) NULL;
