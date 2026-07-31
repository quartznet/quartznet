--
-- Quartz.NET schema migration -- 2.6 to 3.0
--
-- Converts the deprecated IMAGE columns to VARBINARY(MAX). SQL Server has deprecated IMAGE for
-- years and 3.x writes these columns as VARBINARY (#291).
--
-- STATUS
--   REQUIRED when upgrading a SQL Server database from 2.x to 3.0 or later.
--
-- SQL Server only -- the other dialects never used IMAGE, so they need nothing here.
--
-- Replace 'QRTZ_' with your configured table prefix if different. ALTER COLUMN is idempotent:
-- re-applying the same type is a no-op, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

--USE [database_name];
--GO

ALTER TABLE [dbo].[QRTZ_CALENDARS]
ALTER COLUMN [CALENDAR] [VARBINARY](MAX) NOT NULL;
GO

ALTER TABLE [dbo].[QRTZ_JOB_DETAILS]
ALTER COLUMN [JOB_DATA] [VARBINARY](MAX) NULL;
GO

ALTER TABLE [dbo].[QRTZ_BLOB_TRIGGERS]
ALTER COLUMN [BLOB_DATA] [VARBINARY](MAX) NULL;
GO

ALTER TABLE [dbo].[QRTZ_TRIGGERS]
ALTER COLUMN [JOB_DATA] [VARBINARY](MAX) NULL;
GO