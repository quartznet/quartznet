# Blob columns a released Quartz 3.20.0 wrote

Every file here came out of a `QRTZ_JOB_DETAILS.JOB_DATA`, `QRTZ_TRIGGERS.JOB_DATA`,
`QRTZ_CALENDARS.CALENDAR` or `QRTZ_BLOB_TRIGGERS.BLOB_DATA` column, byte for byte, after
`src/Quartz.Tests.Integration.Seeder` — a console project on the **released `Quartz` 3.20.0
package** — filled a 3.20 schema with them. `LegacyJsonPayloadTest` reads them.

That provenance is the point. The literals beside them in `LegacyJsonPayloadTest.cs` are described as
"verbatim output from 3.x", and they are, but that is a comment rather than something the suite can
re-derive. These files are bytes a 3.20 process produced, and the command that produced them is
written down.

## The two folders

`stj/` and `newtonsoft/` are not the same payloads in two encodings; they are what the two serializers
a 3.x deployment could choose actually wrote, with the settings that deployment got by default:

- **`stj/`** — `Quartz.Serialization.SystemTextJson` 3.20.0. Triggers are the discriminated form, with
  a `TriggerType` field naming the family.
- **`newtonsoft/`** — `Quartz.Serialization.Json` 3.20.0 with `RegisterTriggerConverters` left at its
  default of `false`, which is what a 3.x application got unless it said otherwise. Triggers are a
  plain object graph carrying `$type`, and a `Dictionary<string, string>` job data value carries a
  `$type` of its own — the #3582 shape.

`newtonsoft/` has no `trigger-daily-time-interval.json`, and cannot: 3.20 writes that trigger's
`StartTimeOfDay` and `EndTimeOfDay` as `Quartz.TimeOfDay` objects, and `TimeOfDay` has neither a
parameterless constructor nor a `[JsonConstructor]`, so **3.20 cannot read that blob back either**.
`BlobStorageOverride.Families` in the seeder records the same thing.

## What is in a `trigger-*.json`

A stock Quartz trigger, not an application's own. `QRTZ_BLOB_TRIGGERS` normally holds a trigger the
store has no persistence delegate for, which in practice is a type the application declares — and a
blob naming an assembly the reader does not have proves nothing except that it cannot be loaded. So
the seeder declines the delegate for one trigger group instead, which puts a shipped trigger type
through the blob path. Everything about the bytes is what 3.20 wrote; what is not covered is a blob
naming a type the reader does not ship.

The timestamps are the capture's own. Regenerating these files changes `StartTimeUtc` and
`NextFireTimeUtc`, which is why nothing asserts on them.

## Regenerating

`src/Quartz.Tests.Integration.Seeder/README.md` carries the two commands. Delete the scratch database
files first — the schema script is a fresh install and will not run twice.
