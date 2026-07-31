--
-- Quartz.NET schema migration -- 2.5 to 2.6
--
-- MySQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   REQUIRED when upgrading from 2.5 or earlier to 2.6 or later with AdoJobStore.
--
-- Adds TIME_ZONE_ID to QRTZ_SIMPROP_TRIGGERS and QRTZ_CRON_TRIGGERS so a trigger's
-- time zone survives a restart (#136). Both tables need it (#1985).
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_SIMPROP_TRIGGERS' AND COLUMN_NAME = 'TIME_ZONE_ID') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_SIMPROP_TRIGGERS ADD COLUMN TIME_ZONE_ID VARCHAR(80) NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_CRON_TRIGGERS' AND COLUMN_NAME = 'TIME_ZONE_ID') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_CRON_TRIGGERS ADD COLUMN TIME_ZONE_ID VARCHAR(80) NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;
