--
-- Quartz.NET schema migration -- align indexes with the 3.x schema
--
-- Introduced in Quartz.NET 3.20.0 (#3203)
--
-- SQL Server only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   3.x  OPTIONAL, performance only. Nothing stops working if it is not applied, but
--        several of these indexes could not serve a single-scheduler lookup at all.
--
--   4.x  Superseded. ../4.0/schema_30_to_40_indexes_sqlServer.sql converges the same index
--        set onto the 4.x shape -- run that instead when upgrading to 4.x.
--
-- Brings an existing database's index set in line with what the current
-- database/tables/tables_sqlServer.sql creates. A database created from the current
-- script already matches and needs nothing from this file.
--
-- Every Quartz statement filters SCHED_NAME first, so every index here leads with it.
-- Indexes that are a leftmost prefix of a wider one, or that no statement can drive a
-- scan from, are dropped.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === Create the indexes this version expects ===================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_G_J' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_G_J] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, JOB_GROUP, JOB_NAME);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_C' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_C] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, CALENDAR_NAME);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_N_G_STATE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_N_G_STATE] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, TRIGGER_GROUP, TRIGGER_STATE);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_N_STATE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_N_STATE] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, TRIGGER_STATE);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NEXT_FIRE_TIME' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_NEXT_FIRE_TIME] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, NEXT_FIRE_TIME);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_ST' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_NFT_ST] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_ST_MISFIRE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_NFT_ST_MISFIRE] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_STATE);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_ST_MISFIRE_GRP' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_NFT_ST_MISFIRE_GRP] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_GROUP, TRIGGER_STATE);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_INST_JOB_REQ_RCVRY' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_FT_INST_JOB_REQ_RCVRY] ON [dbo].[QRTZ_FIRED_TRIGGERS](SCHED_NAME, INSTANCE_NAME, REQUESTS_RECOVERY);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_G_J' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_FT_G_J] ON [dbo].[QRTZ_FIRED_TRIGGERS](SCHED_NAME, JOB_GROUP, JOB_NAME);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_G_T' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_FT_G_T] ON [dbo].[QRTZ_FIRED_TRIGGERS](SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME);
END
GO

-- === Drop the ones it no longer uses ==========================================
-- Guarded, so each is a no-op when that index is not present.

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_J_GRP' AND object_id = OBJECT_ID('dbo.QRTZ_JOB_DETAILS'))
BEGIN
  DROP INDEX [IDX_QRTZ_J_GRP] ON [dbo].[QRTZ_JOB_DETAILS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_J_REQ_RECOVERY' AND object_id = OBJECT_ID('dbo.QRTZ_JOB_DETAILS'))
BEGIN
  DROP INDEX [IDX_QRTZ_J_REQ_RECOVERY] ON [dbo].[QRTZ_JOB_DETAILS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_JG' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_JG] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_G' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_G] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_STATE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_STATE] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_MISFIRE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_NFT_MISFIRE] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_JG' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_FT_JG] ON [dbo].[QRTZ_FIRED_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_TG' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_FT_TG] ON [dbo].[QRTZ_FIRED_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_TRIG_INST_NAME' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_FT_TRIG_INST_NAME] ON [dbo].[QRTZ_FIRED_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_TRIG_NM_GP' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_FT_TRIG_NM_GP] ON [dbo].[QRTZ_FIRED_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_TRIG_NAME' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_FT_TRIG_NAME] ON [dbo].[QRTZ_FIRED_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_TRIG_GROUP' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_FT_TRIG_GROUP] ON [dbo].[QRTZ_FIRED_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_JOB_NAME' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_FT_JOB_NAME] ON [dbo].[QRTZ_FIRED_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_JOB_GROUP' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_FT_JOB_GROUP] ON [dbo].[QRTZ_FIRED_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_JOB_REQ_RECOVERY' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_FT_JOB_REQ_RECOVERY] ON [dbo].[QRTZ_FIRED_TRIGGERS];
END
GO
