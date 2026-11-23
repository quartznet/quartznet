-- CompleteQuartzSchema



-- MoveQuartzTablesFromAppAny

ALTER TABLE quartz.qrtz_triggers DROP CONSTRAINT "FK_qrtz_triggers_qrtz_job_details_sched_name_job_name_job_group";

ALTER TABLE quartz.qrtz_triggers ADD CONSTRAINT "FK_qrtz_triggers_qrtz_job_details_sched_name_job_name_job_group" FOREIGN KEY (sched_name, job_name, job_group) REFERENCES quartz.qrtz_job_details (sched_name, job_name, job_group);

CREATE INDEX idx_qrtz_eh_job_time ON quartz.qrtz_execution_history (sched_name, job_group, job_name, fired_time);

CREATE INDEX idx_qrtz_eh_fire_inst ON quartz.qrtz_execution_history (sched_name, fire_instance_id);

CREATE INDEX idx_qrtz_eh_trig_time ON quartz.qrtz_execution_history (sched_name, trigger_group, trigger_name, fired_time);