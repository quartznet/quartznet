--USE [database_name];
--GO

ALTER TABLE qrtz_triggers
ADD initial_next_fire_time bigint NULL;
