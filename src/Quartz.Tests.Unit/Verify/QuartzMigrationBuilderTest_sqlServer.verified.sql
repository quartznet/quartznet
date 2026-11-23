-- CompleteQuartzSchema



-- MoveQuartzTablesFromAppAny

DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[quartz].[QRTZ_SIMPROP_TRIGGERS]') AND [c].[name] = N'STR_PROP_1');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [quartz].[QRTZ_SIMPROP_TRIGGERS] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [quartz].[QRTZ_SIMPROP_TRIGGERS] ALTER COLUMN [STR_PROP_1] nvarchar(512) NULL;

DECLARE @var1 nvarchar(max);
SELECT @var1 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[quartz].[QRTZ_SIMPROP_TRIGGERS]') AND [c].[name] = N'STR_PROP_2');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [quartz].[QRTZ_SIMPROP_TRIGGERS] DROP CONSTRAINT ' + @var1 + ';');
ALTER TABLE [quartz].[QRTZ_SIMPROP_TRIGGERS] ALTER COLUMN [STR_PROP_2] nvarchar(512) NULL;

DECLARE @var2 nvarchar(max);
SELECT @var2 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[quartz].[QRTZ_SIMPROP_TRIGGERS]') AND [c].[name] = N'STR_PROP_3');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [quartz].[QRTZ_SIMPROP_TRIGGERS] DROP CONSTRAINT ' + @var2 + ';');
ALTER TABLE [quartz].[QRTZ_SIMPROP_TRIGGERS] ALTER COLUMN [STR_PROP_3] nvarchar(512) NULL;

DROP INDEX [IDX_QRTZ_FT_INST_JOB_REQ_RCVRY] ON [quartz].[QRTZ_FIRED_TRIGGERS];
DECLARE @var3 nvarchar(max);
SELECT @var3 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[quartz].[QRTZ_FIRED_TRIGGERS]') AND [c].[name] = N'INSTANCE_NAME');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [quartz].[QRTZ_FIRED_TRIGGERS] DROP CONSTRAINT ' + @var3 + ';');
ALTER TABLE [quartz].[QRTZ_FIRED_TRIGGERS] ALTER COLUMN [INSTANCE_NAME] nvarchar(200) NOT NULL;
CREATE INDEX [IDX_QRTZ_FT_INST_JOB_REQ_RCVRY] ON [quartz].[QRTZ_FIRED_TRIGGERS] ([SCHED_NAME], [INSTANCE_NAME], [REQUESTS_RECOVERY]);

DECLARE @var4 nvarchar(max);
SELECT @var4 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[quartz].[QRTZ_FIRED_TRIGGERS]') AND [c].[name] = N'IS_NONCONCURRENT');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [quartz].[QRTZ_FIRED_TRIGGERS] DROP CONSTRAINT ' + @var4 + ';');
ALTER TABLE [quartz].[QRTZ_FIRED_TRIGGERS] ALTER COLUMN [IS_NONCONCURRENT] bit NULL;

DROP INDEX [IDX_QRTZ_T_NFT_ST] ON [quartz].[QRTZ_TRIGGERS];
DECLARE @var5 nvarchar(max);
SELECT @var5 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[quartz].[QRTZ_TRIGGERS]') AND [c].[name] = N'MISFIRE_INSTR');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [quartz].[QRTZ_TRIGGERS] DROP CONSTRAINT ' + @var5 + ';');
ALTER TABLE [quartz].[QRTZ_TRIGGERS] ALTER COLUMN [MISFIRE_INSTR] int NULL;
CREATE INDEX [IDX_QRTZ_T_NFT_ST] ON [quartz].[QRTZ_TRIGGERS] ([SCHED_NAME], [TRIGGER_STATE], [NEXT_FIRE_TIME], [PRIORITY] DESC, [MISFIRE_INSTR]);

ALTER TABLE [quartz].[QRTZ_TRIGGERS] DROP CONSTRAINT [FK_QRTZ_TRIGGERS_QRTZ_JOB_DETAILS_SCHED_NAME_JOB_NAME_JOB_GROUP];

ALTER TABLE [quartz].[QRTZ_TRIGGERS] ADD CONSTRAINT [FK_QRTZ_TRIGGERS_QRTZ_JOB_DETAILS_SCHED_NAME_JOB_NAME_JOB_GROUP] FOREIGN KEY ([SCHED_NAME], [JOB_NAME], [JOB_GROUP]) REFERENCES [quartz].[QRTZ_JOB_DETAILS] ([SCHED_NAME], [JOB_NAME], [JOB_GROUP]);

ALTER TABLE [quartz].[QRTZ_BLOB_TRIGGERS] DROP CONSTRAINT [FK_QRTZ_BLOB_TRIGGERS_QRTZ_TRIGGERS_SCHED_NAME_TRIGGER_NAME_TRIGGER_GROUP];

CREATE INDEX [IDX_QRTZ_EH_JOB_TIME] ON [quartz].[QRTZ_EXECUTION_HISTORY] ([SCHED_NAME], [JOB_GROUP], [JOB_NAME], [FIRED_TIME]);

CREATE INDEX [IDX_QRTZ_EH_FIRE_INST] ON [quartz].[QRTZ_EXECUTION_HISTORY] ([SCHED_NAME], [FIRE_INSTANCE_ID]);

CREATE INDEX [IDX_QRTZ_EH_TRIG_TIME] ON [quartz].[QRTZ_EXECUTION_HISTORY] ([SCHED_NAME], [TRIGGER_GROUP], [TRIGGER_NAME], [FIRED_TIME]);