--
-- Quartz.NET schema migration -- add EXECUTION_GROUP
--
-- Introduced in Quartz.NET 3.18.0 (#3004)
--
-- Firebird only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   3.x  OPTIONAL. Without it execution groups still work, but the per-node limit is
--        applied by in-memory filtering after acquisition rather than in the acquire
--        query. The job store probes at startup.
--
--   4.x  REQUIRED. 4.x removed the startup probe and assumes the column exists. When
--        upgrading from 3.x run ../4.0/schema_30_to_40_upgrade_firebird.sql instead -- it
--        folds this change in.
--
-- Carries the execution group tag that per-node thread limits are enforced against.
-- Both tables must be altered together.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

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
