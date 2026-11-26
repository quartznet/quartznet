-- CompleteQuartzSchema

-- nothing

-- MoveQuartzTablesFromAppAny

ALTER TABLE quartz.qrtz_triggers DROP CONSTRAINT "FK_qrtz_triggers_qrtz_job_details_sched_name_job_name_job_group";

ALTER TABLE quartz.qrtz_triggers ADD CONSTRAINT "FK_qrtz_triggers_qrtz_job_details_sched_name_job_name_job_group" FOREIGN KEY (sched_name, job_name, job_group) REFERENCES quartz.qrtz_job_details (sched_name, job_name, job_group);

CREATE TABLE quartz.qrtz_trigger_status (
    sched_name text NOT NULL,
    trigger_group text NOT NULL,
    trigger_name text NOT NULL,
    first_fired_time bigint NOT NULL,
    last_fired_time bigint NOT NULL,
    last_result integer NOT NULL,
    last_run_time bigint NOT NULL,
    last_instance_name text NOT NULL,
    last_entry_id text,
    last_summary text,
    last_success_time bigint,
    last_failure_time bigint,
    last_failure_message text,
    consecutive_failures integer NOT NULL DEFAULT 0,
    run_count bigint NOT NULL DEFAULT 0,
    failure_count bigint NOT NULL DEFAULT 0,
    CONSTRAINT "PK_qrtz_trigger_status" PRIMARY KEY (sched_name, trigger_group, trigger_name)
);

CREATE INDEX idx_qrtz_eh_job_time ON quartz.qrtz_execution_history (sched_name, job_group, job_name, fired_time);

CREATE INDEX idx_qrtz_eh_fire_inst ON quartz.qrtz_execution_history (sched_name, fire_instance_id);

CREATE INDEX idx_qrtz_eh_trig_time ON quartz.qrtz_execution_history (sched_name, trigger_group, trigger_name, fired_time);

ALTER TABLE quartz.qrtz_fired_triggers ADD occurrence_time bigint;

ALTER TABLE quartz.qrtz_fired_triggers ADD retry_attempt integer;