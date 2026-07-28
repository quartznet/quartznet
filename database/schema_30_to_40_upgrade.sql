/*
Upgrade Quartz.NET schema for SQL Server database (or other database in commented code)
Migration from 3.x to 4.x
*/
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST COPY OF YOUR PRODUCTION !!
--
-- Adds the MISFIRE_ORIG_FIRE_TIME column to the QRTZ_TRIGGERS table.
-- This column stores the original scheduled fire time before misfire handling
-- changes it, enabling correct ScheduledFireTimeUtc in JobExecutionContext.
--
-- This column is REQUIRED for 4.x. Apply the appropriate ALTER TABLE for your database.
-- Replace 'QRTZ_' with your configured table prefix if different.
--
-- NOTE: This column was added as optional in Quartz.NET 3.17. If you are already
-- running 3.17 or later, this column may already exist in your database.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST COPY OF YOUR PRODUCTION !!
--

-- SQL Server
IF COL_LENGTH('QRTZ_TRIGGERS','MISFIRE_ORIG_FIRE_TIME') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_TRIGGERS] ADD [MISFIRE_ORIG_FIRE_TIME] bigint NULL;
END
GO

-- PostgreSQL (check existence before adding)
-- DO $$
-- BEGIN
--   IF NOT EXISTS (SELECT 1 FROM information_schema.columns
--                  WHERE table_name = 'qrtz_triggers' AND column_name = 'misfire_orig_fire_time') THEN
--     ALTER TABLE qrtz_triggers ADD COLUMN misfire_orig_fire_time bigint;
--   END IF;
-- END $$;

-- MySQL (check existence before adding)
-- SET @dbname = DATABASE();
-- SET @tablename = 'QRTZ_TRIGGERS';
-- SET @columnname = 'MISFIRE_ORIG_FIRE_TIME';
-- SET @preparedStatement = (SELECT IF(
--   (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
--    WHERE TABLE_SCHEMA = @dbname AND TABLE_NAME = @tablename AND COLUMN_NAME = @columnname) > 0,
--   'SELECT 1',
--   CONCAT('ALTER TABLE ', @tablename, ' ADD COLUMN ', @columnname, ' BIGINT NULL')
-- ));
-- PREPARE alterIfNotExists FROM @preparedStatement;
-- EXECUTE alterIfNotExists;
-- DEALLOCATE PREPARE alterIfNotExists;

-- SQLite (SQLite does not error on duplicate ADD COLUMN in all versions, but check if needed)
-- ALTER TABLE QRTZ_TRIGGERS ADD COLUMN MISFIRE_ORIG_FIRE_TIME INTEGER;

-- Oracle (check existence before adding)
-- DECLARE
--   column_exists NUMBER;
-- BEGIN
--   SELECT COUNT(*) INTO column_exists FROM user_tab_columns
--   WHERE table_name = 'QRTZ_TRIGGERS' AND column_name = 'MISFIRE_ORIG_FIRE_TIME';
--   IF column_exists = 0 THEN
--     EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_TRIGGERS ADD (MISFIRE_ORIG_FIRE_TIME NUMBER(19))';
--   END IF;
-- END;
-- /

-- Firebird
-- ALTER TABLE QRTZ_TRIGGERS ADD MISFIRE_ORIG_FIRE_TIME BIGINT;

--
-- Adds the EXECUTION_GROUP column to the QRTZ_TRIGGERS and QRTZ_FIRED_TRIGGERS tables.
-- This column stores the execution group tag for per-node thread limit enforcement.
--
-- This column is REQUIRED for 4.x. Apply the appropriate ALTER TABLE for your database.
--
-- NOTE: This column was added as optional in Quartz.NET 3.17. If you are already
-- running 3.17 or later with execution groups, this column may already exist.
--

-- SQL Server
IF COL_LENGTH('QRTZ_TRIGGERS','EXECUTION_GROUP') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_TRIGGERS] ADD [EXECUTION_GROUP] nvarchar(200) NULL;
END
GO

IF COL_LENGTH('QRTZ_FIRED_TRIGGERS','EXECUTION_GROUP') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_FIRED_TRIGGERS] ADD [EXECUTION_GROUP] nvarchar(200) NULL;
END
GO

