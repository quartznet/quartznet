# These are taken from Quartz (Java) version 1.6.0

#
# Updated by Claudiu Crisan (claudiu.crisan@schartner.net)
# SQL scripts for DB2 ver 8.1
#
# Changes:
# - "varchar(1)" replaced with "integer"
# - "field_name varchar(xxx) not null" replaced with "field_name varchar(xxx)"
#


DROP TABLE QRTZ_JOB_LISTENERS;
DROP TABLE QRTZ_TRIGGER_LISTENERS;
DROP TABLE QRTZ_FIRED_TRIGGERS;
DROP TABLE QRTZ_PAUSED_TRIGGER_GRPS;
DROP TABLE QRTZ_SCHEDULER_STATE;
DROP TABLE QRTZ_LOCKS;
DROP TABLE QRTZ_SIMPLE_TRIGGERS;
DROP TABLE QRTZ_CRON_TRIGGERS;
DROP TABLE QRTZ_TRIGGERS;
DROP TABLE QRTZ_JOB_DETAILS;
DROP TABLE QRTZ_CALENDARS;
DROP TABLE QRTZ_BLOB_TRIGGERS;

create table qrtz_job_details(
job_name varchar(80) not null,
job_group varchar(80) not null,
description varchar(120),
job_class_name varchar(128) not null,
is_durable integer not null,
is_volatile integer not null,
is_stateful integer not null,
requests_recovery integer not null,
job_data blob(2000),
primary key (job_name,job_group)
);

create table qrtz_job_listeners(
job_name varchar(80) not null,
job_group varchar(80) not null,
job_listener varchar(80) not null,
primary key (job_name,job_group,job_listener),
foreign key (job_name,job_group) references qrtz_job_details(job_name,job_group)
);

create table qrtz_triggers(
trigger_name varchar(80) not null,
trigger_group varchar(80) not null,
job_name varchar(80) not null,
job_group varchar(80) not null,
is_volatile integer not null,
description varchar(120),
next_fire_time bigint,
prev_fire_time bigint,
priority integer,
trigger_state varchar(16) not null,
trigger_type varchar(8) not null,
start_time bigint not null,
end_time bigint,
calendar_name varchar(80),
misfire_instr smallint,
job_data blob(2000),
primary key (trigger_name,trigger_group),
foreign key (job_name,job_group) references qrtz_job_details(job_name,job_group)
);

create table qrtz_simple_triggers(
trigger_name varchar(80) not null,
trigger_group varchar(80) not null,
repeat_count bigint not null,
repeat_interval bigint not null,
times_triggered bigint not null,
primary key (trigger_name,trigger_group),
foreign key (trigger_name,trigger_group) references qrtz_triggers(trigger_name,trigger_group)
);

create table qrtz_cron_triggers(
trigger_name varchar(80) not null,
trigger_group varchar(80) not null,
cron_expression varchar(120) not null,
time_zone_id varchar(80),
primary key (trigger_name,trigger_group),
foreign key (trigger_name,trigger_group) references qrtz_triggers(trigger_name,trigger_group)
);

create table qrtz_blob_triggers(
trigger_name varchar(80) not null,
trigger_group varchar(80) not null,
blob_data blob(2000),
primary key (trigger_name,trigger_group),
foreign key (trigger_name,trigger_group) references qrtz_triggers(trigger_name,trigger_group)
);

create table qrtz_trigger_listeners(
trigger_name varchar(80) not null,
trigger_group varchar(80) not null,
trigger_listener varchar(80) not null,
primary key (trigger_name,trigger_group,trigger_listener),
foreign key (trigger_name,trigger_group) references qrtz_triggers(trigger_name,trigger_group)
);

create table qrtz_calendars(
calendar_name varchar(80) not null,
calendar blob(2000) not null,
primary key (calendar_name)
);

create table qrtz_fired_triggers(
entry_id varchar(95) not null,
trigger_name varchar(80) not null,
trigger_group varchar(80) not null,
is_volatile integer not null,
instance_name varchar(80) not null,
fired_time bigint not null,
priority integer not null,
state varchar(16) not null,
job_name varchar(80),
job_group varchar(80),
is_stateful integer,
requests_recovery integer,
primary key (entry_id)
);

create table qrtz_paused_trigger_grps(
trigger_group varchar(80) not null,
primary key (trigger_group)
);

create table qrtz_scheduler_state(
instance_name varchar(80) not null,
last_checkin_time bigint not null,
checkin_interval bigint not null,
primary key (instance_name)
);

create table qrtz_locks(
lock_name varchar(40) not null,
primary key (lock_name)
);

insert into qrtz_locks values('TRIGGER_ACCESS');
insert into qrtz_locks values('JOB_ACCESS');
insert into qrtz_locks values('CALENDAR_ACCESS');
insert into qrtz_locks values('STATE_ACCESS');
insert into qrtz_locks values('MISFIRE_ACCESS'); 
