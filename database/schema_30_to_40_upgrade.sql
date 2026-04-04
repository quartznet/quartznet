/*
Upgrade Quartz.NET schema for SQL Server database (or other database in commented code)
Migration from 3.x to 4.x
*/
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST COPY OF YOUR PRODUCTION !!
--
-- Adds the MISFIRE_ORIG_FIRE_TIME column to the QRTZ_TRIGGERS table.
-- This column stores the original scheduled fire time before misfire handling
-- changes it, enabling correct ScheduledFireTimeUtc in JobExecutionContext.
--
-- This column is REQUIRED for 4.x. Apply the appropriate ALTER TABLE for your database.
-- Replace 'QRTZ_' with your configured table prefix if different.
--
-- NOTE: This column was added as optional in Quartz.NET 3.17. If you are already
-- running 3.17 or later, this column may already exist in your database.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST COPY OF YOUR PRODUCTION !!
--

-- SQL Server
IF COL_LENGTH('QRTZ_TRIGGERS','MISFIRE_ORIG_FIRE_TIME') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_TRIGGERS] ADD [MISFIRE_ORIG_FIRE_TIME] bigint NULL;
END
GO

-- PostgreSQL (check existence before adding)
-- DO $$
-- BEGIN
--   IF NOT EXISTS (SELECT 1 FROM information_schema.columns
--                  WHERE table_name = 'qrtz_triggers' AND column_name = 'misfire_orig_fire_time') THEN
--     ALTER TABLE qrtz_triggers ADD COLUMN misfire_orig_fire_time bigint;
--   END IF;
-- END $$;

-- MySQL (check existence before adding)
-- SET @dbname = DATABASE();
-- SET @tablename = 'QRTZ_TRIGGERS';
-- SET @columnname = 'MISFIRE_ORIG_FIRE_TIME';
-- SET @preparedStatement = (SELECT IF(
--   (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
--    WHERE TABLE_SCHEMA = @dbname AND TABLE_NAME = @tablename AND COLUMN_NAME = @columnname) > 0,
--   'SELECT 1',
--   CONCAT('ALTER TABLE ', @tablename, ' ADD COLUMN ', @columnname, ' BIGINT NULL')
-- ));
-- PREPARE alterIfNotExists FROM @preparedStatement;
-- EXECUTE alterIfNotExists;
-- DEALLOCATE PREPARE alterIfNotExists;

-- SQLite (SQLite does not error on duplicate ADD COLUMN in all versions, but check if needed)
-- ALTER TABLE QRTZ_TRIGGERS ADD COLUMN MISFIRE_ORIG_FIRE_TIME INTEGER;

-- Oracle (check existence before adding)
-- DECLARE
--   column_exists NUMBER;
-- BEGIN
--   SELECT COUNT(*) INTO column_exists FROM user_tab_columns
--   WHERE table_name = 'QRTZ_TRIGGERS' AND column_name = 'MISFIRE_ORIG_FIRE_TIME';
--   IF column_exists = 0 THEN
--     EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_TRIGGERS ADD (MISFIRE_ORIG_FIRE_TIME NUMBER(19))';
--   END IF;
-- END;
-- /

-- Firebird
-- ALTER TABLE QRTZ_TRIGGERS ADD MISFIRE_ORIG_FIRE_TIME BIGINT;
