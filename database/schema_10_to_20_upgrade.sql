--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST COPY OF YOU PRODUCTION !!
--
-- Migration script sample for MS SQL Server
-- You need to modify script for other databases
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST COPY OF YOU PRODUCTION !!
--

--- 
--- !!! The default value of sched_name column is TestScheduler !!!
--- If you have existing data the scheduler name should correspond to your existing scheduler name
--- in Quartz configuration (quartz.scheduler.instanceName)
--- 

-- 
-- drop tables that are no longer used
--
drop table qrtz_job_listeners;
drop table qrtz_trigger_listeners;
GO

--
-- change some varchar(1)s to true bit/booleans
--
alter table QRTZ_FIRED_TRIGGERS alter column REQUESTS_RECOVERY bit
alter table QRTZ_FIRED_TRIGGERS alter column IS_VOLATILE bit
alter table QRTZ_FIRED_TRIGGERS alter column IS_STATEFUL bit
alter table QRTZ_JOB_DETAILS alter column REQUESTS_RECOVERY bit
alter table QRTZ_JOB_DETAILS alter column IS_DURABLE bit


--
-- drop columns that are no longer used
--
alter table qrtz_job_details drop column is_volatile;
alter table qrtz_triggers drop column is_volatile;
alter table qrtz_fired_triggers drop column is_volatile;
--
-- add new columns that replace the 'is_stateful' column
--
alter table qrtz_job_details add IS_NONCONCURRENT bit;
alter table qrtz_job_details add IS_UPDATE_DATA bit;
GO
update qrtz_job_details set IS_NONCONCURRENT = is_stateful;
update qrtz_job_details set IS_UPDATE_DATA = is_stateful;
GO
alter table qrtz_job_details drop column is_stateful;
alter table qrtz_fired_triggers add IS_NONCONCURRENT bit;
GO
update qrtz_fired_triggers set IS_NONCONCURRENT = is_stateful;
GO
alter table qrtz_fired_triggers drop column is_stateful;
GO

--
-- add new 'sched_name' column to all tables
--
alter table qrtz_blob_triggers add SCHED_NAME varchar(120) not null DEFAULT 'TestScheduler';
alter table qrtz_calendars add SCHED_NAME varchar(120) not null DEFAULT 'TestScheduler';
alter table qrtz_cron_triggers add SCHED_NAME varchar(120) not null DEFAULT 'TestScheduler';
alter table qrtz_fired_triggers add SCHED_NAME varchar(120) not null DEFAULT 'TestScheduler';
alter table qrtz_job_details add SCHED_NAME varchar(120) not null DEFAULT 'TestScheduler';
alter table qrtz_locks add SCHED_NAME varchar(120) not null DEFAULT 'TestScheduler';
alter table qrtz_paused_trigger_grps add SCHED_NAME varchar(120) not null DEFAULT 'TestScheduler';
alter table qrtz_scheduler_state add SCHED_NAME varchar(120) not null DEFAULT 'TestScheduler';
alter table qrtz_simple_triggers add SCHED_NAME varchar(120) not null DEFAULT 'TestScheduler';
alter table qrtz_triggers add SCHED_NAME varchar(120) not null DEFAULT 'TestScheduler';
GO

--
-- drop all primary and foreign key constraints, so that we can define new ones
--
--alter table qrtz_blob_triggers drop constraint qrtz_blob_triggers_pkey;
--alter table qrtz_blob_triggers drop constraint qrtz_blob_triggers_trigger_name_fkey;
alter table qrtz_simple_triggers drop constraint PK_QRTZ_SIMPLE_TRIGGERS;
alter table qrtz_simple_triggers drop constraint FK_QRTZ_SIMPLE_TRIGGERS_QRTZ_TRIGGERS;
alter table qrtz_cron_triggers drop constraint PK_QRTZ_CRON_TRIGGERS;
alter table qrtz_cron_triggers drop constraint FK_QRTZ_CRON_TRIGGERS_QRTZ_TRIGGERS;
alter table qrtz_triggers drop constraint PK_QRTZ_TRIGGERS;
alter table qrtz_triggers drop constraint FK_QRTZ_TRIGGERS_QRTZ_JOB_DETAILS;
alter table qrtz_job_details drop constraint PK_QRTZ_JOB_DETAILS;
GO

