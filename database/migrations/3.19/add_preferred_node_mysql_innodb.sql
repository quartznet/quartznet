--
-- Quartz.NET schema migration -- add PREFERRED_NODE and PREFERRED_NODE_AUTO
--
-- Introduced in Quartz.NET 3.19.0 (#3013, #3144)
--
-- MySQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   3.x  OPTIONAL. Without the columns node affinity is unavailable; the scheduler
--        logs a warning at startup and otherwise behaves exactly as before 3.19.
--
--   4.x  REQUIRED. 4.x removed the startup probe and assumes the column exists. When
--        upgrading from 3.x run ../4.0/schema_30_to_40_upgrade_mysql_innodb.sql instead -- it
--        folds this change in.
--
-- These back node affinity (pinning a trigger to a preferred cluster node).
--
-- PREFERRED_NODE holds the target node's instance id verbatim, or the "*" sentinel
-- requesting auto-pin. PREFERRED_NODE_AUTO records whether that pin was claimed
-- automatically by the node that first fired the trigger -- auto-claimed pins are
-- released back to "*" when their node dies, explicit pins are preserved.
--
-- BOTH COLUMNS MUST BE ADDED TOGETHER. Quartz probes for both and only enables node
-- affinity when both are present, so adding just one leaves the feature off.
--
-- The 3.x and 4.x representations are identical, so no data migration is needed.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_TRIGGERS' AND COLUMN_NAME = 'PREFERRED_NODE') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PREFERRED_NODE VARCHAR(200) NULL'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;

SET @preparedStatement = (SELECT IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_TRIGGERS' AND COLUMN_NAME = 'PREFERRED_NODE_AUTO') > 0,
  'SELECT 1',
  'ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PREFERRED_NODE_AUTO BOOLEAN NOT NULL DEFAULT FALSE'
));
PREPARE alterIfNotExists FROM @preparedStatement;
EXECUTE alterIfNotExists;
DEALLOCATE PREPARE alterIfNotExists;
