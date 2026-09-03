--
-- Quartz.NET schema migration -- 3.x to 4.0, index set
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   OPTIONAL: 4.x runs unchanged either way. The creates matter once a schema holds a
--   non-trivial number of triggers; the drops only reclaim write cost and storage.
--
--   NOT to be run while any 3.x node is still up -- see below.
--
-- Run schema_30_to_40_upgrade_postgres.sql first. That one is mandatory and this one is
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
-- On a busy database use CREATE INDEX CONCURRENTLY / DROP INDEX CONCURRENTLY instead;
-- neither can run inside a transaction block, so run those statements one at a time.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === Drop the indexes whose columns changed but whose name did not ============
-- These have to go first: CREATE INDEX IF NOT EXISTS below would find the name
-- already taken and silently keep the old, wrong column order.

DROP INDEX IF EXISTS idx_qrtz_t_nft_st;

-- === Create the indexes this version expects ===================================

CREATE INDEX IF NOT EXISTS idx_qrtz_j_g_n ON qrtz_job_details (sched_name, job_group, job_name);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_j ON qrtz_triggers (sched_name, job_name, job_group);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_g_n ON qrtz_triggers (sched_name, trigger_group, trigger_name);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_c ON qrtz_triggers (sched_name, calendar_name);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_nft_st ON qrtz_triggers (sched_name, trigger_state, next_fire_time asc, priority desc, misfire_instr);

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
