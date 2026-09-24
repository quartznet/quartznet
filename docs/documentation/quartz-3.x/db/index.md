---
title: Database Schema
---

An ADO.NET-based job store (usually `JobStoreTX`) needs a set of tables. Quartz.NET does not create or migrate them; you create the schema and run migrations yourself.

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
| qrtz_simprop_triggers | Reusable table for custom triggers. `ICalendarIntervalTrigger`, `IDailyTimeIntervalTrigger`, and `IRecurrenceTrigger` (3.18+) use this |
| qrtz_paused_trigger_grps | `IScheduler.PauseTriggers` data |

The [table creation scripts](https://github.com/quartznet/quartznet/tree/main/database/tables) cover the supported providers.

To upgrade an existing database, see [Database Schema Changes](../../database/schema-changes.md): every schema change by version, which migration to run, and what skipping it costs.

## Quartz Triggers Table

Stores the `ITrigger` data shared by all trigger types.

| [Trigger State](https://github.com/quartznet/quartznet/blob/main/src/Quartz/TriggerState.cs) | Description |
| -- | -- |
| Normal | trigger has fire times, and will do so on schedule |
| Paused | paused and will not execute |
| Complete | trigger will not fire again, it has no more "fire times" |
| Error | the trigger had an error, it will not be fired again |
| Blocked | this trigger is associated with a job that is `DisallowConcurrentExecutionAttribute` and so must wait, but the trigger would like to fire |
| None | the trigger doesn't exist |
| Waiting | db only, and means the job is ready to be picked up |
