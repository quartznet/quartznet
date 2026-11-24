--
-- Quartz.NET schema migration -- add the execution due time, start delay, origin and exception type columns
--
-- Introduced in Quartz.NET 4.9.0 (#4333)
--
-- MySQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.9  OPTIONAL, and only for a database that has QRTZ_EXECUTION_HISTORY -- one that
--        ran ../4.2/add_execution_history_mysql_innodb.sql or was created by 4.2 or later.
--        A store configured with UsePersistentStore(s => s.UseExecutionHistory()) writes
--        these columns with every history row, so it refuses to start without them. No
--        other scheduler reads the table.
--
--        Do NOT run it against a database without QRTZ_EXECUTION_HISTORY: the first
--        statement alters that table, and fails when the table is not there.
--
--        Safe under a mixed cluster: a 4.8 node's history rows name their own columns and
--        leave these NULL, and a 4.8 node never reads them.
--
--   3.x  Not applicable.
--
-- On QRTZ_EXECUTION_HISTORY, when a run was due, how late it started and why it ran:
--   DUE_TIME        when the run was due: the occurrence it serves, so a backfill is due
--                   when its slot was, and a retry when its occurrence was
--   START_DELAY     how late the run's job started, in ticks: from when the attempt was due
--                   until the job was called, the same number
--                   quartz.job.execution.start_delay records
--   ORIGIN          why it ran, the integer of FiringOrigin: 0 schedule, 1 manual, 2 backfill
--   EXCEPTION_TYPE  the full name of the type of what the job threw, cut to 200 characters
-- All four are nullable with no default. A row a 4.8 node wrote leaves them NULL, and the
-- History page shows a dash for a due time and a delay it does not have, no badge for an
-- origin it does not have, and the message alone for a failure whose type it does not have.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_EXECUTION_HISTORY' AND COLUMN_NAME = 'DUE_TIME') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN DUE_TIME BIGINT NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_EXECUTION_HISTORY' AND COLUMN_NAME = 'START_DELAY') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN START_DELAY BIGINT NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_EXECUTION_HISTORY' AND COLUMN_NAME = 'ORIGIN') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN ORIGIN INTEGER NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_EXECUTION_HISTORY' AND COLUMN_NAME = 'EXCEPTION_TYPE') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN EXCEPTION_TYPE VARCHAR(200) NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;
