-- Quartz.NET schema migration: drop indexes that are redundant with a wider index
--
-- Every index below is a leftmost prefix of another index on the same table, so the wider index
-- already answers everything the narrow one answered. Keeping both only costs write throughput and
-- buffer pool. The current database/tables/*.sql scripts no longer create these, so this file is
-- only needed for databases created before that change.
--
-- This migration is OPTIONAL: nothing stops working if it is not applied.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
--
-- Which of the four exist depends on the script the database was created from:
--   Firebird, MySQL, Oracle, SQL Server (memory-optimized)  ->  all four
--   SQL Server (regular, incl. Below2016)                   ->  IDX_QRTZ_T_STATE only
--   PostgreSQL  ->  handled by schema_30_postgres_index_realignment.sql, not here
--   SQLite      ->  the SQLite script ships no indexes at all, nothing to do
--
-- Index                        Covered by
-- ---------------------------  --------------------------------------------------------------
-- IDX_QRTZ_T_STATE             IDX_QRTZ_T_NFT_ST             (SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME)
-- IDX_QRTZ_T_G                 IDX_QRTZ_T_N_G_STATE          (SCHED_NAME, TRIGGER_GROUP, TRIGGER_STATE)
-- IDX_QRTZ_T_NFT_MISFIRE       IDX_QRTZ_T_NFT_ST_MISFIRE     (SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_STATE)
-- IDX_QRTZ_FT_TRIG_INST_NAME   IDX_QRTZ_FT_INST_JOB_REQ_RCVRY (SCHED_NAME, INSTANCE_NAME, REQUESTS_RECOVERY)
--
-- Do NOT drop the covering indexes themselves. On MySQL, IDX_QRTZ_T_NFT_ST and
-- IDX_QRTZ_T_NFT_ST_MISFIRE are named in FORCE INDEX hints by MySQLDelegate; dropping either one
-- turns the acquire and misfire statements into hard errors, not merely slow ones.

-- SQL Server (regular and memory-optimized)
-- Guarded, so each statement is a no-op when that index is not present. Safe to run as-is.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_STATE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
    DROP INDEX [IDX_QRTZ_T_STATE] ON [dbo].[QRTZ_TRIGGERS];   -- covered by IDX_QRTZ_T_NFT_ST
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_G' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
    DROP INDEX [IDX_QRTZ_T_G] ON [dbo].[QRTZ_TRIGGERS];   -- covered by IDX_QRTZ_T_N_G_STATE
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_MISFIRE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
    DROP INDEX [IDX_QRTZ_T_NFT_MISFIRE] ON [dbo].[QRTZ_TRIGGERS];   -- covered by IDX_QRTZ_T_NFT_ST_MISFIRE
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_TRIG_INST_NAME' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
    DROP INDEX [IDX_QRTZ_FT_TRIG_INST_NAME] ON [dbo].[QRTZ_FIRED_TRIGGERS];   -- covered by IDX_QRTZ_FT_INST_JOB_REQ_RCVRY
GO

-- MySQL
-- DROP INDEX has no IF EXISTS in MySQL; drop only the ones SHOW INDEX FROM ... reports.
-- DROP INDEX IDX_QRTZ_T_STATE ON QRTZ_TRIGGERS;                        -- covered by IDX_QRTZ_T_NFT_ST
-- DROP INDEX IDX_QRTZ_T_G ON QRTZ_TRIGGERS;                            -- covered by IDX_QRTZ_T_N_G_STATE
-- DROP INDEX IDX_QRTZ_T_NFT_MISFIRE ON QRTZ_TRIGGERS;                  -- covered by IDX_QRTZ_T_NFT_ST_MISFIRE
-- DROP INDEX IDX_QRTZ_FT_TRIG_INST_NAME ON QRTZ_FIRED_TRIGGERS;        -- covered by IDX_QRTZ_FT_INST_JOB_REQ_RCVRY
--
-- MySQL only: QRTZ_BLOB_TRIGGERS was created with an unnamed inline INDEX on
-- (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP), which is an exact duplicate of that table's primary
-- key. The primary key already satisfies InnoDB's index requirement for the foreign key, so the
-- extra index is pure overhead. It has no portable name, so look it up first -- InnoDB names an
-- unnamed index after its first column, usually SCHED_NAME:
-- SHOW INDEX FROM QRTZ_BLOB_TRIGGERS;                                  -- find the non-PRIMARY key
-- DROP INDEX SCHED_NAME ON QRTZ_BLOB_TRIGGERS;                         -- duplicate of the primary key

-- Oracle
-- DROP INDEX IDX_QRTZ_T_STATE;                                         -- covered by IDX_QRTZ_T_NFT_ST
-- DROP INDEX IDX_QRTZ_T_G;                                             -- covered by IDX_QRTZ_T_N_G_STATE
-- DROP INDEX IDX_QRTZ_T_NFT_MISFIRE;                                   -- covered by IDX_QRTZ_T_NFT_ST_MISFIRE
-- DROP INDEX IDX_QRTZ_FT_TRIG_INST_NAME;                               -- covered by IDX_QRTZ_FT_INST_JOB_REQ_RCVRY

-- Firebird
-- DROP INDEX IDX_QRTZ_T_STATE;                                         -- covered by IDX_QRTZ_T_NFT_ST
-- DROP INDEX IDX_QRTZ_T_G;                                             -- covered by IDX_QRTZ_T_N_G_STATE
-- DROP INDEX IDX_QRTZ_T_NFT_MISFIRE;                                   -- covered by IDX_QRTZ_T_NFT_ST_MISFIRE
-- DROP INDEX IDX_QRTZ_FT_TRIG_INST_NAME;                               -- covered by IDX_QRTZ_FT_INST_JOB_REQ_RCVRY

-- PostgreSQL
-- Nothing here: PostgreSQL's index set is reworked as a whole by
-- schema_30_postgres_index_realignment.sql, which includes the IDX_QRTZ_T_STATE drop.

-- SQLite
-- Nothing to do: tables_sqlite.sql creates no indexes beyond the primary keys.
