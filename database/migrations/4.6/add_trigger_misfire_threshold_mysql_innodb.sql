--
-- Quartz.NET schema migration -- add the trigger misfire threshold column
--
-- Introduced in Quartz.NET 4.6.0 (#4070)
--
-- MySQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.6  REQUIRED. A 4.6 node reads and writes this column on every trigger it stores,
--        and its misfire scan and acquisition compare against it, so it refuses to start
--        against a database without it.
--
--        Safe to run while 4.5 nodes are still up: the column is nullable with no default,
--        so every existing row is already valid, and a 4.5 node never names it. What a
--        4.5 node cannot do is honour a threshold -- it judges every trigger by the store's
--        MisfireThreshold -- so migrate, roll every node, and only then give a trigger a
--        threshold of its own.
--
--   4.1  Run ../4.2/add_continuations_mysql_innodb.sql first on a database created by
--        4.0 or 4.1, and every later required migration.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_mysql_innodb.sql and every later migration first;
--        this file is what 4.6 adds on top of them.
--
-- MISFIRE_THRESHOLD is the trigger's own misfire threshold in milliseconds: how late it
-- may fire before its misfire instruction applies. NULL is the store's MisfireThreshold,
-- which is what every row a node older than 4.6 writes reads as. A 4.5 node that updates
-- a row leaves the column as it found it, because it never names it.
--
-- No index is added. The misfire scan and acquisition range over IDX_QRTZ_T_NFT_ST on
-- NEXT_FIRE_TIME as before, and read this column as a residual beside it.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_TRIGGERS' AND COLUMN_NAME = 'MISFIRE_THRESHOLD') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_TRIGGERS ADD COLUMN MISFIRE_THRESHOLD BIGINT NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;
