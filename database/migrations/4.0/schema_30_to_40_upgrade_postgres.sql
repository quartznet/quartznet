--
-- Quartz.NET schema migration -- 3.x to 4.0
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
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
--   4.x also adds a table 3.x never had, QRTZ_PAUSED_JOB_GRPS, and validates its whole
--   schema at startup -- so this script is required even for a 3.x database that took
--   every optional migration going.
--
-- This script supersedes the optional per-feature migrations in ../3.17, ../3.18,
-- ../3.19 and ../3.20 -- it applies everything they do. If you already ran some of
-- them, run this anyway: every statement checks first, so it is safe on a
-- partially-migrated database.
--
-- Sections, in order:
--   1. MISFIRE_ORIG_FIRE_TIME column                REQUIRED
--   2. EXECUTION_GROUP columns                      REQUIRED
--   3. PREFERRED_NODE / PREFERRED_NODE_AUTO         REQUIRED
--   4. QRTZ_PAUSED_JOB_GRPS table                   REQUIRED
--   5. Index set aligned with the 4.x schema        optional
--
-- Run the sections in order: the drops in section 5 assume the creates above them have
-- already succeeded.
--
-- Section 4 has no 3.x counterpart. 3.x pauses a job group without recording it anywhere,
-- so a paused job group could not be listed or asked about; 4.x keeps the group names in
-- QRTZ_PAUSED_JOB_GRPS, which is what makes JobGroup.Paused answer truthfully and what
-- carries the pause across a restart (#3336).
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === 1. MISFIRE_ORIG_FIRE_TIME on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.17, so it may already be present.

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'misfire_orig_fire_time') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN misfire_orig_fire_time bigint null;
  END IF;
END $$;

-- === 2. EXECUTION_GROUP on QRTZ_TRIGGERS and QRTZ_FIRED_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.18, so it may already be present.

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'execution_group') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN execution_group varchar(200) null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_fired_triggers' AND column_name = 'execution_group') THEN
    ALTER TABLE qrtz_fired_triggers ADD COLUMN execution_group varchar(200) null;
  END IF;
END $$;

-- === 3. PREFERRED_NODE and PREFERRED_NODE_AUTO on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.19, so it may already be present.

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'preferred_node') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN preferred_node varchar(200) null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'preferred_node_auto') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN preferred_node_auto bool not null default false;
  END IF;
END $$;

-- === 4. QRTZ_PAUSED_JOB_GRPS ===
-- REQUIRED for 4.x, and new in it -- 3.x has no equivalent. One row per paused job
-- group, mirroring QRTZ_PAUSED_TRIGGER_GRPS. Guarded on every dialect, SQLite
-- included: CREATE TABLE IF NOT EXISTS is conditional DDL SQLite does have.

CREATE TABLE IF NOT EXISTS qrtz_paused_job_grps (
  sched_name TEXT NOT NULL,
  job_group TEXT NOT NULL,
  PRIMARY KEY (sched_name, job_group)
);

-- === 5. Index set ===
-- OPTIONAL: 4.x runs unchanged either way. The creates matter once a schema holds a
-- non-trivial number of triggers; the drops only reclaim write cost and storage.

-- === Drop the indexes whose columns changed but whose name did not ============
-- These have to go first: CREATE INDEX IF NOT EXISTS below would find the name
-- already taken and silently keep the old, wrong column order.

DROP INDEX IF EXISTS idx_qrtz_t_nft_st;

-- === Create the indexes this version expects ===================================

CREATE INDEX IF NOT EXISTS idx_qrtz_j_g_n ON qrtz_job_details (sched_name, job_group, job_name);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_j ON qrtz_triggers (sched_name, job_name, job_group);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_g_n ON qrtz_triggers (sched_name, trigger_group, trigger_name);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_c ON qrtz_triggers (sched_name, calendar_name);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_nft_st ON qrtz_triggers (sched_name, trigger_state, next_fire_time);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_inst_job_req_rcvry ON qrtz_fired_triggers (sched_name, instance_name, requests_recovery);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_j_g ON qrtz_fired_triggers (sched_name, job_name, job_group);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_t_g ON qrtz_fired_triggers (sched_name, trigger_name, trigger_group);

-- === Drop the ones it no longer uses ==========================================
-- Guarded, so each is a no-op when that index is not present.

DROP INDEX IF EXISTS idx_qrtz_j_grp;

DROP INDEX IF EXISTS idx_qrtz_j_req_recovery;

DROP INDEX IF EXISTS idx_qrtz_t_g_j;

DROP INDEX IF EXISTS idx_qrtz_t_jg;

DROP INDEX IF EXISTS idx_qrtz_t_g;

DROP INDEX IF EXISTS idx_qrtz_t_state;

DROP INDEX IF EXISTS idx_qrtz_t_n_state;

DROP INDEX IF EXISTS idx_qrtz_t_n_g_state;

DROP INDEX IF EXISTS idx_qrtz_t_next_fire_time;

DROP INDEX IF EXISTS idx_qrtz_t_nft_misfire;

DROP INDEX IF EXISTS idx_qrtz_t_nft_st_misfire_grp;

DROP INDEX IF EXISTS idx_qrtz_t_nft_st_misfire;

DROP INDEX IF EXISTS idx_qrtz_ft_g_j;

DROP INDEX IF EXISTS idx_qrtz_ft_g_t;

DROP INDEX IF EXISTS idx_qrtz_ft_jg;

DROP INDEX IF EXISTS idx_qrtz_ft_tg;

DROP INDEX IF EXISTS idx_qrtz_ft_trig_inst_name;

DROP INDEX IF EXISTS idx_qrtz_ft_trig_nm_gp;

DROP INDEX IF EXISTS idx_qrtz_ft_trig_name;

DROP INDEX IF EXISTS idx_qrtz_ft_trig_group;

DROP INDEX IF EXISTS idx_qrtz_ft_job_name;

DROP INDEX IF EXISTS idx_qrtz_ft_job_group;

DROP INDEX IF EXISTS idx_qrtz_ft_job_req_recovery;
