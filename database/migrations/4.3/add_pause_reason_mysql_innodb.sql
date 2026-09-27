--
-- Quartz.NET schema migration -- add the pause reason columns
--
-- Introduced in Quartz.NET 4.3.0 (#3879)
--
-- MySQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.3  REQUIRED. A 4.3 node writes these columns whenever it pauses a trigger or a group,
--        so it refuses to start against a database without them.
--
--        Safe to run while 4.2 nodes are still up: the columns are nullable with no default,
--        so every existing row is already valid, and a 4.2 node never names them. A pause a
--        4.2 node makes leaves them NULL, which reads as a pause that gave no reason. A 4.2
--        node's resume leaves a trigger's columns behind, so a 4.3 node reports them only
--        while the trigger is paused -- migrate, and roll every node.
--
--   4.1  Run ../4.2/add_continuations_mysql_innodb.sql first on a database created by
--        4.0 or 4.1.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_mysql_innodb.sql and every later migration first;
--        this file is what 4.3 adds on top of them.
--
-- PAUSE_REASON is why the trigger or group was paused, as the caller said it, cut to 250
-- characters. PAUSED_BY is who asked, cut to 200: a user name, or 'quartz:retries-exhausted'
-- when the scheduler paused a trigger whose retries ran out. PAUSED_AT is when, in ticks,
-- as every other instant in this schema is stored.
--
-- A 4.3 node writes all three on every pause, a pause without a reason included, and clears
-- them on a trigger it resumes. A group's row is deleted when the group is resumed, so it
-- takes its columns with it.
--
-- No index is added. The columns are read with the row they are on, never searched on.
--
-- ALL NINE COLUMNS MUST BE ADDED TOGETHER.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === 1. QRTZ_TRIGGERS ===

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_TRIGGERS' AND COLUMN_NAME = 'PAUSE_REASON') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PAUSE_REASON VARCHAR(250) NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_TRIGGERS' AND COLUMN_NAME = 'PAUSED_BY') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PAUSED_BY VARCHAR(200) NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_TRIGGERS' AND COLUMN_NAME = 'PAUSED_AT') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PAUSED_AT BIGINT NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

-- === 2. QRTZ_PAUSED_TRIGGER_GRPS ===

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_PAUSED_TRIGGER_GRPS' AND COLUMN_NAME = 'PAUSE_REASON') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_PAUSED_TRIGGER_GRPS ADD COLUMN PAUSE_REASON VARCHAR(250) NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_PAUSED_TRIGGER_GRPS' AND COLUMN_NAME = 'PAUSED_BY') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_PAUSED_TRIGGER_GRPS ADD COLUMN PAUSED_BY VARCHAR(200) NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_PAUSED_TRIGGER_GRPS' AND COLUMN_NAME = 'PAUSED_AT') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_PAUSED_TRIGGER_GRPS ADD COLUMN PAUSED_AT BIGINT NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

-- === 3. QRTZ_PAUSED_JOB_GRPS ===

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_PAUSED_JOB_GRPS' AND COLUMN_NAME = 'PAUSE_REASON') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_PAUSED_JOB_GRPS ADD COLUMN PAUSE_REASON VARCHAR(250) NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_PAUSED_JOB_GRPS' AND COLUMN_NAME = 'PAUSED_BY') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_PAUSED_JOB_GRPS ADD COLUMN PAUSED_BY VARCHAR(200) NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_PAUSED_JOB_GRPS' AND COLUMN_NAME = 'PAUSED_AT') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_PAUSED_JOB_GRPS ADD COLUMN PAUSED_AT BIGINT NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;
