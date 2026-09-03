--
-- Quartz.NET schema migration -- align indexes with the 3.x schema
--
-- Introduced in Quartz.NET 3.20.0 (#3203)
--
-- SQLite only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   3.x  OPTIONAL, performance only. Nothing stops working if it is not applied, but
--        several of these indexes could not serve a single-scheduler lookup at all.
--
--   4.x  Superseded. ../4.0/schema_30_to_40_indexes_sqlite.sql converges the same index
--        set onto the 4.x shape -- run that instead when upgrading to 4.x.
--
-- Brings an existing database's index set in line with what the current
-- database/tables/tables_sqlite.sql creates. A database created from the current
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

CREATE INDEX IF NOT EXISTS IDX_QRTZ_J_REQ_RECOVERY ON QRTZ_JOB_DETAILS(SCHED_NAME,REQUESTS_RECOVERY);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_J_G_N ON QRTZ_JOB_DETAILS(SCHED_NAME,JOB_GROUP,JOB_NAME);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_T_J ON QRTZ_TRIGGERS(SCHED_NAME,JOB_NAME,JOB_GROUP);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_T_C ON QRTZ_TRIGGERS(SCHED_NAME,CALENDAR_NAME);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_T_G_N ON QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_GROUP,TRIGGER_NAME);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_T_NEXT_FIRE_TIME ON QRTZ_TRIGGERS(SCHED_NAME,NEXT_FIRE_TIME);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_T_NFT_ST ON QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_STATE,NEXT_FIRE_TIME);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_FT_INST_JOB_REQ_RCVRY ON QRTZ_FIRED_TRIGGERS(SCHED_NAME,INSTANCE_NAME,REQUESTS_RECOVERY);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_FT_J_G ON QRTZ_FIRED_TRIGGERS(SCHED_NAME,JOB_NAME,JOB_GROUP);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_FT_T_G ON QRTZ_FIRED_TRIGGERS(SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP);

-- === Drop the ones it no longer uses ==========================================
-- Guarded, so each is a no-op when that index is not present.

DROP INDEX IF EXISTS IDX_QRTZ_J_GRP;

DROP INDEX IF EXISTS IDX_QRTZ_T_G_J;

DROP INDEX IF EXISTS IDX_QRTZ_T_JG;

DROP INDEX IF EXISTS IDX_QRTZ_T_G;

DROP INDEX IF EXISTS IDX_QRTZ_T_STATE;

DROP INDEX IF EXISTS IDX_QRTZ_T_N_STATE;

DROP INDEX IF EXISTS IDX_QRTZ_T_N_G_STATE;

DROP INDEX IF EXISTS IDX_QRTZ_T_NFT_MISFIRE;

DROP INDEX IF EXISTS IDX_QRTZ_T_NFT_ST_MISFIRE_GRP;

DROP INDEX IF EXISTS IDX_QRTZ_T_NFT_ST_MISFIRE;

DROP INDEX IF EXISTS IDX_QRTZ_FT_G_J;

DROP INDEX IF EXISTS IDX_QRTZ_FT_G_T;

DROP INDEX IF EXISTS IDX_QRTZ_FT_JG;

DROP INDEX IF EXISTS IDX_QRTZ_FT_TG;

DROP INDEX IF EXISTS IDX_QRTZ_FT_TRIG_INST_NAME;

DROP INDEX IF EXISTS IDX_QRTZ_FT_TRIG_NM_GP;

DROP INDEX IF EXISTS IDX_QRTZ_FT_TRIG_NAME;

DROP INDEX IF EXISTS IDX_QRTZ_FT_TRIG_GROUP;

DROP INDEX IF EXISTS IDX_QRTZ_FT_JOB_NAME;

DROP INDEX IF EXISTS IDX_QRTZ_FT_JOB_GROUP;

DROP INDEX IF EXISTS IDX_QRTZ_FT_JOB_REQ_RECOVERY;
