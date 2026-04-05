# JSON Configuration

Quartz.NET supports hierarchical JSON configuration in `appsettings.json`, providing a modern alternative to flat property keys. This includes both scheduler properties and declarative job/trigger definitions.

::: tip
Requires Quartz 4.1 or later. JSON configuration support is included in the core `Quartz` package.
:::

## Hierarchical Properties

Instead of flat property keys like `"quartz.threadPool.maxConcurrency": "10"`, you can use a natural nested JSON structure:

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
      "Type": "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
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
        "Type": "Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins"
      }
    },
    "Serializer": {
      "Type": "stj"
    }
  }
}
```

### Mapping Rules

Each JSON path segment becomes a dot-separated segment in the flat property key, with PascalCase automatically converted to camelCase:

| JSON Path | Flat Property Key |
|---|---|
| `Scheduler:InstanceName` | `quartz.scheduler.instanceName` |
| `ThreadPool:MaxConcurrency` | `quartz.threadPool.maxConcurrency` |
| `DataSource:default:Provider` | `quartz.dataSource.default.provider` |
| `Plugin:jobHistory:Type` | `quartz.plugin.jobHistory.type` |

### Usage with DI

```csharp
services.AddQuartz(Configuration.GetSection("Quartz"), q =>
{
    // Additional code-based configuration still works alongside JSON
    q.AddJob<MyJob>(j => j.WithIdentity("codeJob").StoreDurably());
});
```

### Usage without DI

```csharp
var properties = QuartzConfigurationHelper.ToNameValueCollection(
    Configuration.GetSection("Quartz"));
var factory = new StdSchedulerFactory();
factory.Initialize(properties);
```

### Backward Compatibility

Flat property keys still work. You can mix both styles in the same section:

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

Jobs and triggers can be defined declaratively in `appsettings.json` under a `Schedule` sub-section:

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

Exactly one schedule type must be specified per trigger. The trigger type is determined by which nested object is present.

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

- `RepeatCount`: Number of times to repeat. Use `-1` for indefinite, `0` for fire once.
- `Interval`: TimeSpan string (e.g., `"00:00:10"` for 10 seconds, `"01:00:00"` for 1 hour).

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

### Common Trigger Fields

All trigger types support these optional fields:

| Field | Description |
|---|---|
| `Name` | Trigger name (required) |
| `Group` | Trigger group (defaults to DEFAULT) |
| `JobName` | Associated job name (required) |
| `JobGroup` | Associated job group (defaults to DEFAULT) |
| `Description` | Trigger description |
| `Priority` | Trigger priority (integer) |
| `CalendarName` | Calendar to apply |
| `StartTime` | ISO 8601 start time (e.g., `"2024-01-01T00:00:00Z"`) |
| `StartTimeSecondsInFuture` | Start time as seconds from now (mutually exclusive with StartTime) |
| `EndTime` | ISO 8601 end time |
| `JobDataMap` | Key-value pairs for the trigger's data map |

## Multiple Named Schedulers

When the `Quartz` section contains a `Schedulers` sub-section, each child is automatically registered as a named scheduler:

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

```csharp
// Registers "Primary" and "Secondary" named schedulers automatically
services.AddQuartz(Configuration.GetSection("Quartz"));
services.AddQuartzHostedService();
```

Each named scheduler section supports the same hierarchical properties, `Schedule` sub-section with `Jobs`/`Triggers`, and code-based overrides.

::: warning
Defining both a `Schedulers` sub-section and direct scheduler configuration (e.g., `Scheduler`, `ThreadPool` at the top level) is an error. Use one or the other.
:::

## Standalone JSON Files (quartz_jobs.json)

For file-based scheduling with hot-reload support, use `JsonSchedulingDataProcessorPlugin` from the `Quartz.Plugins` package. See [Quartz Plugins](quartz-plugins.md) for plugin configuration.

Standalone JSON files use the same `Jobs` and `Triggers` format as the `Schedule` section above, wrapped in an envelope with optional `PreProcessingCommands` and `ProcessingDirectives`:

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
    "OverWriteExistingData": true,
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

Commands executed before scheduling. All fields are optional:

| Field | Description |
|---|---|
| `DeleteJobsInGroup` | Array of group names. Use `"*"` to delete jobs in all groups. |
| `DeleteTriggersInGroup` | Array of group names. Use `"*"` to delete triggers in all groups. |
| `DeleteJobs` | Array of `{ "Name": "...", "Group": "..." }` objects. Group is optional. |
| `DeleteTriggers` | Array of `{ "Name": "...", "Group": "..." }` objects. Group is optional. |

### ProcessingDirectives

| Field | Default | Description |
|---|---|---|
| `OverWriteExistingData` | `true` | Replace existing jobs/triggers with the same identity. |
| `IgnoreDuplicates` | `false` | When `OverWriteExistingData` is `false`, silently skip duplicates instead of erroring. |
| `ScheduleTriggerRelativeToReplacedTrigger` | `false` | Adjust new trigger timing based on old trigger's last fire time. |
