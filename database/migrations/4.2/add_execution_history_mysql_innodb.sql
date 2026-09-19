--
-- Quartz.NET schema migration -- add the execution history tables
--
-- Introduced in Quartz.NET 4.2.0 (#3771)
--
-- MySQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.2  OPTIONAL. Only a store configured with
--        UsePersistentStore(s => s.UseExecutionHistory()) reads or writes these two
--        tables, and such a store refuses to start without them. Every other scheduler
--        ignores them, so running this file changes nothing until the history is turned
--        on -- and not running it costs nothing while it is off.
--
--        Safe under a mixed cluster, in either direction: a 4.0 or 4.1 node cannot see
--        these tables at all, and a 4.2 node without UseExecutionHistory() neither writes
--        nor reads them. Nodes that do write share one history, which is the point.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_mysql_innodb.sql first; this file is what 4.2 adds
--        on top of it.
--
-- QRTZ_EXECUTION_HISTORY records what a scheduler ran: which node ran it, which job and
-- trigger, when it fired, how long it took, whether it threw and what it said. RUN_TIME is
-- in ticks (100 ns), the unit every instant in this schema is already stored in.
--
-- QRTZ_MISFIRE_HISTORY records what it missed: the trigger, the node that noticed, when it
-- was noticed and the firing that was missed. Nothing ran, so there is no duration and no
-- outcome -- which is why this is a second table rather than a kind column that would leave
-- half of every row null.
--
-- Neither table has a foreign key. A history row outlives the trigger and the job it names,
-- which is the whole point of keeping one, and nothing else in the schema references these
-- tables -- so creating them changes nothing about how the rest of the store behaves.
--
-- ENTRY_ID is written by the store, exactly as QRTZ_FIRED_TRIGGERS.ENTRY_ID is: no dialect
-- here spells an identity column the same way, and nothing reads the number.
--
-- The store keeps both tables trimmed itself, to ExecutionHistoryOptions.Retention (24 hours
-- by default) and MaxEntriesPerScheduler (2,000). The two indexes on each table are what the
-- sweep and the dashboard's node filter read; a search by job or trigger name is a scan.
--
-- MySQL only: the indexes are declared inside CREATE TABLE. MySQL has no
-- CREATE INDEX IF NOT EXISTS, and the guarded form the rest of these scripts use needs a
-- user variable -- which is fine in a client but would double-create here if the table
-- were already present.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === 1. QRTZ_EXECUTION_HISTORY ===

CREATE TABLE IF NOT EXISTS QRTZ_EXECUTION_HISTORY (
  SCHED_NAME VARCHAR(120) NOT NULL,
  ENTRY_ID VARCHAR(140) NOT NULL,
  INSTANCE_NAME VARCHAR(200) NOT NULL,
  JOB_NAME VARCHAR(200) NOT NULL,
  JOB_GROUP VARCHAR(200) NOT NULL,
  TRIGGER_NAME VARCHAR(200) NOT NULL,
  TRIGGER_GROUP VARCHAR(200) NOT NULL,
  FIRED_TIME BIGINT NOT NULL,
  RUN_TIME BIGINT NOT NULL,
  SUCCEEDED BOOLEAN NOT NULL,
  ERROR_MESSAGE VARCHAR(1000) NULL,
  RETRY_ATTEMPT INTEGER NOT NULL DEFAULT 0,
  RETRY_SCHEDULED BOOLEAN NOT NULL DEFAULT FALSE,
  PRIMARY KEY (SCHED_NAME,ENTRY_ID),
  KEY IDX_QRTZ_EH_FIRED_TIME (SCHED_NAME,FIRED_TIME),
  KEY IDX_QRTZ_EH_INST (SCHED_NAME,INSTANCE_NAME)
) ENGINE=InnoDB;

-- === 2. QRTZ_MISFIRE_HISTORY ===

CREATE TABLE IF NOT EXISTS QRTZ_MISFIRE_HISTORY (
  SCHED_NAME VARCHAR(120) NOT NULL,
  ENTRY_ID VARCHAR(140) NOT NULL,
  INSTANCE_NAME VARCHAR(200) NOT NULL,
  TRIGGER_NAME VARCHAR(200) NOT NULL,
  TRIGGER_GROUP VARCHAR(200) NOT NULL,
  JOB_NAME VARCHAR(200) NULL,
  JOB_GROUP VARCHAR(200) NULL,
  MISFIRE_TIME BIGINT NOT NULL,
  SCHED_TIME BIGINT NULL,
  PRIMARY KEY (SCHED_NAME,ENTRY_ID),
  KEY IDX_QRTZ_MH_MISFIRE_TIME (SCHED_NAME,MISFIRE_TIME),
  KEY IDX_QRTZ_MH_INST (SCHED_NAME,INSTANCE_NAME)
) ENGINE=InnoDB;