-- PostgreSQL (check existence before adding)
-- DO $$
-- BEGIN
--   IF NOT EXISTS (SELECT 1 FROM information_schema.columns
--                  WHERE table_name = 'qrtz_triggers' AND column_name = 'execution_group') THEN
--     ALTER TABLE qrtz_triggers ADD COLUMN execution_group VARCHAR(200);
--   END IF;
--   IF NOT EXISTS (SELECT 1 FROM information_schema.columns
--                  WHERE table_name = 'qrtz_fired_triggers' AND column_name = 'execution_group') THEN
--     ALTER TABLE qrtz_fired_triggers ADD COLUMN execution_group VARCHAR(200);
--   END IF;
-- END $$;

-- MySQL (check existence before adding)
-- SET @dbname = DATABASE();
-- SET @preparedStatement = (SELECT IF(
--   (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
--    WHERE TABLE_SCHEMA = @dbname AND TABLE_NAME = 'QRTZ_TRIGGERS' AND COLUMN_NAME = 'EXECUTION_GROUP') > 0,
--   'SELECT 1',
--   'ALTER TABLE QRTZ_TRIGGERS ADD COLUMN EXECUTION_GROUP VARCHAR(200) NULL'
-- ));
-- PREPARE alterIfNotExists FROM @preparedStatement;
-- EXECUTE alterIfNotExists;
-- DEALLOCATE PREPARE alterIfNotExists;
--
-- SET @preparedStatement = (SELECT IF(
--   (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
--    WHERE TABLE_SCHEMA = @dbname AND TABLE_NAME = 'QRTZ_FIRED_TRIGGERS' AND COLUMN_NAME = 'EXECUTION_GROUP') > 0,
--   'SELECT 1',
--   'ALTER TABLE QRTZ_FIRED_TRIGGERS ADD COLUMN EXECUTION_GROUP VARCHAR(200) NULL'
-- ));
-- PREPARE alterIfNotExists FROM @preparedStatement;
-- EXECUTE alterIfNotExists;
-- DEALLOCATE PREPARE alterIfNotExists;

-- SQLite
-- ALTER TABLE QRTZ_TRIGGERS ADD COLUMN EXECUTION_GROUP NVARCHAR(200);
-- ALTER TABLE QRTZ_FIRED_TRIGGERS ADD COLUMN EXECUTION_GROUP NVARCHAR(200);

-- Oracle (check existence before adding)
-- DECLARE
--   column_exists NUMBER;
-- BEGIN
--   SELECT COUNT(*) INTO column_exists FROM user_tab_columns
--   WHERE table_name = 'QRTZ_TRIGGERS' AND column_name = 'EXECUTION_GROUP';
--   IF column_exists = 0 THEN
--     EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200))';
--   END IF;
--   SELECT COUNT(*) INTO column_exists FROM user_tab_columns
--   WHERE table_name = 'QRTZ_FIRED_TRIGGERS' AND column_name = 'EXECUTION_GROUP';
--   IF column_exists = 0 THEN
--     EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_FIRED_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200))';
--   END IF;
-- END;
-- /

-- Firebird
-- ALTER TABLE QRTZ_TRIGGERS ADD EXECUTION_GROUP VARCHAR(200);
-- ALTER TABLE QRTZ_FIRED_TRIGGERS ADD EXECUTION_GROUP VARCHAR(200);

--
-- Adds the PREFERRED_NODE and PREFERRED_NODE_AUTO columns to the QRTZ_TRIGGERS table.
-- These support preferred node (node affinity / trigger pinning in clusters):
-- PREFERRED_NODE holds the target node's instance id verbatim, or the "*" sentinel
-- requesting auto-pin; PREFERRED_NODE_AUTO records whether that pin was claimed
-- automatically by the node that first fired the trigger (auto-claimed pins are released
-- back to "*" when their node dies, explicit pins are preserved).
--
-- These columns are REQUIRED for 4.x. Apply the appropriate ALTER TABLE for your database.
--
-- NOTE: These columns were added as optional in Quartz.NET 3.19. If you are already
-- running 3.19 or later with node affinity, they may already exist. The 3.x and 4.x
-- representations are identical, so no data migration is needed.
--

-- SQL Server
IF COL_LENGTH('QRTZ_TRIGGERS','PREFERRED_NODE') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_TRIGGERS] ADD [PREFERRED_NODE] nvarchar(200) NULL;
END
GO

IF COL_LENGTH('QRTZ_TRIGGERS','PREFERRED_NODE_AUTO') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_TRIGGERS] ADD [PREFERRED_NODE_AUTO] bit NOT NULL DEFAULT 0;
END
GO

