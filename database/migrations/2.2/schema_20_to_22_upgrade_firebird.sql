--
-- Quartz.NET schema migration -- 2.0 to 2.2
--
-- Firebird only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   REQUIRED when upgrading from 2.0/2.1 to 2.2 or later with AdoJobStore.
--
-- Adds SCHED_TIME to QRTZ_FIRED_TRIGGERS so recovery jobs see both the scheduled and
-- the actual fire time (#113).
--
-- The column is NOT NULL with no default, so the ALTER fails on a table that already
-- holds rows. QRTZ_FIRED_TRIGGERS only ever holds in-flight entries, so stop the
-- scheduler and clear it first:
--
--   DELETE FROM QRTZ_FIRED_TRIGGERS;
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
                 WHERE TRIM(RDB$RELATION_NAME) = 'QRTZ_FIRED_TRIGGERS'
                   AND TRIM(RDB$FIELD_NAME) = 'SCHED_TIME')) THEN
    EXECUTE STATEMENT 'ALTER TABLE QRTZ_FIRED_TRIGGERS ADD SCHED_TIME BIGINT NOT NULL';
END^
SET TERM ; ^
COMMIT;
