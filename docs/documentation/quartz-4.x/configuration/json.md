# JSON Configuration

`appsettings.json` can hold both scheduler settings, as nested JSON instead of flat property keys, and
job and trigger definitions.

::: tip
JSON configuration is in the core `Quartz` package. The [Configuration Reference](reference.md) lists
every option.
:::

## Hierarchical Properties

Instead of flat keys like `"quartz.threadPool.maxConcurrency": "10"`, nest the settings:

```json
{
  "Quartz": {
    "Scheduler": {
      "InstanceName": "My Scheduler",
      "InstanceId": "AUTO"
    },
    "ThreadPool": {
      "MaxConcurrency": 10
    },
    "JobStore": {
      "Type": "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz",
      "DataSource": "default",
      "TablePrefix": "QRTZ_"
    },
    "DataSource": {
      "default": {
        "Provider": "SqlServer",
        "ConnectionString": "Server=localhost;Database=quartznet"
      }
    },
    "Plugin": {
      "jobHistory": {
        "Type": "Quartz.Plugins.History.LoggingJobHistoryPlugin, Quartz.Plugins"
      }
    },
    "Serializer": {
      "Type": "stj"
    }
  }
}
```

### Mapping Rules

Each JSON path segment becomes a dot-separated segment of the flat key, with PascalCase converted to
camelCase:

| JSON Path | Flat Property Key |
|---|---|
| `Scheduler:InstanceName` | `quartz.scheduler.instanceName` |
| `ThreadPool:MaxConcurrency` | `quartz.threadPool.maxConcurrency` |
| `DataSource:default:Provider` | `quartz.dataSource.default.provider` |
| `Plugin:jobHistory:Type` | `quartz.plugin.jobHistory.type` |

### Usage with DI

<!-- snippet: sample_configuration_json_with_di -->
```csharp
services.AddQuartz(Configuration.GetSection("Quartz"), q =>
{
    // Additional code-based configuration still works alongside JSON
    q.AddJob<MyJob>(j => j.WithIdentity("codeJob").StoreDurably());
});
```
<!-- endSnippet -->

### Usage without DI

`QuartzSchedulerBuilder` reads the same section: hand it the `IConfiguration`, and it binds the typed
options and translates the flat keys, as `AddQuartz` does.

<!-- snippet: sample_configuration_json_without_di -->
```csharp
ISchedulerFactory factory = QuartzSchedulerBuilder.Create()
    .UseConfiguration(Configuration.GetSection("Quartz"))
    .Build();
```
<!-- endSnippet -->

A `NameValueCollection` you built yourself (from a properties file, from environment variables) goes in
through `UseProperties(properties)`.

### Backward Compatibility

Flat property keys still work, and both styles can be mixed in one section:

```json
{
  "Quartz": {
    "quartz.scheduler.instanceId": "AUTO",
    "ThreadPool": {
      "MaxConcurrency": 10
    }
  }
}
```

## JSON Scheduling Data

Jobs and triggers can be declared in `appsettings.json` under a `Schedule` sub-section:

```json
{
  "Quartz": {
    "Scheduler": {
      "InstanceName": "My Scheduler"
    },
    "Schedule": {
      "Jobs": [
        {
          "Name": "sampleJob",
          "Group": "sampleGroup",
          "JobType": "MyApp.Jobs.SampleJob, MyApp",
          "Description": "A sample job",
          "Durable": true,
          "Recover": false,
          "JobDataMap": {
            "connectionString": "Server=localhost",
            "retryCount": "3"
          }
        }
      ],
      "Triggers": [
        {
          "Name": "cronTrigger",
          "JobName": "sampleJob",
          "JobGroup": "sampleGroup",
          "Description": "Fires every 10 seconds",
          "Cron": {
            "Expression": "0/10 * * * * ?",
            "TimeZone": "UTC"
          }
        }
      ]
    }
  }
}
```

### Trigger Types

Each trigger has exactly one schedule object — `Simple`, `Cron`, `CalendarInterval`, `DailyTimeInterval`
or `Recurrence` — and that object decides the trigger type.

#### Simple Trigger

