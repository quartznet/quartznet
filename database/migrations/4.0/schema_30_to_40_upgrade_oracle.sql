--
-- Quartz.NET schema migration -- 3.x to 4.0
--
-- Oracle only. Run the file matching your database; the other dialects live
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
-- ../3.19 and ../3.20 -- it applies everything they do. If you already ran some of
-- them, run this anyway: every statement checks first, so it is safe on a
-- partially-migrated database.
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
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === 1. MISFIRE_ORIG_FIRE_TIME on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.17, so it may already be present.

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_TRIGGERS' AND column_name = 'MISFIRE_ORIG_FIRE_TIME';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_TRIGGERS ADD (MISFIRE_ORIG_FIRE_TIME NUMBER(19) NULL)';
  END IF;
END;
/

-- === 2. EXECUTION_GROUP on QRTZ_TRIGGERS and QRTZ_FIRED_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.18, so it may already be present.

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_TRIGGERS' AND column_name = 'EXECUTION_GROUP';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200) NULL)';
  END IF;
END;
/

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_FIRED_TRIGGERS' AND column_name = 'EXECUTION_GROUP';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_FIRED_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200) NULL)';
  END IF;
END;
/

-- === 3. PREFERRED_NODE and PREFERRED_NODE_AUTO on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.19, so it may already be present.

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_TRIGGERS' AND column_name = 'PREFERRED_NODE';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_TRIGGERS ADD (PREFERRED_NODE VARCHAR2(200) NULL)';
  END IF;
END;
/

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_TRIGGERS' AND column_name = 'PREFERRED_NODE_AUTO';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_TRIGGERS ADD (PREFERRED_NODE_AUTO VARCHAR2(1) DEFAULT ''0'' NOT NULL)';
  END IF;
END;
/

-- === 4. RETRY_POLICY and RETRY_ATTEMPT on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x, and new in it -- 3.x has no equivalent, so on a database coming
-- from 3.x both columns are always absent. Nullable with no default: an existing row
-- reads as "no retry policy".

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_TRIGGERS' AND column_name = 'RETRY_POLICY';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_TRIGGERS ADD (RETRY_POLICY VARCHAR2(250) NULL)';
  END IF;
END;
/

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_TRIGGERS' AND column_name = 'RETRY_ATTEMPT';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_TRIGGERS ADD (RETRY_ATTEMPT NUMBER(13) NULL)';
  END IF;
END;
/

-- === 5. QRTZ_PAUSED_JOB_GRPS ===
-- REQUIRED for 4.x, and new in it -- 3.x has no equivalent. One row per paused job
-- group, mirroring QRTZ_PAUSED_TRIGGER_GRPS. Guarded on every dialect, SQLite
-- included: CREATE TABLE IF NOT EXISTS is conditional DDL SQLite does have.

DECLARE
  table_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO table_exists FROM user_tables
  WHERE table_name = 'QRTZ_PAUSED_JOB_GRPS';
  IF table_exists = 0 THEN
    EXECUTE IMMEDIATE 'CREATE TABLE QRTZ_PAUSED_JOB_GRPS (SCHED_NAME VARCHAR2(120) NOT NULL, JOB_GROUP VARCHAR2(200) NOT NULL, CONSTRAINT QRTZ_PAUSED_JOB_GRPS_PK PRIMARY KEY (SCHED_NAME,JOB_GROUP))';
  END IF;
END;
/

-- === 6. Index set ===
-- OPTIONAL: 4.x runs unchanged either way. The creates matter once a schema holds a
-- non-trivial number of triggers; the drops only reclaim write cost and storage.

-- === Drop the indexes whose columns changed but whose name did not ============
-- These have to go first: CREATE INDEX IF NOT EXISTS below would find the name
-- already taken and silently keep the old, wrong column order.

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_NFT_ST';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_T_NFT_ST';
  END IF;
END;
/

-- === Create the indexes this version expects ===================================

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_J_G_N';
  IF index_exists = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IDX_QRTZ_J_G_N ON QRTZ_JOB_DETAILS(SCHED_NAME,JOB_GROUP,JOB_NAME)';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_J';
  IF index_exists = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IDX_QRTZ_T_J ON QRTZ_TRIGGERS(SCHED_NAME,JOB_NAME,JOB_GROUP)';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_G_N';
  IF index_exists = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IDX_QRTZ_T_G_N ON QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_GROUP,TRIGGER_NAME)';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_C';
  IF index_exists = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IDX_QRTZ_T_C ON QRTZ_TRIGGERS(SCHED_NAME,CALENDAR_NAME)';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_NFT_ST';
  IF index_exists = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IDX_QRTZ_T_NFT_ST ON QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_STATE,NEXT_FIRE_TIME ASC,PRIORITY DESC,MISFIRE_INSTR)';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_NFT_ST_MISFIRE';
  IF index_exists = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IDX_QRTZ_T_NFT_ST_MISFIRE ON QRTZ_TRIGGERS(SCHED_NAME,MISFIRE_INSTR,NEXT_FIRE_TIME,TRIGGER_STATE)';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_INST_JOB_REQ_RCVRY';
  IF index_exists = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IDX_QRTZ_FT_INST_JOB_REQ_RCVRY ON QRTZ_FIRED_TRIGGERS(SCHED_NAME,INSTANCE_NAME,REQUESTS_RECOVERY)';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_J_G';
  IF index_exists = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IDX_QRTZ_FT_J_G ON QRTZ_FIRED_TRIGGERS(SCHED_NAME,JOB_NAME,JOB_GROUP)';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_T_G';
  IF index_exists = 0 THEN
    EXECUTE IMMEDIATE 'CREATE INDEX IDX_QRTZ_FT_T_G ON QRTZ_FIRED_TRIGGERS(SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP)';
  END IF;
END;
/

-- === Drop the ones it no longer uses ==========================================
-- Guarded, so each is a no-op when that index is not present.

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_J_GRP';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_J_GRP';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_J_REQ_RECOVERY';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_J_REQ_RECOVERY';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_G_J';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_T_G_J';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_JG';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_T_JG';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_G';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_T_G';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_STATE';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_T_STATE';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_N_STATE';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_T_N_STATE';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_N_G_STATE';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_T_N_G_STATE';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_NEXT_FIRE_TIME';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_T_NEXT_FIRE_TIME';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_NFT_MISFIRE';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_T_NFT_MISFIRE';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_NFT_ST_MISFIRE_GRP';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_T_NFT_ST_MISFIRE_GRP';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_G_J';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_FT_G_J';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_G_T';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_FT_G_T';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_JG';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_FT_JG';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_TG';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_FT_TG';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_TRIG_INST_NAME';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_FT_TRIG_INST_NAME';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_TRIG_NM_GP';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_FT_TRIG_NM_GP';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_TRIG_NAME';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_FT_TRIG_NAME';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_TRIG_GROUP';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_FT_TRIG_GROUP';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_JOB_NAME';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_FT_JOB_NAME';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_JOB_GROUP';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_FT_JOB_GROUP';
  END IF;
END;
/

DECLARE
  index_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_FT_JOB_REQ_RECOVERY';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_FT_JOB_REQ_RECOVERY';
  END IF;
END;
/
