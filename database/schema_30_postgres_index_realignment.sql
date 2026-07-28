-- Quartz.NET schema migration: realign the PostgreSQL indexes with the statements AdoJobStore runs
--
-- PostgreSQL only. Optional but strongly recommended -- nothing breaks without it. Every
-- AdoJobStore statement filters SCHED_NAME first, yet 9 of the 11 indexes previously created by
-- tables_postgres.sql did not lead with SCHED_NAME, so the planner could not seek on them, and
-- IDX_QRTZ_T_NFT_ST had its two columns in the wrong order (see below). The names and column
-- orders below are exactly what tables_postgres.sql now creates, so a database created from the
-- current script needs nothing from this file.
--
-- Replace 'qrtz_' with your configured table prefix if different.
--
-- New indexes are created before anything is dropped. The three indexes whose *name* is unchanged
-- but whose *columns* changed have to be dropped first, because CREATE INDEX IF NOT EXISTS would
-- otherwise silently keep the old, wrong shape.
--
-- On a busy database use CREATE INDEX CONCURRENTLY / DROP INDEX CONCURRENTLY instead; neither can
-- run inside a transaction block, so run those statements one at a time.

-- === New indexes ============================================================================

-- SelectJobsInGroup / SelectJobGroups: SCHED_NAME = ? AND JOB_GROUP = ?
CREATE INDEX IF NOT EXISTS idx_qrtz_j_g_n ON qrtz_job_details (sched_name, job_group, job_name);

-- SelectTriggersForJob and the job delete/replace paths: SCHED_NAME = ? AND JOB_NAME = ? AND JOB_GROUP = ?
CREATE INDEX IF NOT EXISTS idx_qrtz_t_j ON qrtz_triggers (sched_name, job_name, job_group);

-- Calendar-in-use check and calendar update propagation: SCHED_NAME = ? AND CALENDAR_NAME = ?
CREATE INDEX IF NOT EXISTS idx_qrtz_t_c ON qrtz_triggers (sched_name, calendar_name);

-- Trigger group reads plus group pause/resume: SCHED_NAME = ? AND TRIGGER_GROUP = ?
CREATE INDEX IF NOT EXISTS idx_qrtz_t_g_n ON qrtz_triggers (sched_name, trigger_group, trigger_name);

-- Cluster failover recovery: SCHED_NAME = ? AND INSTANCE_NAME = ? (+ REQUESTS_RECOVERY)
CREATE INDEX IF NOT EXISTS idx_qrtz_ft_inst_job_req_rcvry ON qrtz_fired_triggers (sched_name, instance_name, requests_recovery);

-- DisallowConcurrentExecution checks: SCHED_NAME = ? AND JOB_NAME = ? AND JOB_GROUP = ?
CREATE INDEX IF NOT EXISTS idx_qrtz_ft_j_g ON qrtz_fired_triggers (sched_name, job_name, job_group);

-- Fired-trigger rows of one trigger; same columns as the old idx_qrtz_ft_trig_nm_gp, under the name
-- the other dialects already use
CREATE INDEX IF NOT EXISTS idx_qrtz_ft_t_g ON qrtz_fired_triggers (sched_name, trigger_name, trigger_group);

-- === Reshaped indexes (same name, different columns: drop first, then recreate) ==============

-- Was (requests_recovery). No statement filters qrtz_job_details.requests_recovery on its own, so
-- this index earns nothing either way; it is kept only so PostgreSQL does not diverge from the
-- other dialect scripts. Kept means reshaped: sched_name has to lead, like everywhere else.
DROP INDEX IF EXISTS idx_qrtz_j_req_recovery;
CREATE INDEX IF NOT EXISTS idx_qrtz_j_req_recovery ON qrtz_job_details (sched_name, requests_recovery);

-- Was (next_fire_time). The only statement shaped SCHED_NAME = ? AND NEXT_FIRE_TIME < ? with no
-- state filter is SelectMisfiredTriggers; the misfire sweeps that actually run also filter
-- TRIGGER_STATE and are better served by idx_qrtz_t_nft_st. Kept for parity with the other
-- dialect scripts, reshaped so sched_name leads.
DROP INDEX IF EXISTS idx_qrtz_t_next_fire_time;
CREATE INDEX IF NOT EXISTS idx_qrtz_t_next_fire_time ON qrtz_triggers (sched_name, next_fire_time);

-- Was (next_fire_time, trigger_state) -- the columns were REVERSED. The acquire statement is
-- SCHED_NAME = ? AND TRIGGER_STATE = ? AND NEXT_FIRE_TIME <= ? ORDER BY NEXT_FIRE_TIME: two
-- equalities followed by a range, so the range column must come last. Leading with next_fire_time
-- made the range the first column, which meant scanning every trigger below noLaterThan across
-- every scheduler in the table and filtering the state afterwards. This is the single most
-- valuable statement in this file; the acquire loop runs it continuously.
DROP INDEX IF EXISTS idx_qrtz_t_nft_st;
CREATE INDEX IF NOT EXISTS idx_qrtz_t_nft_st ON qrtz_triggers (sched_name, trigger_state, next_fire_time);

-- === Redundant indexes ======================================================================

-- Leftmost prefix of the reordered idx_qrtz_t_nft_st (sched_name, trigger_state, next_fire_time)
DROP INDEX IF EXISTS idx_qrtz_t_state;

-- Replaced by idx_qrtz_ft_t_g above: identical columns, renamed to match the other dialects
DROP INDEX IF EXISTS idx_qrtz_ft_trig_nm_gp;

-- Single-column indexes on qrtz_fired_triggers. No statement filters any of these columns without
-- also filtering sched_name, so none of them can serve a seek, and the composite indexes created
-- above cover every predicate that reaches this table. Group-only administrative reads fall back
-- to a sequential scan, which is cheap here: qrtz_fired_triggers only ever holds in-flight rows.
DROP INDEX IF EXISTS idx_qrtz_ft_trig_name;          -- covered by idx_qrtz_ft_t_g
DROP INDEX IF EXISTS idx_qrtz_ft_trig_group;         -- group-only read, see the note above
DROP INDEX IF EXISTS idx_qrtz_ft_trig_inst_name;     -- covered by idx_qrtz_ft_inst_job_req_rcvry
DROP INDEX IF EXISTS idx_qrtz_ft_job_name;           -- covered by idx_qrtz_ft_j_g
DROP INDEX IF EXISTS idx_qrtz_ft_job_group;          -- group-only read, see the note above
DROP INDEX IF EXISTS idx_qrtz_ft_job_req_recovery;   -- covered by idx_qrtz_ft_inst_job_req_rcvry