--
-- add all primary and foreign key constraints, based on new columns
--
alter table qrtz_job_details add primary key (sched_name, job_name, job_group);
alter table qrtz_triggers add primary key (sched_name, trigger_name, trigger_group);
alter table qrtz_triggers add foreign key (sched_name, job_name, job_group) references qrtz_job_details(sched_name, job_name, job_group);
alter table qrtz_blob_triggers add primary key (sched_name, trigger_name, trigger_group);
alter table qrtz_blob_triggers add foreign key (sched_name, trigger_name, trigger_group) references qrtz_triggers(sched_name, trigger_name, trigger_group);
alter table qrtz_cron_triggers add primary key (sched_name, trigger_name, trigger_group);
alter table qrtz_cron_triggers add foreign key (sched_name, trigger_name, trigger_group) references qrtz_triggers(sched_name, trigger_name, trigger_group);
alter table qrtz_simple_triggers add primary key (sched_name, trigger_name, trigger_group);
alter table qrtz_simple_triggers add foreign key (sched_name, trigger_name, trigger_group) references qrtz_triggers(sched_name, trigger_name, trigger_group);
alter table qrtz_fired_triggers drop constraint PK_QRTZ_FIRED_TRIGGERS;
alter table qrtz_fired_triggers add primary key (sched_name, entry_id);
alter table qrtz_calendars drop constraint PK_QRTZ_CALENDARS;
alter table qrtz_calendars add primary key (sched_name, calendar_name);
alter table qrtz_locks drop constraint PK_QRTZ_LOCKS;
alter table qrtz_locks add primary key (sched_name, lock_name);
alter table qrtz_paused_trigger_grps drop constraint PK_QRTZ_PAUSED_TRIGGER_GRPS;
alter table qrtz_paused_trigger_grps add primary key (sched_name, trigger_group);
alter table qrtz_scheduler_state drop constraint PK_QRTZ_SCHEDULER_STATE;
alter table qrtz_scheduler_state add primary key (sched_name, instance_name);
GO

--
-- add new simprop_triggers table
--
CREATE TABLE QRTZ_SIMPROP_TRIGGERS
 (          
    SCHED_NAME VARCHAR(120) NOT NULL,
    TRIGGER_NAME VARCHAR(200) NOT NULL,
    TRIGGER_GROUP VARCHAR(200) NOT NULL,
    STR_PROP_1 VARCHAR(512) NULL,
    STR_PROP_2 VARCHAR(512) NULL,
    STR_PROP_3 VARCHAR(512) NULL,
    INT_PROP_1 INT NULL,
    INT_PROP_2 INT NULL,
    LONG_PROP_1 BIGINT NULL,
    LONG_PROP_2 BIGINT NULL,
    DEC_PROP_1 NUMERIC(13,4) NULL,
    DEC_PROP_2 NUMERIC(13,4) NULL,
    BOOL_PROP_1 BIT NULL,
    BOOL_PROP_2 BIT NULL,
    PRIMARY KEY (SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP),
    FOREIGN KEY (SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP)
    REFERENCES QRTZ_TRIGGERS(SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP)
);
GO

--
-- create indexes for faster queries
--
create index idx_qrtz_j_req_recovery on qrtz_job_details(SCHED_NAME,REQUESTS_RECOVERY);
create index idx_qrtz_j_grp on qrtz_job_details(SCHED_NAME,JOB_GROUP);
create index idx_qrtz_t_j on qrtz_triggers(SCHED_NAME,JOB_NAME,JOB_GROUP);
create index idx_qrtz_t_jg on qrtz_triggers(SCHED_NAME,JOB_GROUP);
create index idx_qrtz_t_c on qrtz_triggers(SCHED_NAME,CALENDAR_NAME);
create index idx_qrtz_t_g on qrtz_triggers(SCHED_NAME,TRIGGER_GROUP);
create index idx_qrtz_t_state on qrtz_triggers(SCHED_NAME,TRIGGER_STATE);
create index idx_qrtz_t_n_state on qrtz_triggers(SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP,TRIGGER_STATE);
create index idx_qrtz_t_n_g_state on qrtz_triggers(SCHED_NAME,TRIGGER_GROUP,TRIGGER_STATE);
create index idx_qrtz_t_next_fire_time on qrtz_triggers(SCHED_NAME,NEXT_FIRE_TIME);
create index idx_qrtz_t_nft_st on qrtz_triggers(SCHED_NAME,TRIGGER_STATE,NEXT_FIRE_TIME);
create index idx_qrtz_t_nft_misfire on qrtz_triggers(SCHED_NAME,MISFIRE_INSTR,NEXT_FIRE_TIME);
create index idx_qrtz_t_nft_st_misfire on qrtz_triggers(SCHED_NAME,MISFIRE_INSTR,NEXT_FIRE_TIME,TRIGGER_STATE);
create index idx_qrtz_t_nft_st_misfire_grp on qrtz_triggers(SCHED_NAME,MISFIRE_INSTR,NEXT_FIRE_TIME,TRIGGER_GROUP,TRIGGER_STATE);
create index idx_qrtz_ft_trig_inst_name on qrtz_fired_triggers(SCHED_NAME,INSTANCE_NAME);
create index idx_qrtz_ft_inst_job_req_rcvry on qrtz_fired_triggers(SCHED_NAME,INSTANCE_NAME,REQUESTS_RECOVERY);
create index idx_qrtz_ft_j_g on qrtz_fired_triggers(SCHED_NAME,JOB_NAME,JOB_GROUP);
create index idx_qrtz_ft_jg on qrtz_fired_triggers(SCHED_NAME,JOB_GROUP);
create index idx_qrtz_ft_t_g on qrtz_fired_triggers(SCHED_NAME,TRIGGER_NAME,TRIGGER_GROUP);
create index idx_qrtz_ft_tg on qrtz_fired_triggers(SCHED_NAME,TRIGGER_GROUP);
GO
--
-- All done
--
