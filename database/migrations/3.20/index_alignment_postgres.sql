--
-- Quartz.NET schema migration -- align indexes with the 3.x schema
--
-- Introduced in Quartz.NET 3.20.0 (#3203)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   3.x  OPTIONAL, performance only. Nothing stops working if it is not applied, but
--        several of these indexes could not serve a single-scheduler lookup at all.
--
--   4.x  Superseded. ../4.0/schema_30_to_40_indexes_postgres.sql converges the same index
--        set onto the 4.x shape -- run that instead when upgrading to 4.x.
--
-- Brings an existing database's index set in line with what the current
-- database/tables/tables_postgres.sql creates. A database created from the current
-- script already matches and needs nothing from this file.
--
-- Every Quartz statement filters SCHED_NAME first, so every index here leads with it.
-- Indexes that are a leftmost prefix of a wider one, or that no statement can drive a
-- scan from, are dropped.
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

DROP INDEX IF EXISTS idx_qrtz_j_req_recovery;

DROP INDEX IF EXISTS idx_qrtz_t_next_fire_time;

DROP INDEX IF EXISTS idx_qrtz_t_nft_st;

-- === Create the indexes this version expects ===================================

CREATE INDEX IF NOT EXISTS idx_qrtz_j_req_recovery ON qrtz_job_details (sched_name, requests_recovery);

CREATE INDEX IF NOT EXISTS idx_qrtz_j_g_n ON qrtz_job_details (sched_name, job_group, job_name);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_j ON qrtz_triggers (sched_name, job_name, job_group);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_c ON qrtz_triggers (sched_name, calendar_name);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_g_n ON qrtz_triggers (sched_name, trigger_group, trigger_name);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_next_fire_time ON qrtz_triggers (sched_name, next_fire_time);

CREATE INDEX IF NOT EXISTS idx_qrtz_t_nft_st ON qrtz_triggers (sched_name, trigger_state, next_fire_time);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_inst_job_req_rcvry ON qrtz_fired_triggers (sched_name, instance_name, requests_recovery);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_j_g ON qrtz_fired_triggers (sched_name, job_name, job_group);

CREATE INDEX IF NOT EXISTS idx_qrtz_ft_t_g ON qrtz_fired_triggers (sched_name, trigger_name, trigger_group);

-- === Drop the ones it no longer uses ==========================================
-- Guarded, so each is a no-op when that index is not present.

DROP INDEX IF EXISTS idx_qrtz_j_grp;

DROP INDEX IF EXISTS idx_qrtz_t_g_j;

DROP INDEX IF EXISTS idx_qrtz_t_jg;

DROP INDEX IF EXISTS idx_qrtz_t_g;

DROP INDEX IF EXISTS idx_qrtz_t_state;

DROP INDEX IF EXISTS idx_qrtz_t_n_state;

DROP INDEX IF EXISTS idx_qrtz_t_n_g_state;

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
