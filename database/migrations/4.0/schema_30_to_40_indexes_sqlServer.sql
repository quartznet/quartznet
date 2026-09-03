--
-- Quartz.NET schema migration -- 3.x to 4.0, index set
--
-- SQL Server only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   OPTIONAL: 4.x runs unchanged either way. The creates matter once a schema holds a
--   non-trivial number of triggers; the drops only reclaim write cost and storage.
--
--   NOT to be run while any 3.x node is still up -- see below.
--
-- Run schema_30_to_40_upgrade_sqlServer.sql first. That one is mandatory and this one is
-- not, and it is the one that is safe to run while 3.x nodes are still up.
--
-- WHEN TO RUN THIS: once the last 3.x node has shut down, or straight after the upgrade file
-- on an offline upgrade. Among the drops is IDX_QRTZ_T_NFT_ST_MISFIRE, which 3.x drives its
-- misfire sweep from and 4.x does not read at all (#3656). A 3.x node keeps working without
-- it -- it scans where it used to seek, which on a large schedule is the difference between a
-- misfire sweep that finishes and one that times out.
--
-- What it does: creates the indexes 4.x's statements are written for, reshapes
-- IDX_QRTZ_T_NFT_ST to carry the order acquisition reads in, and drops the ones no 4.x
-- statement can drive a scan from. The end state is the index set database/tables/ creates
-- for a fresh 4.x install.
--
-- Run it top to bottom. The creates come before the drops on purpose: IDX_QRTZ_T_NFT_ST is
-- brought to its 4.x shape before IDX_QRTZ_T_NFT_ST_MISFIRE is dropped, so no schema is ever
-- left with neither index.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === Drop the indexes whose columns changed but whose name did not ============
-- These have to go first: CREATE INDEX IF NOT EXISTS below would find the name
-- already taken and silently keep the old, wrong column order.

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_ST' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_NFT_ST] ON [dbo].[QRTZ_TRIGGERS];
END
GO

-- === Create the indexes this version expects ===================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_J_G_N' AND object_id = OBJECT_ID('dbo.QRTZ_JOB_DETAILS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_J_G_N] ON [dbo].[QRTZ_JOB_DETAILS](SCHED_NAME, JOB_GROUP, JOB_NAME);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_J' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_J] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, JOB_NAME, JOB_GROUP);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_G_N' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_G_N] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_C' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_C] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, CALENDAR_NAME);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_ST' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_NFT_ST] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME ASC, PRIORITY DESC, MISFIRE_INSTR);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_INST_JOB_REQ_RCVRY' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_FT_INST_JOB_REQ_RCVRY] ON [dbo].[QRTZ_FIRED_TRIGGERS](SCHED_NAME, INSTANCE_NAME, REQUESTS_RECOVERY);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_J_G' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_FT_J_G] ON [dbo].[QRTZ_FIRED_TRIGGERS](SCHED_NAME, JOB_NAME, JOB_GROUP);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_T_G' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_FT_T_G] ON [dbo].[QRTZ_FIRED_TRIGGERS](SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP);
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

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_G_J' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_G_J] ON [dbo].[QRTZ_TRIGGERS];
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

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_N_STATE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_N_STATE] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_N_G_STATE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_N_G_STATE] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NEXT_FIRE_TIME' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_NEXT_FIRE_TIME] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_MISFIRE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_NFT_MISFIRE] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_ST_MISFIRE_GRP' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_NFT_ST_MISFIRE_GRP] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_ST_MISFIRE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_NFT_ST_MISFIRE] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_G_J' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_FT_G_J] ON [dbo].[QRTZ_FIRED_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_G_T' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_FT_G_T] ON [dbo].[QRTZ_FIRED_TRIGGERS];
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