-- PostgreSQL (check existence before adding)
-- DO $$
-- BEGIN
--   IF NOT EXISTS (SELECT 1 FROM information_schema.columns
--                  WHERE table_name = 'qrtz_triggers' AND column_name = 'preferred_node') THEN
--     ALTER TABLE qrtz_triggers ADD COLUMN preferred_node VARCHAR(200);
--   END IF;
--   IF NOT EXISTS (SELECT 1 FROM information_schema.columns
--                  WHERE table_name = 'qrtz_triggers' AND column_name = 'preferred_node_auto') THEN
--     ALTER TABLE qrtz_triggers ADD COLUMN preferred_node_auto BOOL NOT NULL DEFAULT FALSE;
--   END IF;
-- END $$;

-- MySQL (check existence before adding)
-- SET @dbname = DATABASE();
-- SET @preparedStatement = (SELECT IF(
--   (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
--    WHERE TABLE_SCHEMA = @dbname AND TABLE_NAME = 'QRTZ_TRIGGERS' AND COLUMN_NAME = 'PREFERRED_NODE') > 0,
--   'SELECT 1',
--   'ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PREFERRED_NODE VARCHAR(200) NULL'
-- ));
-- PREPARE alterIfNotExists FROM @preparedStatement;
-- EXECUTE alterIfNotExists;
-- DEALLOCATE PREPARE alterIfNotExists;
--
-- SET @preparedStatement = (SELECT IF(
--   (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
--    WHERE TABLE_SCHEMA = @dbname AND TABLE_NAME = 'QRTZ_TRIGGERS' AND COLUMN_NAME = 'PREFERRED_NODE_AUTO') > 0,
--   'SELECT 1',
--   'ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PREFERRED_NODE_AUTO BOOLEAN NOT NULL DEFAULT FALSE'
-- ));
-- PREPARE alterIfNotExists FROM @preparedStatement;
-- EXECUTE alterIfNotExists;
-- DEALLOCATE PREPARE alterIfNotExists;

-- SQLite
-- ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PREFERRED_NODE NVARCHAR(200);
-- ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PREFERRED_NODE_AUTO BIT NOT NULL DEFAULT 0;

-- Oracle (check existence before adding)
-- DECLARE
--   column_exists NUMBER;
-- BEGIN
--   SELECT COUNT(*) INTO column_exists FROM user_tab_columns
--   WHERE table_name = 'QRTZ_TRIGGERS' AND column_name = 'PREFERRED_NODE';
--   IF column_exists = 0 THEN
--     EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_TRIGGERS ADD (PREFERRED_NODE VARCHAR2(200))';
--   END IF;
--   SELECT COUNT(*) INTO column_exists FROM user_tab_columns
--   WHERE table_name = 'QRTZ_TRIGGERS' AND column_name = 'PREFERRED_NODE_AUTO';
--   IF column_exists = 0 THEN
--     EXECUTE IMMEDIATE 'ALTER TABLE QRTZ_TRIGGERS ADD (PREFERRED_NODE_AUTO VARCHAR2(1) DEFAULT ''0'' NOT NULL)';
--   END IF;
-- END;
-- /

-- Firebird
-- ALTER TABLE QRTZ_TRIGGERS ADD PREFERRED_NODE VARCHAR(200);
-- ALTER TABLE QRTZ_TRIGGERS ADD PREFERRED_NODE_AUTO SMALLINT DEFAULT 0 NOT NULL;

--
-- Adds the IDX_QRTZ_J_G_N and IDX_QRTZ_T_G_N indexes to the QRTZ_JOB_DETAILS and
-- QRTZ_TRIGGERS tables. The 4.x job and trigger listing queries page with
-- ORDER BY JOB_GROUP, JOB_NAME and ORDER BY TRIGGER_GROUP, TRIGGER_NAME; the primary
-- keys are name-before-group, so no existing index serves those ordered scans.
--
-- These indexes are OPTIONAL: the queries work without them, but each page becomes a
-- scan plus a sort. Add them if you list jobs or triggers from a large schema.
--

-- SQL Server
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_J_G_N' AND object_id = OBJECT_ID('dbo.QRTZ_JOB_DETAILS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_J_G_N] ON [dbo].[QRTZ_JOB_DETAILS](SCHED_NAME, JOB_GROUP, JOB_NAME);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_G_N' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  CREATE INDEX [IDX_QRTZ_T_G_N] ON [dbo].[QRTZ_TRIGGERS](SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME);
END
GO

