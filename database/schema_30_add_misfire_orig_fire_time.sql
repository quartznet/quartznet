-- Adds the MISFIRE_ORIG_FIRE_TIME column to the QRTZ_TRIGGERS table.
-- This column enables correct ScheduledFireTimeUtc for misfired triggers
-- when using "fire now" misfire policies (FireOnceNow, FireNow, etc.).
--
-- This migration is OPTIONAL. Without it, AdoJobStore continues to work
-- but ScheduledFireTimeUtc will equal FireTimeUtc for misfired triggers
-- (the pre-existing behavior). RAMJobStore does not require this migration.
--
-- Replace 'QRTZ_' with your configured table prefix if different.

-- SQL Server
ALTER TABLE [dbo].[QRTZ_TRIGGERS] ADD [MISFIRE_ORIG_FIRE_TIME] bigint NULL;
GO

-- PostgreSQL
-- ALTER TABLE qrtz_triggers ADD COLUMN misfire_orig_fire_time bigint;

-- MySQL
-- ALTER TABLE QRTZ_TRIGGERS ADD COLUMN MISFIRE_ORIG_FIRE_TIME BIGINT NULL;

-- SQLite
-- ALTER TABLE QRTZ_TRIGGERS ADD COLUMN MISFIRE_ORIG_FIRE_TIME INTEGER;

-- Oracle
-- ALTER TABLE QRTZ_TRIGGERS ADD (MISFIRE_ORIG_FIRE_TIME NUMBER(19));

-- Firebird
-- ALTER TABLE QRTZ_TRIGGERS ADD MISFIRE_ORIG_FIRE_TIME BIGINT;
