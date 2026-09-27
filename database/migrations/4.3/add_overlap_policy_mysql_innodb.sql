--
-- Quartz.NET schema migration -- add the overlap policy column
--
-- Introduced in Quartz.NET 4.3.0 (#3875)
--
-- MySQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.3  REQUIRED. A 4.3 node reads and writes this column on every trigger it stores, so
--        it refuses to start against a database without it.
--
--        Safe to run while 4.2 nodes are still up: the column is nullable with no default,
--        so every existing row is already valid, and a 4.2 node never names it. What a 4.2
--        node cannot do is honour a policy -- it fires every trigger as Default does -- so
--        migrate, roll every node, and only then give a trigger an overlap policy.
--
--   4.1  Run ../4.2/add_continuations_mysql_innodb.sql first on a database created by
--        4.0 or 4.1.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_mysql_innodb.sql and every later migration first;
--        this file is what 4.3 adds on top of them.
--
-- OVERLAP_POLICY is the integer of the trigger's OverlapPolicy: what happens when a
-- firing comes due while an earlier firing of the same trigger is still running.
-- NULL and 0 are Default, 1 Skip, 2 BufferOne, 3 CancelPrevious, 4 AllowAll. The store
-- adds 16 to CancelPrevious (19) while a firing that started under another policy may
-- still be running, and clears it once none is.
--
-- No index is added. The column is read with the rest of the trigger's row, never
-- searched on.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_TRIGGERS' AND COLUMN_NAME = 'OVERLAP_POLICY') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_TRIGGERS ADD COLUMN OVERLAP_POLICY INTEGER NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;
