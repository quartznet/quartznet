--
-- Quartz.NET schema -- PostgreSQL
--
-- GENERATED FILE. Describe the schema in build/Build.DatabaseSchema.cs and run
-- 'dotnet fallout GenerateSchema'; edits made here are overwritten.
--
-- This is what AdoJobStore runs for itself when SchemaProvisioning.CreateIfMissing is
-- configured. It is not the script to run by hand -- use
-- database/tables/tables_postgres.sql for that, which is written for a person with a
-- database client and drops an existing schema before it recreates one.
--
-- Every statement creates only what is missing, and nothing here ever drops anything.
-- So it is safe to run against a schema that already exists, and safe to run twice.
--
-- '{0}' is the configured table prefix and '{1}' is the same prefix with any schema
-- qualifier removed, for the identifiers that cannot carry one -- index, constraint and
-- catalog-lookup names. They are substituted at runtime, so a schema provisioned under a
-- prefix of its own collides with nothing.
--
-- Statements are separated by a line reading exactly '--;;'. The job store splits on
-- it and sends each piece to the provider as one command, which is why no dialect's batch
-- separator appears: no GO, no lone '/', no SET TERM.
--
--;;
-- {0}JOB_DETAILS
CREATE TABLE IF NOT EXISTS {0}job_details (
  sched_name text not null,
  job_name text not null,
  job_group text not null,
  description text null,
  job_class_name text not null,
  is_durable bool not null,
  is_nonconcurrent bool not null,
  is_update_data bool not null,
  requests_recovery bool not null,
  job_data bytea null,
  primary key (sched_name,job_name,job_group)
);
--;;
-- {0}TRIGGERS
CREATE TABLE IF NOT EXISTS {0}triggers (
  sched_name text not null,
  trigger_name text not null,
  trigger_group text not null,
  job_name text not null,
  job_group text not null,
  description text null,
  next_fire_time bigint null,
  prev_fire_time bigint null,
  priority integer null,
  trigger_state text not null,
  trigger_type text not null,
  start_time bigint not null,
  end_time bigint null,
  calendar_name text null,
  misfire_instr smallint null,
  misfire_orig_fire_time bigint null,
  execution_group varchar(200) null,
  preferred_node varchar(200) null,
  preferred_node_auto bool not null default false,
  retry_policy varchar(250) null,
  retry_attempt integer null,
  job_data bytea null,
  primary key (sched_name,trigger_name,trigger_group),
  foreign key (sched_name,job_name,job_group) references {0}job_details (sched_name,job_name,job_group)
);
--;;
-- {0}SIMPLE_TRIGGERS
CREATE TABLE IF NOT EXISTS {0}simple_triggers (
  sched_name text not null,
  trigger_name text not null,
  trigger_group text not null,
  repeat_count bigint not null,
  repeat_interval bigint not null,
  times_triggered bigint not null,
  primary key (sched_name,trigger_name,trigger_group),
  foreign key (sched_name,trigger_name,trigger_group) references {0}triggers (sched_name,trigger_name,trigger_group) on delete cascade
);
--;;
-- {0}CRON_TRIGGERS
CREATE TABLE IF NOT EXISTS {0}cron_triggers (
  sched_name text not null,
  trigger_name text not null,
  trigger_group text not null,
  cron_expression text not null,
  time_zone_id text,
  primary key (sched_name,trigger_name,trigger_group),
  foreign key (sched_name,trigger_name,trigger_group) references {0}triggers (sched_name,trigger_name,trigger_group) on delete cascade
);
--;;
-- {0}SIMPROP_TRIGGERS
CREATE TABLE IF NOT EXISTS {0}simprop_triggers (
  sched_name text not null,
  trigger_name text not null,
  trigger_group text not null,
  str_prop_1 text null,
  str_prop_2 text null,
  str_prop_3 text null,
  int_prop_1 integer null,
  int_prop_2 integer null,
  long_prop_1 bigint null,
  long_prop_2 bigint null,
  dec_prop_1 numeric null,
  dec_prop_2 numeric null,
  bool_prop_1 bool null,
  bool_prop_2 bool null,
  time_zone_id text null,
  primary key (sched_name,trigger_name,trigger_group),
  foreign key (sched_name,trigger_name,trigger_group) references {0}triggers (sched_name,trigger_name,trigger_group) on delete cascade
);
--;;
-- {0}BLOB_TRIGGERS
CREATE TABLE IF NOT EXISTS {0}blob_triggers (
  sched_name text not null,
  trigger_name text not null,
  trigger_group text not null,
  blob_data bytea null,
  primary key (sched_name,trigger_name,trigger_group),
  foreign key (sched_name,trigger_name,trigger_group) references {0}triggers (sched_name,trigger_name,trigger_group) on delete cascade
);
--;;
-- {0}CALENDARS
CREATE TABLE IF NOT EXISTS {0}calendars (
  sched_name text not null,
  calendar_name text not null,
  calendar bytea not null,
  primary key (sched_name,calendar_name)
);
--;;
-- {0}PAUSED_TRIGGER_GRPS
CREATE TABLE IF NOT EXISTS {0}paused_trigger_grps (
  sched_name text not null,
  trigger_group text not null,
  primary key (sched_name,trigger_group)
);
--;;
-- {0}PAUSED_JOB_GRPS
CREATE TABLE IF NOT EXISTS {0}paused_job_grps (
  sched_name text not null,
  job_group text not null,
  primary key (sched_name,job_group)
);
--;;
-- {0}FIRED_TRIGGERS
CREATE TABLE IF NOT EXISTS {0}fired_triggers (
  sched_name text not null,
  entry_id text not null,
  trigger_name text not null,
  trigger_group text not null,
  instance_name text not null,
  fired_time bigint not null,
  sched_time bigint not null,
  priority integer not null,
  state text not null,
  job_name text null,
  job_group text null,
  is_nonconcurrent bool not null,
  requests_recovery bool null,
  execution_group varchar(200) null,
  primary key (sched_name,entry_id)
);
--;;
-- {0}SCHEDULER_STATE
CREATE TABLE IF NOT EXISTS {0}scheduler_state (
  sched_name text not null,
  instance_name text not null,
  last_checkin_time bigint not null,
  checkin_interval bigint not null,
  primary key (sched_name,instance_name)
);
--;;
-- {0}LOCKS
CREATE TABLE IF NOT EXISTS {0}locks (
  sched_name text not null,
  lock_name text not null,
  primary key (sched_name,lock_name)
);
--;;
-- IDX_{1}J_G_N
CREATE INDEX IF NOT EXISTS idx_{1}j_g_n ON {0}job_details (sched_name, job_group, job_name);
--;;
-- IDX_{1}T_J
CREATE INDEX IF NOT EXISTS idx_{1}t_j ON {0}triggers (sched_name, job_name, job_group);
--;;
-- IDX_{1}T_G_N
CREATE INDEX IF NOT EXISTS idx_{1}t_g_n ON {0}triggers (sched_name, trigger_group, trigger_name);
--;;
-- IDX_{1}T_C
CREATE INDEX IF NOT EXISTS idx_{1}t_c ON {0}triggers (sched_name, calendar_name);
--;;
-- IDX_{1}T_NFT_ST
CREATE INDEX IF NOT EXISTS idx_{1}t_nft_st ON {0}triggers (sched_name, trigger_state, next_fire_time asc, priority desc, misfire_instr);
--;;
-- IDX_{1}FT_INST_JOB_REQ_RCVRY
CREATE INDEX IF NOT EXISTS idx_{1}ft_inst_job_req_rcvry ON {0}fired_triggers (sched_name, instance_name, requests_recovery);
--;;
-- IDX_{1}FT_J_G
CREATE INDEX IF NOT EXISTS idx_{1}ft_j_g ON {0}fired_triggers (sched_name, job_name, job_group);
--;;
-- IDX_{1}FT_T_G
CREATE INDEX IF NOT EXISTS idx_{1}ft_t_g ON {0}fired_triggers (sched_name, trigger_name, trigger_group);
