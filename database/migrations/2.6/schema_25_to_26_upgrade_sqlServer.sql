--
-- Quartz.NET schema migration -- 2.5 to 2.6
--
-- SQL Server only. Run the file matching your database; the other dialects live
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

IF COL_LENGTH('QRTZ_SIMPROP_TRIGGERS','TIME_ZONE_ID') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_SIMPROP_TRIGGERS] ADD [TIME_ZONE_ID] nvarchar(80) NULL;
END
GO

IF COL_LENGTH('QRTZ_CRON_TRIGGERS','TIME_ZONE_ID') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_CRON_TRIGGERS] ADD [TIME_ZONE_ID] nvarchar(80) NULL;
END
GO
