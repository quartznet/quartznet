-- Quartz.NET schema migration: add PREFERRED_NODE column
-- Supports preferred node feature (node affinity / job pinning in clusters)
-- This migration is optional. Without it, preferred node functionality is not available.

-- SQL Server
-- ALTER TABLE QRTZ_TRIGGERS ADD PREFERRED_NODE NVARCHAR(250) NULL;
-- Note: PREFERRED_NODE is wider than INSTANCE_NAME (200) to accommodate the internal "auto:" prefix (5 chars).

-- PostgreSQL
-- ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PREFERRED_NODE VARCHAR(250) NULL;

-- MySQL
-- ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PREFERRED_NODE VARCHAR(250) NULL;

-- Oracle
-- ALTER TABLE QRTZ_TRIGGERS ADD (PREFERRED_NODE VARCHAR2(250) NULL);

-- SQLite
-- ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PREFERRED_NODE VARCHAR(250) NULL;

-- Firebird
-- ALTER TABLE QRTZ_TRIGGERS ADD PREFERRED_NODE VARCHAR(250);
