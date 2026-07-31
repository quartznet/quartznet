--
-- Quartz.NET schema migration -- 2.5 to 2.6
--
-- Oracle only. Run the file matching your database; the other dialects live
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

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_SIMPROP_TRIGGERS' AND column_name = 'TIME_ZONE_ID';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_SIMPROP_TRIGGERS ADD (TIME_ZONE_ID VARCHAR2(80) NULL)';
  END IF;
END;
/

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_CRON_TRIGGERS' AND column_name = 'TIME_ZONE_ID';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_CRON_TRIGGERS ADD (TIME_ZONE_ID VARCHAR2(80) NULL)';
  END IF;
END;
/
