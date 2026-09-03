--
-- Quartz.NET schema migration -- 3.x to 4.0, index set
--
-- Oracle only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   OPTIONAL: 4.x runs unchanged either way. The creates matter once a schema holds a
--   non-trivial number of triggers; the drops only reclaim write cost and storage.
--
--   NOT to be run while any 3.x node is still up -- see below.
--
-- Run schema_30_to_40_upgrade_oracle.sql first. That one is mandatory and this one is
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
  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_NFT_ST_MISFIRE';
  IF index_exists > 0 THEN
    EXECUTE IMMEDIATE 'DROP INDEX IDX_QRTZ_T_NFT_ST_MISFIRE';
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
