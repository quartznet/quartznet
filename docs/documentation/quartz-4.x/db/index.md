---
title: Database Schema
---

When using ADO.NET-based job store (the usual being `LocalTransactionJobStore`), Quartz requires the creation of a set of tables. Creating the initial schema or migrating existing one is a manual step, as Quartz.NET does not create or migrate these automatically.

| Table | Brief Description |
| -- | -- |
| qrtz_calendars | Stores non-standard calendars |
| qrtz_job_details | Stores `IJobDetail` data |
| qrtz_locks | locks used by quartz |
| qrtz_scheduler_state | stores `IScheduler` data |
| qrtz_triggers | Stores `ITrigger` data |
| qrtz_cron_triggers | Stores CRON trigger cron expression |
| qrtz_fired_triggers | triggers that are currently running |
| qrtz_blob_triggers | trigger table with a binary blob data storage |
| qrtz_simple_triggers | data for very simple repeat triggers |
| qrtz_simprop_triggers | Reusable table for custom triggers. `ICalendarIntervalTrigger`, `IDailyTimeIntervalTrigger`, and `IRecurrenceTrigger` use this |
| qrtz_paused_trigger_grps | `IScheduler.PauseTriggers` data |
| qrtz_paused_job_grps | `IScheduler.PauseJobs` data — one row per paused job group, so a group paused while it holds nothing is still reported as paused |

The scripts to create these tables for various providers can be found [here](https://github.com/quartznet/quartznet/tree/main/database/tables).

Upgrading an existing database instead? See [Database Schema Changes](../../database/schema-changes.md) — upgrading from 3.x to 4.x is **mandatory**, because 4.x no longer probes for the optional 3.x columns.

## Columns 4.x requires

These four columns are optional in Quartz.NET 3.x — the scheduler probes for them at startup and disables the corresponding feature when they are missing. **4.x removed those probes and assumes all four exist.**

| Column | Table(s) | Added as optional in |
| -- | -- | -- |
| `MISFIRE_ORIG_FIRE_TIME` | `QRTZ_TRIGGERS` | 3.17 |
| `EXECUTION_GROUP` | `QRTZ_TRIGGERS`, `QRTZ_FIRED_TRIGGERS` | 3.18 |
| `PREFERRED_NODE` | `QRTZ_TRIGGERS` | 3.19 |
| `PREFERRED_NODE_AUTO` | `QRTZ_TRIGGERS` | 3.19 |

4.x also needs a **table** 3.x never had: `QRTZ_PAUSED_JOB_GRPS`, which is what lets a job group be
paused while it holds nothing and what makes a job group listing report `paused` truthfully.

Apply [`database/migrations/4.0/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0) to add whichever are missing — it covers the table as well as the columns. Every statement is guarded, so it is safe to run on a database that already has some of them. [Database Schema Changes](../../database/schema-changes.md#version-4-0) lists the whole 3.x → 4.x set.

## The QRTZ_TRIGGERS table

This table stores the `ITrigger` data that is shared across all trigger types. What is specific to a type
lives in one of the sibling tables — `QRTZ_CRON_TRIGGERS`, `QRTZ_SIMPLE_TRIGGERS`, `QRTZ_SIMPROP_TRIGGERS` or
`QRTZ_BLOB_TRIGGERS` — and `TRIGGER_TYPE` says which.

| Column | Holds |
| -- | -- |
| `SCHED_NAME` | The `Scheduler:InstanceName` this row belongs to. Every table has it: one database can hold several schedulers. |
| `TRIGGER_NAME`, `TRIGGER_GROUP` | The `TriggerKey`. Together with `SCHED_NAME` they are the primary key. |
| `JOB_NAME`, `JOB_GROUP` | The `JobKey` of the job this trigger fires. |
| `DESCRIPTION` | `ITrigger.Description`. |
| `NEXT_FIRE_TIME`, `PREV_FIRE_TIME` | Fire times, as UTC ticks. Null when there is none. |
| `PRIORITY` | `ITrigger.Priority`, which breaks ties between triggers due at the same instant. |
| `TRIGGER_STATE` | The stored state — see the table below. |
| `TRIGGER_TYPE` | `CRON`, `SIMPLE`, `CAL_INT`, `DAILY_I`, `RECUR` or `BLOB`; which sibling table holds the rest. |
| `START_TIME`, `END_TIME` | The window the schedule is in force, as UTC ticks. |
| `CALENDAR_NAME` | The `QRTZ_CALENDARS` entry excluding times from this trigger's schedule, if any. |
| `MISFIRE_INSTR` | The misfire instruction, as its numeric value. |
| `MISFIRE_ORIG_FIRE_TIME` | The fire time a misfire handler moved the trigger away from, so a job can see what it missed. |
| `EXECUTION_GROUP` | The [execution group](../tutorial/execution-groups.md) this trigger's work belongs to. |
| `PREFERRED_NODE`, `PREFERRED_NODE_AUTO` | [Node affinity](../tutorial/node-affinity.md): which node should acquire this trigger, and whether it claimed the pin itself. |
| `JOB_DATA` | The trigger's own `JobDataMap`, serialized. |

### Trigger states

The string in `TRIGGER_STATE` is not the enum an application sees. The stored vocabulary is
[`StoredTriggerState`](https://github.com/quartznet/quartznet/blob/main/src/Quartz/Impl/AdoJobStore/StoredTriggerState.cs),
in `Quartz.Extensibility`:

| `TRIGGER_STATE` | Meaning |
| -- | -- |
| `WAITING` | Ready to be picked up when its time comes. This is the ordinary resting state. |
| `ACQUIRED` | A node has taken this trigger and is about to fire it. |
| `EXECUTING` | Its job is running. |
| `COMPLETE` | It will not fire again. |
| `BLOCKED` | Its job is `[DisallowConcurrentExecution]` and another firing of it is in progress. |
| `PAUSED` | Paused, and will not fire until resumed. |
| `PAUSED_BLOCKED` | Both at once: paused, and blocked by a running firing of the same job. |
| `ERROR` | The trigger could not be fired — usually because its job type could not be built. `IScheduler.ResetTriggerFromErrorState` clears it. |
| `DELETED` | A transient marker used while a trigger is being removed. |

What `IScheduler.GetTriggerState` returns is
[`TriggerState`](https://github.com/quartznet/quartznet/blob/main/src/Quartz/TriggerState.cs), which is the
API's shorter vocabulary — `Normal`, `Paused`, `Complete`, `Error`, `Blocked`, `Executing` and `None` for a
trigger that does not exist. The stored states map onto it: `WAITING` and `ACQUIRED` both read as `Normal`,
`PAUSED_BLOCKED` reads as `Paused`, and `DELETED` reads as `None`. A trigger whose job is running reads as
`Executing`, unless the row says it is deleted, in error or paused — those are reported as they stand.
`TriggerStateResolver.Resolve` is that mapping, should you need it in code of your own.