-- PostgreSQL
-- CREATE INDEX IF NOT EXISTS idx_qrtz_j_g_n ON qrtz_job_details (sched_name, job_group, job_name);
-- CREATE INDEX IF NOT EXISTS idx_qrtz_t_g_n ON qrtz_triggers (sched_name, trigger_group, trigger_name);

-- MySQL (check existence before adding)
-- SET @dbname = DATABASE();
-- SET @preparedStatement = (SELECT IF(
--   (SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
--    WHERE TABLE_SCHEMA = @dbname AND TABLE_NAME = 'QRTZ_JOB_DETAILS' AND INDEX_NAME = 'IDX_QRTZ_J_G_N') > 0,
--   'SELECT 1',
--   'CREATE INDEX IDX_QRTZ_J_G_N ON QRTZ_JOB_DETAILS(SCHED_NAME,JOB_GROUP,JOB_NAME)'
-- ));
-- PREPARE createIfNotExists FROM @preparedStatement;
-- EXECUTE createIfNotExists;
-- DEALLOCATE PREPARE createIfNotExists;
--
-- SET @preparedStatement = (SELECT IF(
--   (SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
--    WHERE TABLE_SCHEMA = @dbname AND TABLE_NAME = 'QRTZ_TRIGGERS' AND INDEX_NAME = 'IDX_QRTZ_T_G_N') > 0,
--   'SELECT 1',
--   'CREATE INDEX IDX_QRTZ_T_G_N ON QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_GROUP,TRIGGER_NAME)'
-- ));
-- PREPARE createIfNotExists FROM @preparedStatement;
-- EXECUTE createIfNotExists;
-- DEALLOCATE PREPARE createIfNotExists;

-- SQLite
-- CREATE INDEX IF NOT EXISTS IDX_QRTZ_J_G_N ON QRTZ_JOB_DETAILS(SCHED_NAME,JOB_GROUP,JOB_NAME);
-- CREATE INDEX IF NOT EXISTS IDX_QRTZ_T_G_N ON QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_GROUP,TRIGGER_NAME);

-- Oracle (check existence before adding)
-- DECLARE
--   index_exists NUMBER;
-- BEGIN
--   SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_J_G_N';
--   IF index_exists = 0 THEN
--     EXECUTE IMMEDIATE 'CREATE INDEX IDX_QRTZ_J_G_N ON QRTZ_JOB_DETAILS(SCHED_NAME,JOB_GROUP,JOB_NAME)';
--   END IF;
--   SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_T_G_N';
--   IF index_exists = 0 THEN
--     EXECUTE IMMEDIATE 'CREATE INDEX IDX_QRTZ_T_G_N ON QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_GROUP,TRIGGER_NAME)';
--   END IF;
-- END;
-- /

-- Firebird
-- CREATE INDEX IDX_QRTZ_J_G_N ON QRTZ_JOB_DETAILS(SCHED_NAME,JOB_GROUP,JOB_NAME);
-- CREATE INDEX IDX_QRTZ_T_G_N ON QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_GROUP,TRIGGER_NAME);

--
-- Drops indexes that are redundant: every column list below is the leading prefix, in the
-- same order, of another index on the same table, so every lookup, range scan and ordered
-- scan that used the narrow index is served by the wider one. They only cost writes and
-- storage. Each one and the index that covers it:
--
--   IDX_QRTZ_J_GRP             (SCHED_NAME, JOB_GROUP)
--     covered by IDX_QRTZ_J_G_N              (SCHED_NAME, JOB_GROUP, JOB_NAME)
--   IDX_QRTZ_T_G               (SCHED_NAME, TRIGGER_GROUP)
--     covered by IDX_QRTZ_T_N_G_STATE        (SCHED_NAME, TRIGGER_GROUP, TRIGGER_STATE)
--     and by     IDX_QRTZ_T_G_N              (SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME)
--   IDX_QRTZ_T_STATE           (SCHED_NAME, TRIGGER_STATE)
--     covered by IDX_QRTZ_T_NFT_ST           (SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME)
--   IDX_QRTZ_T_NFT_MISFIRE     (SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME)
--     covered by IDX_QRTZ_T_NFT_ST_MISFIRE   (SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_STATE)
--   IDX_QRTZ_FT_TRIG_INST_NAME (SCHED_NAME, INSTANCE_NAME)
--     covered by IDX_QRTZ_FT_INST_JOB_REQ_RCVRY (SCHED_NAME, INSTANCE_NAME, REQUESTS_RECOVERY)
--
-- These drops are OPTIONAL: 4.x runs unchanged with the extra indexes, they are just dead
-- weight. Which of them a schema has varies by database and by how old the schema is, so
-- every statement checks first. IDX_QRTZ_J_GRP is dropped only once IDX_QRTZ_J_G_N exists,
-- because that index is what replaces it -- run the section above first.
--

