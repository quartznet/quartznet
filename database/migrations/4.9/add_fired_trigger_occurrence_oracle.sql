--
-- Quartz.NET schema migration -- add the fired trigger occurrence columns
--
-- Introduced in Quartz.NET 4.9.0 (#4272)
--
-- Oracle only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.9  REQUIRED. A 4.9 node writes these columns when a firing starts, and reads them
--        when it recovers one, so it refuses to start against a database without them.
--
--        Safe to run while 4.8 nodes are still up: the columns are nullable with no default,
--        so every existing row is already valid, and a 4.8 node never names them. A firing
--        a 4.8 node runs recovers at the time its attempt was due with attempt 0, as it
--        always has.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_oracle.sql and every later migration first;
--        this file is what 4.9 adds on top of them.
--
-- OCCURRENCE_TIME is the occurrence the firing serves, in ticks like SCHED_TIME: a
-- misfire's original fire time, else the previous fire time the fire set or left alone.
-- RETRY_ATTEMPT is which attempt at that occurrence the firing is. A firing writes
-- both when it starts; a reservation leaves both NULL. SCHED_TIME keeps meaning when
-- the attempt was due.
--
-- A recovery starts its trigger at OCCURRENCE_TIME, or SCHED_TIME when it is NULL, and
-- restores RETRY_ATTEMPT, or 0 when it is NULL, because a 4.8 node writes neither.
--
-- No index is added. The columns are read with the rest of the row, which recovery
-- already reads, and never searched on.
--
-- BOTH COLUMNS MUST BE ADDED TOGETHER.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_FIRED_TRIGGERS' AND column_name = 'OCCURRENCE_TIME';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_FIRED_TRIGGERS ADD (OCCURRENCE_TIME NUMBER(19) NULL)';
  END IF;
END;
/

DECLARE
  column_exists NUMBER;
BEGIN
  SELECT COUNT(*) INTO column_exists FROM user_tab_columns
  WHERE table_name = 'QRTZ_FIRED_TRIGGERS' AND column_name = 'RETRY_ATTEMPT';
  IF column_exists = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_FIRED_TRIGGERS ADD (RETRY_ATTEMPT NUMBER(13) NULL)';
  END IF;
END;
/