```json
{
  "Name": "simpleTrigger",
  "JobName": "myJob",
  "Simple": {
    "RepeatCount": -1,
    "Interval": "00:00:10",
    "MisfireInstruction": "SmartPolicy"
  }
}
```

- `RepeatCount`: how many times to repeat; `-1` for indefinite, `0` to fire once.
- `Interval`: a TimeSpan string, e.g. `"00:00:10"` for 10 seconds, `"01:00:00"` for 1 hour.

#### Cron Trigger

```json
{
  "Name": "cronTrigger",
  "JobName": "myJob",
  "Cron": {
    "Expression": "0/30 * * * * ?",
    "TimeZone": "America/New_York",
    "MisfireInstruction": "DoNothing"
  }
}
```

#### Calendar Interval Trigger

```json
{
  "Name": "calendarTrigger",
  "JobName": "myJob",
  "CalendarInterval": {
    "RepeatInterval": 1,
    "RepeatIntervalUnit": "Day",
    "MisfireInstruction": "SmartPolicy"
  }
}
```

`RepeatIntervalUnit` values: `Second`, `Minute`, `Hour`, `Day`, `Week`, `Month`, `Year`.

#### Daily Time Interval Trigger

```json
{
  "Name": "businessHoursTrigger",
  "JobName": "myJob",
  "DailyTimeInterval": {
    "RepeatInterval": 15,
    "RepeatIntervalUnit": "Minute",
    "RepeatCount": -1,
    "StartTimeOfDay": "08:00:00",
    "EndTimeOfDay": "17:00:00",
    "DaysOfWeek": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
    "TimeZone": "America/Chicago"
  }
}
```

#### Recurrence Trigger

```json
{
  "Name": "recurrenceTrigger",
  "JobName": "myJob",
  "StartTime": "2026-01-05T09:00:00Z",
  "Recurrence": {
    "Rule": "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO",
    "TimeZone": "America/New_York",
    "MisfireInstruction": "DoNothing"
  }
}
```

- `Rule`: the RFC 5545 recurrence rule, required. See [Recurrence Triggers](../tutorial/recurrencetrigger.md).
- `TimeZone`: the zone the rule's days and times are read in. Defaults to the machine's local zone, so
  name it if the schedule must mean the same thing wherever it runs.
- `MisfireInstruction`: `SmartPolicy` (the default), `FireOnceNow`, `DoNothing` or `IgnoreMisfirePolicy`.

The trigger's `StartTime` anchors the rule, as `DTSTART` does in iCalendar: `FREQ=WEEKLY;INTERVAL=2` is
every second week counted from the start time. Without a `StartTime`, the anchor is the moment the
scheduler read the declaration. A rule that does not parse is refused, naming the rule, when the file is
read.