-- SQL Server
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_J_GRP' AND object_id = OBJECT_ID('dbo.QRTZ_JOB_DETAILS'))
   AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_J_G_N' AND object_id = OBJECT_ID('dbo.QRTZ_JOB_DETAILS'))
BEGIN
  DROP INDEX [IDX_QRTZ_J_GRP] ON [dbo].[QRTZ_JOB_DETAILS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_G' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
   AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_N_G_STATE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_G] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_STATE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
   AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_ST' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_STATE] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_MISFIRE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
   AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_ST_MISFIRE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_T_NFT_MISFIRE] ON [dbo].[QRTZ_TRIGGERS];
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_TRIG_INST_NAME' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
   AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_INST_JOB_REQ_RCVRY' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
  DROP INDEX [IDX_QRTZ_FT_TRIG_INST_NAME] ON [dbo].[QRTZ_FIRED_TRIGGERS];
END
GO

-- PostgreSQL
-- Only idx_qrtz_t_state applies here; the 3.x PostgreSQL schema had no equivalent of the rest,
-- except idx_qrtz_ft_trig_inst_name -- that one is dropped by the PostgreSQL index realignment
-- section at the end of this file, which first creates the index that covers it.
-- CAUTION: it is covered only by the 4.x idx_qrtz_t_nft_st. The 3.x index of that name was
-- (next_fire_time, trigger_state), which does not lead with trigger_state and so does not
-- cover it. Check yours with \d qrtz_triggers, and if it is still the 3.x shape, rebuild it
-- before dropping idx_qrtz_t_state:
-- DROP INDEX IF EXISTS idx_qrtz_t_nft_st;
-- CREATE INDEX idx_qrtz_t_nft_st ON qrtz_triggers (sched_name, trigger_state, next_fire_time);
--
-- DROP INDEX IF EXISTS idx_qrtz_t_state;

-- MySQL
-- DROP INDEX has no IF EXISTS clause, so skip any line whose index your schema lacks --
-- SHOW INDEX FROM QRTZ_TRIGGERS and SHOW INDEX FROM QRTZ_JOB_DETAILS list them.
-- DROP INDEX IDX_QRTZ_J_GRP ON QRTZ_JOB_DETAILS;
-- DROP INDEX IDX_QRTZ_T_G ON QRTZ_TRIGGERS;
-- DROP INDEX IDX_QRTZ_T_STATE ON QRTZ_TRIGGERS;
-- DROP INDEX IDX_QRTZ_T_NFT_MISFIRE ON QRTZ_TRIGGERS;
-- DROP INDEX IDX_QRTZ_FT_TRIG_INST_NAME ON QRTZ_FIRED_TRIGGERS;
--
-- The 3.x QRTZ_BLOB_TRIGGERS table also declared an inline INDEX on the primary key's own
-- columns (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP), which InnoDB stores as a second copy of
-- the primary key. MySQL auto-named it, usually SCHED_NAME or SCHED_NAME_2; find the name
-- with SHOW INDEX FROM QRTZ_BLOB_TRIGGERS and drop it:
-- DROP INDEX SCHED_NAME ON QRTZ_BLOB_TRIGGERS;

-- SQLite
-- Nothing to drop: the 3.x SQLite schema created no secondary indexes.

-- Oracle (check existence before dropping)
-- DECLARE
--   index_exists NUMBER;
-- BEGIN
--   SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = 'IDX_QRTZ_J_G_N';
--   IF index_exists > 0 THEN
--     FOR i IN (SELECT index_name FROM user_indexes WHERE index_name = 'IDX_QRTZ_J_GRP') LOOP
--       EXECUTE IMMEDIATE 'DROP INDEX ' || i.index_name;
--     END LOOP;
--   END IF;
--   FOR i IN (SELECT index_name FROM user_indexes
--             WHERE index_name IN ('IDX_QRTZ_T_G', 'IDX_QRTZ_T_STATE', 'IDX_QRTZ_T_NFT_MISFIRE',
--                                  'IDX_QRTZ_FT_TRIG_INST_NAME')) LOOP
--     EXECUTE IMMEDIATE 'DROP INDEX ' || i.index_name;
--   END LOOP;
-- END;
-- /

