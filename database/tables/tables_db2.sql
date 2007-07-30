# These are taken from Quartz (Java) version 1.6.0

#
# Thanks to Horia Muntean for submitting this....
#
# .. known to work with DB2 7.1 and the JDBC driver "COM.ibm.db2.jdbc.net.DB2Driver"
# .. likely to work with others...
#
# In your Quartz properties file, you'll need to set 
# org.quartz.jobStore.driverDelegateClass = org.quartz.impl.jdbcjobstore.StdJDBCDelegate
#
# If you're using DB2 6.x you'll want to set this property to
# org.quartz.jobStore.driverDelegateClass = org.quartz.impl.jdbcjobstore.DB2v6Delegate
#
# Note that the blob column size (e.g. blob(2000)) dictates the amount of data that can be stored in 
# that blob - i.e. limits the amount of data you can put into your JobDataMap 
#


create table qrtz_job_details (
  job_name varchar(80) not null,
  job_group varchar(80) not null,
  description varchar(120) null,
  job_class_name varchar(128) not null,
  is_durable varchar(1) not null,
  is_volatile varchar(1) not null,
  is_stateful varchar(1) not null,
  requests_recovery varchar(1) not null,
  job_data blob(2000),
    primary key (job_name,job_group)
)

create table qrtz_job_listeners(
  job_name varchar(80) not null,
  job_group varchar(80) not null,
  job_listener varchar(80) not null,
    primary key (job_name,job_group,job_listener),
    foreign key (job_name,job_group) references qrtz_job_details(job_name,job_group)
)

create table qrtz_triggers(
  trigger_name varchar(80) not null,
  trigger_group varchar(80) not null,
  job_name varchar(80) not null,
  job_group varchar(80) not null,
  is_volatile varchar(1) not null,
  description varchar(120) null,
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
)

create table qrtz_simple_triggers(
  trigger_name varchar(80) not null,
  trigger_group varchar(80) not null,
  repeat_count bigint not null,
  repeat_interval bigint not null,
  times_triggered bigint not null,
    primary key (trigger_name,trigger_group),
    foreign key (trigger_name,trigger_group) references qrtz_triggers(trigger_name,trigger_group)
)

create table qrtz_cron_triggers(
  trigger_name varchar(80) not null,
  trigger_group varchar(80) not null,
  cron_expression varchar(80) not null,
  time_zone_id varchar(80),
    primary key (trigger_name,trigger_group),
    foreign key (trigger_name,trigger_group) references qrtz_triggers(trigger_name,trigger_group)
)

create table qrtz_blob_triggers(
  trigger_name varchar(80) not null,
  trigger_group varchar(80) not null,
  blob_data blob(2000) null,
    primary key (trigger_name,trigger_group),
    foreign key (trigger_name,trigger_group) references qrtz_triggers(trigger_name,trigger_group)
)

create table qrtz_trigger_listeners(
  trigger_name varchar(80) not null,
  trigger_group varchar(80) not null,
  trigger_listener varchar(80) not null,
    primary key (trigger_name,trigger_group,trigger_listener),
    foreign key (trigger_name,trigger_group) references qrtz_triggers(trigger_name,trigger_group)
)

create table qrtz_calendars(
  calendar_name varchar(80) not null,
  calendar blob(2000) not null,
    primary key (calendar_name)
)

create table qrtz_fired_triggers(
  entry_id varchar(95) not null,
  trigger_name varchar(80) not null,
  trigger_group varchar(80) not null,
  is_volatile varchar(1) not null,
  instance_name varchar(80) not null,
  fired_time bigint not null,
  priority integer not null,
  state varchar(16) not null,
  job_name varchar(80) null,
  job_group varchar(80) null,
  is_stateful varchar(1) null,
  requests_recovery varchar(1) null,
    primary key (entry_id)
);


create table qrtz_paused_trigger_grps(
  trigger_group  varchar(80) not null, 
    primary key (trigger_group)
);

create table qrtz_scheduler_state (
  instance_name varchar(80) not null,
  last_checkin_time bigint not null,
  checkin_interval bigint not null,
    primary key (instance_name)
);

create table qrtz_locks
  (
    lock_name  varchar(40) not null, 
      primary key (lock_name)
);

insert into qrtz_locks values('TRIGGER_ACCESS');
insert into qrtz_locks values('JOB_ACCESS');
insert into qrtz_locks values('CALENDAR_ACCESS');
insert into qrtz_locks values('STATE_ACCESS');
insert into qrtz_locks values('MISFIRE_ACCESS');