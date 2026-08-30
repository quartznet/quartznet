--
-- Quartz.NET schema migration -- 3.x to 4.0
--
-- SQLite only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   MANDATORY. This is the one migration you cannot skip.
--
--   Quartz.NET 3.x probes for MISFIRE_ORIG_FIRE_TIME, EXECUTION_GROUP, PREFERRED_NODE
--   and PREFERRED_NODE_AUTO at startup and degrades gracefully when they are absent.
--   4.x removed those probes and assumes all four exist, so a 3.x database that never
--   ran the optional migrations will fail against 4.x until this script has run.
--
--   4.x also adds columns and a table 3.x never had -- RETRY_POLICY and RETRY_ATTEMPT
--   on QRTZ_TRIGGERS, and the whole QRTZ_PAUSED_JOB_GRPS table -- and validates its
--   schema at startup, so this script is required even for a 3.x database that took
--   every optional migration going.
--
-- This script supersedes the optional per-feature migrations in ../3.17, ../3.18,
-- ../3.19 and ../3.20 -- it applies everything they do, and it assumes none of them
-- were applied. Run it exactly once, against a database that took none of the optional
-- 3.x column migrations.
--
-- On a partially-migrated database take the stepped route instead -- run the
-- per-feature files you are still missing -- or check PRAGMA table_info(<table>) and
-- apply only the sections whose columns are absent.
--
-- Sections, in order:
--   1. MISFIRE_ORIG_FIRE_TIME column                REQUIRED
--   2. EXECUTION_GROUP columns                      REQUIRED
--   3. PREFERRED_NODE / PREFERRED_NODE_AUTO         REQUIRED
--   4. RETRY_POLICY / RETRY_ATTEMPT                 REQUIRED
--   5. QRTZ_PAUSED_JOB_GRPS table                   REQUIRED
--   6. Index set aligned with the 4.x schema        optional
--
-- Run the sections in order: the drops in section 6 assume the creates above them have
-- already succeeded.
--
-- Sections 4 and 5 have no 3.x counterpart at all, so nothing you ran on 3.x can have
-- applied them.
--
-- RETRY_POLICY holds a trigger's retry policy and RETRY_ATTEMPT how many retries of the
-- occurrence being executed have already been made. Both are nullable with no default, so
-- every existing row reads as "no retry policy" and no data migration is needed (#3520).
--
-- 3.x pauses a job group without recording it anywhere, so a paused job group could not be
-- listed or asked about; 4.x keeps the group names in QRTZ_PAUSED_JOB_GRPS, which is what
-- makes JobGroup.Paused answer truthfully and what carries the pause across a restart
-- (#3336).
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- NOT IDEMPOTENT: SQLite has no conditional DDL, so re-running this fails with a
-- duplicate-column error. Check PRAGMA table_info(<table>) before applying.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === 1. MISFIRE_ORIG_FIRE_TIME on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.17, so it may already be present.

ALTER TABLE QRTZ_TRIGGERS ADD COLUMN MISFIRE_ORIG_FIRE_TIME INTEGER NULL;

-- === 2. EXECUTION_GROUP on QRTZ_TRIGGERS and QRTZ_FIRED_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.18, so it may already be present.

ALTER TABLE QRTZ_TRIGGERS ADD COLUMN EXECUTION_GROUP NVARCHAR(200) NULL;

ALTER TABLE QRTZ_FIRED_TRIGGERS ADD COLUMN EXECUTION_GROUP NVARCHAR(200) NULL;

-- === 3. PREFERRED_NODE and PREFERRED_NODE_AUTO on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.19, so it may already be present.

ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PREFERRED_NODE NVARCHAR(200) NULL;

ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PREFERRED_NODE_AUTO BIT NOT NULL DEFAULT 0;

-- === 4. RETRY_POLICY and RETRY_ATTEMPT on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x, and new in it -- 3.x has no equivalent, so on a database coming
-- from 3.x both columns are always absent. Nullable with no default: an existing row
-- reads as "no retry policy".

ALTER TABLE QRTZ_TRIGGERS ADD COLUMN RETRY_POLICY NVARCHAR(250) NULL;

ALTER TABLE QRTZ_TRIGGERS ADD COLUMN RETRY_ATTEMPT INTEGER NULL;

-- === 5. QRTZ_PAUSED_JOB_GRPS ===
-- REQUIRED for 4.x, and new in it -- 3.x has no equivalent. One row per paused job
-- group, mirroring QRTZ_PAUSED_TRIGGER_GRPS. Guarded on every dialect, SQLite
-- included: CREATE TABLE IF NOT EXISTS is conditional DDL SQLite does have.

CREATE TABLE IF NOT EXISTS QRTZ_PAUSED_JOB_GRPS (
  SCHED_NAME NVARCHAR(120) NOT NULL,
  JOB_GROUP NVARCHAR(150) NOT NULL,
  PRIMARY KEY (SCHED_NAME,JOB_GROUP)
);

-- === 6. Index set ===
-- OPTIONAL: 4.x runs unchanged either way. The creates matter once a schema holds a
-- non-trivial number of triggers; the drops only reclaim write cost and storage.

-- === Create the indexes this version expects ===================================

CREATE INDEX IF NOT EXISTS IDX_QRTZ_J_G_N ON QRTZ_JOB_DETAILS(SCHED_NAME,JOB_GROUP,JOB_NAME);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_T_J ON QRTZ_TRIGGERS(SCHED_NAME,JOB_NAME,JOB_GROUP);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_T_G_N ON QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_GROUP,TRIGGER_NAME);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_T_C ON QRTZ_TRIGGERS(SCHED_NAME,CALENDAR_NAME);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_T_NFT_ST ON QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_STATE,NEXT_FIRE_TIME);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_FT_INST_JOB_REQ_RCVRY ON QRTZ_FIRED_TRIGGERS(SCHED_NAME,INSTANCE_NAME,REQUESTS_RECOVERY);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_FT_J_G ON QRTZ_FIRED_TRIGGERS(SCHED_NAME,JOB_NAME,JOB_GROUP);

CREATE INDEX IF NOT EXISTS IDX_QRTZ_FT_T_G ON QRTZ_FIRED_TRIGGERS(SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP);

-- === Drop the ones it no longer uses ==========================================
-- Guarded, so each is a no-op when that index is not present.

DROP INDEX IF EXISTS IDX_QRTZ_J_GRP;

DROP INDEX IF EXISTS IDX_QRTZ_J_REQ_RECOVERY;

DROP INDEX IF EXISTS IDX_QRTZ_T_G_J;

DROP INDEX IF EXISTS IDX_QRTZ_T_JG;

DROP INDEX IF EXISTS IDX_QRTZ_T_G;

DROP INDEX IF EXISTS IDX_QRTZ_T_STATE;

DROP INDEX IF EXISTS IDX_QRTZ_T_N_STATE;

DROP INDEX IF EXISTS IDX_QRTZ_T_N_G_STATE;

DROP INDEX IF EXISTS IDX_QRTZ_T_NEXT_FIRE_TIME;

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
