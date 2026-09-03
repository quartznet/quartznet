--
-- Quartz.NET schema migration -- 3.x to 4.0
--
-- Firebird only. Run the file matching your database; the other dialects live
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
-- This script supersedes the optional per-feature migrations in ../3.17, ../3.18 and
-- ../3.19 -- it applies everything they do. If you already ran some of them, run this
-- anyway: every statement checks first, so it is safe on a partially-migrated database.
-- (../3.20's index alignment is superseded by schema_30_to_40_indexes_firebird.sql,
-- beside this file.)
--
-- Sections, in order:
--   1. MISFIRE_ORIG_FIRE_TIME column                REQUIRED
--   2. EXECUTION_GROUP columns                      REQUIRED
--   3. PREFERRED_NODE / PREFERRED_NODE_AUTO         REQUIRED
--   4. RETRY_POLICY / RETRY_ATTEMPT                 REQUIRED
--   5. QRTZ_PAUSED_JOB_GRPS table                   REQUIRED
--
-- Every section in this file is safe to run while 3.x nodes are still up, which is why the
-- index set is not in it. That is a second file in this folder --
-- schema_30_to_40_indexes_firebird.sql -- and it is NOT safe to run during a mixed window:
-- it drops IDX_QRTZ_T_NFT_ST_MISFIRE, which 3.x drives its misfire sweep from and 4.x does
-- not read at all (#3656). Run this file now; run that one once the last 3.x node has shut
-- down, or straight afterwards on an upgrade with nothing running.
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

SET TERM ^ ;
EXECUTE BLOCK AS
BEGIN
  IF (NOT EXISTS(SELECT 1 FROM RDB$RELATION_FIELDS
                 WHERE TRIM(RDB$RELATION_NAME) = 'QRTZ_TRIGGERS'
                   AND TRIM(RDB$FIELD_NAME) = 'MISFIRE_ORIG_FIRE_TIME')) THEN
    EXECUTE STATEMENT 'ALTER TABLE QRTZ_TRIGGERS ADD MISFIRE_ORIG_FIRE_TIME BIGINT DEFAULT NULL';
END^
SET TERM ; ^
COMMIT;

-- === 2. EXECUTION_GROUP on QRTZ_TRIGGERS and QRTZ_FIRED_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.18, so it may already be present.

SET TERM ^ ;
EXECUTE BLOCK AS
BEGIN
  IF (NOT EXISTS(SELECT 1 FROM RDB$RELATION_FIELDS
                 WHERE TRIM(RDB$RELATION_NAME) = 'QRTZ_TRIGGERS'
                   AND TRIM(RDB$FIELD_NAME) = 'EXECUTION_GROUP')) THEN
    EXECUTE STATEMENT 'ALTER TABLE QRTZ_TRIGGERS ADD EXECUTION_GROUP VARCHAR(200)';
END^
SET TERM ; ^
COMMIT;

SET TERM ^ ;
EXECUTE BLOCK AS
BEGIN
  IF (NOT EXISTS(SELECT 1 FROM RDB$RELATION_FIELDS
                 WHERE TRIM(RDB$RELATION_NAME) = 'QRTZ_FIRED_TRIGGERS'
                   AND TRIM(RDB$FIELD_NAME) = 'EXECUTION_GROUP')) THEN
    EXECUTE STATEMENT 'ALTER TABLE QRTZ_FIRED_TRIGGERS ADD EXECUTION_GROUP VARCHAR(200)';
END^
SET TERM ; ^
COMMIT;

-- === 3. PREFERRED_NODE and PREFERRED_NODE_AUTO on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.19, so it may already be present.

SET TERM ^ ;
EXECUTE BLOCK AS
BEGIN
  IF (NOT EXISTS(SELECT 1 FROM RDB$RELATION_FIELDS
                 WHERE TRIM(RDB$RELATION_NAME) = 'QRTZ_TRIGGERS'
                   AND TRIM(RDB$FIELD_NAME) = 'PREFERRED_NODE')) THEN
    EXECUTE STATEMENT 'ALTER TABLE QRTZ_TRIGGERS ADD PREFERRED_NODE VARCHAR(200)';
END^
SET TERM ; ^
COMMIT;

SET TERM ^ ;
EXECUTE BLOCK AS
BEGIN
  IF (NOT EXISTS(SELECT 1 FROM RDB$RELATION_FIELDS
                 WHERE TRIM(RDB$RELATION_NAME) = 'QRTZ_TRIGGERS'
                   AND TRIM(RDB$FIELD_NAME) = 'PREFERRED_NODE_AUTO')) THEN
    EXECUTE STATEMENT 'ALTER TABLE QRTZ_TRIGGERS ADD PREFERRED_NODE_AUTO SMALLINT DEFAULT 0 NOT NULL';
END^
SET TERM ; ^
COMMIT;

-- === 4. RETRY_POLICY and RETRY_ATTEMPT on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x, and new in it -- 3.x has no equivalent, so on a database coming
-- from 3.x both columns are always absent. Nullable with no default: an existing row
-- reads as "no retry policy".

SET TERM ^ ;
EXECUTE BLOCK AS
BEGIN
  IF (NOT EXISTS(SELECT 1 FROM RDB$RELATION_FIELDS
                 WHERE TRIM(RDB$RELATION_NAME) = 'QRTZ_TRIGGERS'
                   AND TRIM(RDB$FIELD_NAME) = 'RETRY_POLICY')) THEN
    EXECUTE STATEMENT 'ALTER TABLE QRTZ_TRIGGERS ADD RETRY_POLICY VARCHAR(250)';
END^
SET TERM ; ^
COMMIT;

SET TERM ^ ;
EXECUTE BLOCK AS
BEGIN
  IF (NOT EXISTS(SELECT 1 FROM RDB$RELATION_FIELDS
                 WHERE TRIM(RDB$RELATION_NAME) = 'QRTZ_TRIGGERS'
                   AND TRIM(RDB$FIELD_NAME) = 'RETRY_ATTEMPT')) THEN
    EXECUTE STATEMENT 'ALTER TABLE QRTZ_TRIGGERS ADD RETRY_ATTEMPT INTEGER DEFAULT NULL';
END^
SET TERM ; ^
COMMIT;

-- === 5. QRTZ_PAUSED_JOB_GRPS ===
-- REQUIRED for 4.x, and new in it -- 3.x has no equivalent. One row per paused job
-- group, mirroring QRTZ_PAUSED_TRIGGER_GRPS. Guarded on every dialect, SQLite
-- included: CREATE TABLE IF NOT EXISTS is conditional DDL SQLite does have.

SET TERM ^ ;
EXECUTE BLOCK AS
BEGIN
  IF (NOT EXISTS(SELECT 1 FROM RDB$RELATIONS
                 WHERE TRIM(RDB$RELATION_NAME) = 'QRTZ_PAUSED_JOB_GRPS')) THEN
    EXECUTE STATEMENT 'CREATE TABLE QRTZ_PAUSED_JOB_GRPS (SCHED_NAME VARCHAR(120) NOT NULL, JOB_GROUP VARCHAR(150) NOT NULL, CONSTRAINT PK_QRTZ_PAUSED_JOB_GRPS PRIMARY KEY (SCHED_NAME, JOB_GROUP))';
END^
SET TERM ; ^
COMMIT;
