CREATE TABLE qrtz_calendars (
    sched_name text NOT NULL,
    calendar_name text NOT NULL,
    calendar bytea NOT NULL,
    CONSTRAINT "PK_qrtz_calendars" PRIMARY KEY (sched_name, calendar_name)
);


CREATE TABLE qrtz_execution_history (
    sched_name text NOT NULL,
    entry_id text NOT NULL,
    due_time bigint,
    error_message text,
    exception_type text,
    execution_log text,
    fired_time bigint NOT NULL,
    fire_instance_id text,
    instance_name text NOT NULL,
    job_group text NOT NULL,
    job_input text,
    job_input_too_large bool,
    job_name text NOT NULL,
    manual bool,
    metrics text,
    origin integer,
    result integer,
    retry_attempt integer NOT NULL DEFAULT 0,
    retry_scheduled bool NOT NULL DEFAULT FALSE,
    run_time bigint NOT NULL,
    start_delay bigint,
    succeeded bool NOT NULL,
    summary text,
    trigger_group text NOT NULL,
    trigger_name text NOT NULL,
    CONSTRAINT "PK_qrtz_execution_history" PRIMARY KEY (sched_name, entry_id)
);


CREATE TABLE qrtz_fired_triggers (
    sched_name text NOT NULL,
    entry_id text NOT NULL,
    execution_group varchar(200),
    fired_time bigint NOT NULL,
    instance_name text NOT NULL,
    interrupt_requested_time bigint,
    is_nonconcurrent bool NOT NULL,
    job_group text,
    job_name text,
    priority integer NOT NULL,
    progress integer,
    progress_message varchar(250),
    requests_recovery bool,
    sched_time bigint NOT NULL,
    state text NOT NULL,
    trigger_group text NOT NULL,
    trigger_name text NOT NULL,
    CONSTRAINT "PK_qrtz_fired_triggers" PRIMARY KEY (sched_name, entry_id)
);


CREATE TABLE qrtz_job_details (
    sched_name text NOT NULL,
    job_name text NOT NULL,
    job_group text NOT NULL,
    description text,
    is_durable bool NOT NULL,
    is_nonconcurrent bool NOT NULL,
    is_update_data bool NOT NULL,
    job_class_name text NOT NULL,
    job_data bytea,
    requests_recovery bool NOT NULL,
    CONSTRAINT "PK_qrtz_job_details" PRIMARY KEY (sched_name, job_name, job_group)
);


CREATE TABLE qrtz_job_status (
    sched_name text NOT NULL,
    job_group text NOT NULL,
    job_name text NOT NULL,
    consecutive_failures integer NOT NULL DEFAULT 0,
    failure_count bigint NOT NULL DEFAULT 0,
    first_fired_time bigint NOT NULL,
    last_entry_id text,
    last_failure_message text,
    last_failure_time bigint,
    last_fired_time bigint NOT NULL,
    last_instance_name text NOT NULL,
    last_result integer NOT NULL,
    last_run_time bigint NOT NULL,
    last_success_time bigint,
    last_summary text,
    run_count bigint NOT NULL DEFAULT 0,
    CONSTRAINT "PK_qrtz_job_status" PRIMARY KEY (sched_name, job_group, job_name)
);


CREATE TABLE qrtz_locks (
    sched_name text NOT NULL,
    lock_name text NOT NULL,
    CONSTRAINT "PK_qrtz_locks" PRIMARY KEY (sched_name, lock_name)
);


CREATE TABLE qrtz_misfire_history (
    sched_name text NOT NULL,
    entry_id text NOT NULL,
    instance_name text NOT NULL,
    job_group text,
    job_name text,
    misfire_time bigint NOT NULL,
    reason integer,
    sched_time bigint,
    trigger_group text NOT NULL,
    trigger_name text NOT NULL,
    CONSTRAINT "PK_qrtz_misfire_history" PRIMARY KEY (sched_name, entry_id)
);


CREATE TABLE qrtz_paused_job_grps (
    sched_name text NOT NULL,
    job_group text NOT NULL,
    paused_at bigint,
    paused_by varchar(200),
    pause_reason varchar(250),
    CONSTRAINT "PK_qrtz_paused_job_grps" PRIMARY KEY (sched_name, job_group)
);


CREATE TABLE qrtz_paused_trigger_grps (
    sched_name text NOT NULL,
    trigger_group text NOT NULL,
    paused_at bigint,
    paused_by varchar(200),
    pause_reason varchar(250),
    CONSTRAINT "PK_qrtz_paused_trigger_grps" PRIMARY KEY (sched_name, trigger_group)
);


CREATE TABLE qrtz_scheduler_state (
    sched_name text NOT NULL,
    instance_name text NOT NULL,
    checkin_interval bigint NOT NULL,
    last_checkin_time bigint NOT NULL,
    quartz_version varchar(20),
    CONSTRAINT "PK_qrtz_scheduler_state" PRIMARY KEY (sched_name, instance_name)
);


CREATE TABLE qrtz_trigger_status (
    sched_name text NOT NULL,
    trigger_group text NOT NULL,
    trigger_name text NOT NULL,
    consecutive_failures integer NOT NULL DEFAULT 0,
    failure_count bigint NOT NULL DEFAULT 0,
    first_fired_time bigint NOT NULL,
    last_entry_id text,
    last_failure_message text,
    last_failure_time bigint,
    last_fired_time bigint NOT NULL,
    last_instance_name text NOT NULL,
    last_result integer NOT NULL,
    last_run_time bigint NOT NULL,
    last_success_time bigint,
    last_summary text,
    run_count bigint NOT NULL DEFAULT 0,
    CONSTRAINT "PK_qrtz_trigger_status" PRIMARY KEY (sched_name, trigger_group, trigger_name)
);