-- Firebird
-- DROP INDEX errors if the index is absent, so skip any line whose index your schema lacks.
-- DROP INDEX IDX_QRTZ_J_GRP;
-- DROP INDEX IDX_QRTZ_T_G;
-- DROP INDEX IDX_QRTZ_T_STATE;
-- DROP INDEX IDX_QRTZ_T_NFT_MISFIRE;
-- DROP INDEX IDX_QRTZ_FT_TRIG_INST_NAME;

--
-- Realigns the PostgreSQL index set with the statements 4.x actually issues. This section is
-- PostgreSQL only -- the other dialect scripts already carry these indexes.
--
-- The PostgreSQL script has long carried five fired-trigger indexes on a single column that is
-- not sched_name. No Quartz statement can drive a scan from one: every query against
-- QRTZ_FIRED_TRIGGERS filters SCHED_NAME first and then a whole key, so the planner's best case
-- is bitmap-ANDing such an index with another. They are replaced below by the composite forms
-- the other dialects carry, together with the two QRTZ_TRIGGERS indexes PostgreSQL never had.
--
-- These changes are OPTIONAL: 4.x runs unchanged without them. The creates matter once a schema
-- holds a non-trivial number of triggers; the drops only reclaim write cost and storage.
--
-- Run the creates before the drops. Replace 'qrtz_' with your configured table prefix if
-- different. CREATE INDEX blocks writes to the table while it builds, so use
-- CREATE INDEX CONCURRENTLY (outside a transaction block) against a live scheduler.
--

-- PostgreSQL
-- Serves SelectTriggersForJob, SelectNumTriggersForJob, both UpdateJobTriggerStates statements and the trigger listing's job filter.
-- CREATE INDEX IF NOT EXISTS idx_qrtz_t_j ON qrtz_triggers (sched_name, job_name, job_group);
--
-- Serves SelectTriggersForCalendar and SelectReferencedCalendar, which otherwise scan every trigger on each calendar store and remove.
-- CREATE INDEX IF NOT EXISTS idx_qrtz_t_c ON qrtz_triggers (sched_name, calendar_name);
--
-- Serves SelectInstancesRecoverableFiredTriggers, the instance-name filter of the fired-trigger select and delete, and SelectFiredTriggerInstanceNames.
-- CREATE INDEX IF NOT EXISTS idx_qrtz_ft_inst_job_req_rcvry ON qrtz_fired_triggers (sched_name, instance_name, requests_recovery);
--
-- Serves the job filter of the fired-trigger select and delete, and IsJobCurrentlyExecuting, which runs on every fire of a non-concurrent job.
-- CREATE INDEX IF NOT EXISTS idx_qrtz_ft_j_g ON qrtz_fired_triggers (sched_name, job_name, job_group);
--
-- The existing idx_qrtz_ft_trig_nm_gp is the 4.x idx_qrtz_ft_t_g under PostgreSQL's own older name; renaming is metadata only, and optional -- nothing in Quartz names an index.
-- ALTER INDEX IF EXISTS idx_qrtz_ft_trig_nm_gp RENAME TO idx_qrtz_ft_t_g;
--
-- The five that lead with something other than sched_name, and so serve no Quartz query at all; the composite indexes above replace them.
-- DROP INDEX IF EXISTS idx_qrtz_ft_trig_name;
-- DROP INDEX IF EXISTS idx_qrtz_ft_trig_group;
-- DROP INDEX IF EXISTS idx_qrtz_ft_job_name;
-- DROP INDEX IF EXISTS idx_qrtz_ft_job_group;
-- DROP INDEX IF EXISTS idx_qrtz_ft_job_req_recovery;
--
-- CAUTION: (sched_name, instance_name) is a leading prefix of idx_qrtz_ft_inst_job_req_rcvry and
-- of nothing else, so drop it only once that CREATE INDEX above has succeeded.
-- DROP INDEX IF EXISTS idx_qrtz_ft_trig_inst_name;
