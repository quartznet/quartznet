--
-- Quartz.NET schema migration -- add MISFIRE_ORIG_FIRE_TIME
--
-- Introduced in Quartz.NET 3.17.0 (#2899)
--
-- MySQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   3.x  OPTIONAL. Without it AdoJobStore keeps working, but ScheduledFireTimeUtc
--        equals FireTimeUtc for misfired triggers (the pre-3.17 behavior). The job
--        store probes at startup and logs a warning when the column is absent.
--        RAMJobStore is unaffected.
--
--   4.x  REQUIRED. 4.x removed the startup probe and assumes the column exists. When
--        upgrading from 3.x run ../4.0/schema_30_to_40_upgrade_mysql_innodb.sql instead -- it
--        folds this change in.
--
-- Stores the original scheduled fire time before misfire handling overwrites it, which
-- is what makes ScheduledFireTimeUtc correct for misfired triggers under the "fire
-- now" misfire policies (FireOnceNow, FireNow, etc.).
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_TRIGGERS' AND COLUMN_NAME = 'MISFIRE_ORIG_FIRE_TIME') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_TRIGGERS ADD COLUMN MISFIRE_ORIG_FIRE_TIME BIGINT NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;