CREATE TABLE qrtz_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    calendar_name text,
    continuation_condition integer,
    continues_trigger_group text,
    continues_trigger_name text,
    description text,
    end_time bigint,
    execution_group varchar(200),
    fixed_delay bool,
    job_data bytea,
    job_group text NOT NULL,
    job_name text NOT NULL,
    misfire_instr smallint,
    misfire_orig_fire_time bigint,
    misfire_threshold bigint,
    next_fire_time bigint,
    overlap_policy integer,
    paused_at bigint,
    paused_by varchar(200),
    pause_reason varchar(250),
    preferred_node varchar(200),
    preferred_node_auto bool NOT NULL DEFAULT FALSE,
    prev_fire_time bigint,
    priority integer,
    retry_attempt integer,
    retry_policy varchar(250),
    start_time bigint NOT NULL,
    trigger_state text NOT NULL,
    trigger_type text NOT NULL,
    CONSTRAINT "PK_qrtz_triggers" PRIMARY KEY (sched_name, trigger_name, trigger_group),
    CONSTRAINT "FK_qrtz_triggers_qrtz_job_details_sched_name_job_name_job_group" FOREIGN KEY (sched_name, job_name, job_group) REFERENCES qrtz_job_details (sched_name, job_name, job_group)
);


CREATE TABLE qrtz_blob_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    blob_data bytea,
    CONSTRAINT "PK_qrtz_blob_triggers" PRIMARY KEY (sched_name, trigger_name, trigger_group),
    CONSTRAINT "FK_qrtz_blob_triggers_qrtz_triggers_sched_name_trigger_name_tr~" FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES qrtz_triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
);


CREATE TABLE qrtz_cron_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    cron_expression text NOT NULL,
    time_zone_id text,
    CONSTRAINT "PK_qrtz_cron_triggers" PRIMARY KEY (sched_name, trigger_name, trigger_group),
    CONSTRAINT "FK_qrtz_cron_triggers_qrtz_triggers_sched_name_trigger_name_tr~" FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES qrtz_triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
);


CREATE TABLE qrtz_simple_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    repeat_count bigint NOT NULL,
    repeat_interval bigint NOT NULL,
    times_triggered bigint NOT NULL,
    CONSTRAINT "PK_qrtz_simple_triggers" PRIMARY KEY (sched_name, trigger_name, trigger_group),
    CONSTRAINT "FK_qrtz_simple_triggers_qrtz_triggers_sched_name_trigger_name_~" FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES qrtz_triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
);


CREATE TABLE qrtz_simprop_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    bool_prop_1 bool,
    bool_prop_2 bool,
    dec_prop_1 numeric,
    dec_prop_2 numeric,
    int_prop_1 integer,
    int_prop_2 integer,
    long_prop_1 bigint,
    long_prop_2 bigint,
    str_prop_1 text,
    str_prop_2 text,
    str_prop_3 text,
    time_zone_id text,
    CONSTRAINT "PK_qrtz_simprop_triggers" PRIMARY KEY (sched_name, trigger_name, trigger_group),
    CONSTRAINT "FK_qrtz_simprop_triggers_qrtz_triggers_sched_name_trigger_name~" FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES qrtz_triggers (sched_name, trigger_name, trigger_group) ON DELETE CASCADE
);


CREATE INDEX idx_qrtz_eh_fire_inst ON qrtz_execution_history (sched_name, fire_instance_id);


CREATE INDEX idx_qrtz_eh_fired_time ON qrtz_execution_history (sched_name, fired_time);


CREATE INDEX idx_qrtz_eh_inst ON qrtz_execution_history (sched_name, instance_name);


CREATE INDEX idx_qrtz_eh_job_time ON qrtz_execution_history (sched_name, job_group, job_name, fired_time);


CREATE INDEX idx_qrtz_eh_trig_time ON qrtz_execution_history (sched_name, trigger_group, trigger_name, fired_time);


CREATE INDEX idx_qrtz_ft_inst_job_req_rcvry ON qrtz_fired_triggers (sched_name, instance_name, requests_recovery);


CREATE INDEX idx_qrtz_ft_j_g ON qrtz_fired_triggers (sched_name, job_name, job_group);


CREATE INDEX idx_qrtz_ft_t_g ON qrtz_fired_triggers (sched_name, trigger_name, trigger_group);


CREATE INDEX idx_qrtz_j_g_n ON qrtz_job_details (sched_name, job_group, job_name);


CREATE INDEX idx_qrtz_mh_inst ON qrtz_misfire_history (sched_name, instance_name);


CREATE INDEX idx_qrtz_mh_misfire_time ON qrtz_misfire_history (sched_name, misfire_time);


CREATE INDEX idx_qrtz_t_c ON qrtz_triggers (sched_name, calendar_name);


CREATE INDEX idx_qrtz_t_g_n ON qrtz_triggers (sched_name, trigger_group, trigger_name);


CREATE INDEX idx_qrtz_t_j ON qrtz_triggers (sched_name, job_name, job_group);


CREATE INDEX idx_qrtz_t_nft_st ON qrtz_triggers (sched_name, trigger_state, next_fire_time, priority DESC, misfire_instr);


