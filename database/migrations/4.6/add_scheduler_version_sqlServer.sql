--
-- Quartz.NET schema migration -- add the scheduler version column
--
-- Introduced in Quartz.NET 4.6.0 (#4067)
--
-- SQL Server only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.6  REQUIRED. A 4.6 node writes this column at every check-in and reads it for every
--        live node, so it refuses to start against a database without it.
--
--        Safe to run while 4.5 nodes are still up: the column is nullable with no default,
--        so every existing row is already valid, and a 4.5 node never names it. A 4.5
--        node's row therefore reads NULL, which a 4.6 node reads as 'older than 4.6'.
--
--        Run it, then roll every node to 4.6. A 4.6 node keeps taking TRIGGER_ACCESS for
--        every firing round while any live row reads NULL or a version below 4.6, and goes
--        lock-free on its next check-in after the last such row ages out. Nothing has to be
--        configured for the rollout; the column is what coordinates it.
--
--   4.1  Run ../4.2/add_continuations_sqlServer.sql first on a database created by
--        4.0 or 4.1, and every later required migration.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_sqlServer.sql and every later migration first;
--        this file is what 4.6 adds on top of them.
--
-- QUARTZ_VERSION is the Quartz version the node runs, as major.minor.patch ('4.6.0'),
-- written with LAST_CHECKIN_TIME at every check-in. NULL is a node older than 4.6, or a
-- row an older node inserted. A node's first check-in recovers and deletes its own stale
-- row before it writes a fresh one, so a node downgraded under the same instance id never
-- keeps a newer stamp.
--
-- A 4.6 node fires a round of triggers whose jobs allow concurrent execution without the
-- cluster's TRIGGER_ACCESS lock only while every live row -- one not past its check-in
-- failure threshold -- carries 4.6 or later. The lock is what kept a 4.5 node's misfire
-- and edit writes from landing over such a round, and a 4.6 node cannot guard writes it
-- does not make.
--
-- No index is added. The column is read with the rest of the row, which the check-in
-- already reads, and never searched on.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

IF COL_LENGTH('QRTZ_SCHEDULER_STATE','QUARTZ_VERSION') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_SCHEDULER_STATE] ADD [QUARTZ_VERSION] nvarchar(20) NULL;
END
GO
