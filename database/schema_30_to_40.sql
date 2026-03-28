-- Schema migration from 3.x to 4.x
--
-- Adds the MISFIRE_ORIG_FIRE_TIME column to the QRTZ_TRIGGERS table.
-- This column stores the original scheduled fire time before misfire handling
-- changes it, enabling correct ScheduledFireTimeUtc in JobExecutionContext.
--
-- This column is REQUIRED for 4.x. Apply the appropriate ALTER TABLE for your database.
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