::: tip
This trigger kind is JSON only. The XML format is
[frozen](../packages/quartz-plugins.md#the-xml-trigger-kinds-are-frozen) at its three trigger kinds and
will not gain a `<recurrence>` element; declare a recurrence rule in JSON or in code.
:::

### Common Trigger Fields

| Field | Description |
|---|---|
| `Name` | Trigger name (required) |
| `Group` | Trigger group (defaults to DEFAULT) |
| `JobName` | Associated job name (required) |
| `JobGroup` | Associated job group (defaults to DEFAULT) |
| `Description` | Trigger description |
| `Priority` | Trigger priority (integer) |
| `CalendarName` | Calendar to apply |
| `ExecutionGroup` | The trigger's [execution group](../tutorial/execution-groups.md) |
| `RetryPolicy` | The trigger's [retry policy](../how-tos/retrying-failed-jobs.md) in stored form, e.g. `fixed;3;00:00:30` |
| `PreferredNode` | The cluster node the trigger [prefers](../tutorial/node-affinity.md): a scheduler instance id, or `"*"` for whichever node fires it first. Omitted: unpinned |
| `ContinuesAfter` | The trigger this one waits for, as a `Name`/`Group` pair — a [continuation](../how-tos/job-continuations.md). Omitted: fires on its own schedule |
| `ContinuationCondition` | Which outcomes release the wait: `OnSuccess`, `OnFailure`, `OnCancellation`, `OnVeto` or `OnAnyOutcome`, joined with `\|`. Omitted: `OnSuccess` |
| `StartTime` | ISO 8601 start time (e.g., `"2024-01-01T00:00:00Z"`) |
| `StartTimeSecondsInFuture` | Start time as seconds from now (mutually exclusive with StartTime) |
| `EndTime` | ISO 8601 end time |
| `JobDataMap` | Key-value pairs for the trigger's data map |

`PreferredNode` describes the deployment, not the schedule: every machine that reads the file pins the
trigger to the node it names.

```json
{
  "Name": "nightlyReport",
  "JobName": "reportJob",
  "PreferredNode": "production-node-1",
  "Cron": { "Expression": "0 0 2 * * ?" }
}
```

::: warning
The value must match a scheduler instance id **exactly**. Pins are compared in SQL with the database's
collation, so a value differing only in case is a different node on a case-sensitive database. A pin
to a node that is not checking in is ignored: any node fires the trigger until the named node is live
again. `"*"` names no node: the first node to
fire the trigger claims it, and the pin is released if that node stops checking in.
:::

In the XML format the field is `<preferred-node>`.

### Waiting for another trigger

`ContinuesAfter` names a trigger by `Name` and optional `Group` (default `DEFAULT`), as a delete command
does. The trigger declared with it is stored [waiting](../how-tos/job-continuations.md), not scheduled:

```json
{
  "Name": "reconcile",
  "Group": "nightly",
  "JobName": "reconcileJob",
  "ContinuesAfter": { "Name": "import", "Group": "nightly" },
  "ContinuationCondition": "OnFailure|OnCancellation",
  "Cron": { "Expression": "0 0 2 * * ?" }
}
```

- The parent is looked up when the file is scheduled, not read. It may be declared later in the same file
  (the file's triggers are stored parent first) or already be in the store.
- A parent in neither is refused when the file is scheduled, with `ObjectDoesNotExistException`.
- An unknown outcome, or a `ContinuationCondition` without `ContinuesAfter`, is refused when the file is
  read, naming the trigger.

In the XML format the pair is `<continues-after>` and `<continuation-condition>`.

## Multiple Named Schedulers

Each child of a `Schedulers` sub-section is registered as a named scheduler:

```json
{
  "Quartz": {
    "Schedulers": {
      "Primary": {
        "Scheduler": {
          "InstanceId": "AUTO"
        },
        "ThreadPool": {
          "MaxConcurrency": 10
        },
        "Schedule": {
          "Jobs": [
            {
              "Name": "primaryJob",
              "JobType": "MyApp.Jobs.PrimaryJob, MyApp",
              "Durable": true
            }
          ],
          "Triggers": [
            {
              "Name": "primaryTrigger",
              "JobName": "primaryJob",
              "Cron": { "Expression": "0/10 * * * * ?" }
            }
          ]
        }
      },
      "Secondary": {
        "ThreadPool": {
          "MaxConcurrency": 5
        }
      }
    }
  }
}
```

<!-- snippet: sample_configuration_json_named_schedulers -->
```csharp
// Registers "Primary" and "Secondary" named schedulers automatically
services.AddQuartz(Configuration.GetSection("Quartz"));
services.AddQuartzHostedService();
```
<!-- endSnippet -->

Each named section supports the same properties, a `Schedule` sub-section with `Jobs`/`Triggers`, and
code-based overrides.

To register one named scheduler explicitly, pass either its own section or the root `Quartz` section;
given the root, the overload finds `Schedulers:{name}` itself:

<!-- snippet: sample_configuration_json_one_named_scheduler -->
```csharp
// Both lines are equivalent
services.AddQuartz("Primary", Configuration.GetSection("Quartz"));
services.AddQuartz("Primary", Configuration.GetSection("Quartz:Schedulers:Primary"));
```
<!-- endSnippet -->

::: warning
A `Schedulers` sub-section cannot be combined with top-level scheduler configuration (`Scheduler`,
`ThreadPool`, …) or with a top-level `Schedule`/`Scheduling` section. Move those under the matching
`Schedulers:{name}` entry.
:::

## Standalone JSON Files (quartz_jobs.json)

For file-based scheduling with hot reload, use `JsonSchedulingDataProcessorPlugin` from the
`Quartz.Plugins` package — see [Quartz Plugins](../packages/quartz-plugins.md).

A standalone file uses the same `Jobs` and `Triggers` format as the `Schedule` section, in an envelope
with optional `PreProcessingCommands` and `ProcessingDirectives`:

```json
{
  "PreProcessingCommands": {
    "DeleteJobsInGroup": ["obsoleteGroup"],
    "DeleteTriggersInGroup": ["oldTriggerGroup"],
    "DeleteJobs": [
      { "Name": "oldJob", "Group": "DEFAULT" }
    ],
    "DeleteTriggers": [
      { "Name": "oldTrigger" }
    ]
  },
  "ProcessingDirectives": {
    "OverwriteExistingData": true,
    "IgnoreDuplicates": false,
    "ScheduleTriggerRelativeToReplacedTrigger": false
  },
  "Schedule": {
    "Jobs": [
      {
        "Name": "myJob",
        "JobType": "MyApp.Jobs.MyJob, MyApp",
        "Durable": true
      }
    ],
    "Triggers": [
      {
        "Name": "myTrigger",
        "JobName": "myJob",
        "Cron": {
          "Expression": "0/30 * * * * ?"
        }
      }
    ]
  }
}
```

### PreProcessingCommands

Run before scheduling. All fields are optional:

| Field | Description |
|---|---|
| `DeleteJobsInGroup` | Array of group names. `"*"` deletes jobs in all groups. |
| `DeleteTriggersInGroup` | Array of group names. `"*"` deletes triggers in all groups. |
| `DeleteJobs` | Array of `{ "Name": "...", "Group": "..." }` objects. Group is optional. |
| `DeleteTriggers` | Array of `{ "Name": "...", "Group": "..." }` objects. Group is optional. |

### ProcessingDirectives

| Field | Default | Description |
|---|---|---|
| `OverwriteExistingData` | `true` | Replace existing jobs/triggers with the same identity. The default applies only when the file does not carry `IgnoreDuplicates`. |
| `IgnoreDuplicates` | `false` | Skip duplicates instead of failing. A file with this and no `OverwriteExistingData` gets overwriting turned off. |
| `ScheduleTriggerRelativeToReplacedTrigger` | `false` | Time a replacing trigger from the old trigger's last fire time. |

::: warning Declaring one key twice in a file is an error
The directives describe how the file relates to the scheduler, not to itself. None of them suppresses the
error for a job or trigger key (name **and** group) declared twice in one file:

```text
Trigger 'DEFAULT.myTrigger' is defined more than once in the scheduling data.
```

The same holds for the XML format's `<overwrite-existing-data>` and `<ignore-duplicates>`. Before
Quartz.NET 4, the last definition won, logged only at `Debug`.
:::

## When a file is wrong

Two settings on [`FileSchedulingOptions`](../packages/quartz-plugins.md#configuration) decide what a bad
file does. Everything below applies to the XML format too.

| Setting | Default | Effect |
|---|---|---|
| `FailOnFileNotFound` | **`true`** | A named file that does not exist stops the scheduler being built, with a `SchedulerException` naming it. `false` logs it and skips it, for an optional overlay file. |
| `FailOnSchedulingError` | **`false`** | A file that exists but is wrong is logged and reported; `true` rethrows so the deployment stops. |

The missing-file error:

```text
File named 'quartz_jobs.json' does not exist.
```

**A file that is there and is wrong** — malformed JSON, a trigger with two schedule blocks, a `JobType`
that will not load, a key declared twice. The plugin logs it, wraps it in a `SchedulerException` naming
the file, and passes it to every registered `ISchedulerListener` through `SchedulerError`. Unless
`FailOnSchedulingError` is `true`, the scheduler starts without that file's schedule.

**Which exception you get:**

- `Quartz.SchedulingDataValidationException`, a `SchedulerException`, carries **every** violation found:
  `ValidationExceptions` is the list, and `Message` has one message per line. A document is checked
  against the schema, and for duplicate keys, before any of it is applied, so three mistakes report
  three.
- Anything else — a `JobType` that will not load, a non-durable job with no trigger — is an ordinary
  `SchedulerException` raised where it happens, naming the one thing that failed.

A file is not a transaction against the store: `PreProcessingCommands` have run by the time a job is
stored, and several jobs are applied one at a time.
