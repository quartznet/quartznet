--
-- Quartz.NET schema migration -- add the fire progress columns
--
-- Introduced in Quartz.NET 4.3.0 (#3874)
--
-- Firebird only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.3  REQUIRED. A 4.3 node writes these two columns when a running job reports its
--        progress and reads them whenever it lists what is executing, so it refuses to
--        start against a database without them.
--
--        Safe to run while 4.2 nodes are still up: the columns are nullable with no
--        default, so every existing row is already valid, and a 4.2 node never names them.
--        A firing a 4.2 node runs reads as one that has reported no progress.
--
--   4.1  Run ../4.2/add_continuations_firebird.sql first on a database created by
--        4.0 or 4.1.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_firebird.sql and every later migration first;
--        this file is what 4.3 adds on top of them.
--
-- PROGRESS is the percentage, 0 to 100, a running job last passed to
-- IJobExecutionContext.ReportProgress; PROGRESS_MESSAGE is the message it passed with it,
-- truncated to 250 characters. The scheduler writes them at most once a second per firing
-- and only when they change, by ENTRY_ID -- the row belongs to the node writing it, so no
-- lock is taken. The row is deleted when the firing completes, as it always was.
--
-- No index is added. The write is by primary key, and the read is the fire-instance listing,
-- which already reads this table.
--
-- BOTH COLUMNS MUST BE ADDED TOGETHER.
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
                   AND TRIM(RDB$FIELD_NAME) = 'PROGRESS')) THEN
    EXECUTE STATEMENT 'ALTER TABLE QRTZ_FIRED_TRIGGERS ADD PROGRESS INTEGER DEFAULT NULL';
END^
SET TERM ; ^
COMMIT;

SET TERM ^ ;
EXECUTE BLOCK AS
BEGIN
  IF (NOT EXISTS(SELECT 1 FROM RDB$RELATION_FIELDS
                 WHERE TRIM(RDB$RELATION_NAME) = 'QRTZ_FIRED_TRIGGERS'
                   AND TRIM(RDB$FIELD_NAME) = 'PROGRESS_MESSAGE')) THEN
    EXECUTE STATEMENT 'ALTER TABLE QRTZ_FIRED_TRIGGERS ADD PROGRESS_MESSAGE VARCHAR(250) DEFAULT NULL';
END^
SET TERM ; ^
COMMIT;
