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

Apply [`database/migrations/4.0/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0) to add whichever are missing. Every statement is guarded, so it is safe to run on a database that already has some of them.

## Quartz Triggers Table

This table stores the configuration of the `ITrigger` data that is shared across all types.

| [Trigger State](https://github.com/quartznet/quartznet/blob/main/src/Quartz/TriggerState.cs) | Description |
| -- | -- |
| Normal | trigger has fire times, and will do so on schedule |
| Paused | paused and will not execute |
| Complete | trigger will not fire again, it has no more "fire times" |
| Error | the trigger had an error, it will not be fired again |
| Blocked | this trigger is associated with a job that is `DisallowConcurrentExecutionAttribute` and so must wait, but the trigger would like to fire |
| Executing | the trigger is currently executing |
| None | the trigger doesn't exist |
| Waiting | db only, and means the job is ready to be picked up |
