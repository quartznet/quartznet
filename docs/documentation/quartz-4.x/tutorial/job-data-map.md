---
title: 'Job Data'
---

The full reference for the `JobDataMap` introduced in [More About Jobs](more-about-jobs.md): merge
precedence, the typed accessors, `PutAsString` formats, persistent storage, and what to keep out.

## Two maps and a merge

| Map | Stored with | Use |
|---|---|---|
| `IJobDetail.JobDataMap` | the job | the same for every trigger that fires it |
| `ITrigger.JobDataMap` | the trigger | several triggers drive one job with different inputs |

`IJobExecutionContext.MergedJobDataMap` is the job's map with the trigger's map laid over it: for the
same key, **the trigger wins**. It is built lazily, once per firing. Jobs should read it:

<!-- snippet: sample_job_data_map_merged_map -->
```csharp
public sealed class ReportJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobDataMap data = context.MergedJobDataMap;
        string region = data.GetString("region")!;
        int lookbackDays = data.GetInt("lookbackDays");
        // ...

        return default;
    }
}
```
<!-- endSnippet -->

The merged map is a per-firing copy. Values written into it are not saved to the job's own map. To
persist state across fires, use
[`[PersistJobDataAfterExecution]`](#persisting-changes-across-fires) on the job's own map.

::: warning Changed in 4.x
The scheduler context is **no longer merged into the per-fire map**. In 3.x
`context.MergedJobDataMap` also carried everything in `SchedulerContext`, so a scheduler-wide key could
silently shadow a job's own key, or be shadowed by it. The merge is now job over trigger and nothing
else; read scheduler-wide values from `context.Scheduler.Context`.
:::

## Putting values in

`JobBuilder<TJob>` and `TriggerBuilder<TJob>` have the same three `UsingJobData` shapes:

<!-- snippet: sample_job_data_map_using_job_data -->
```csharp
IJobDetail job = JobBuilder.Create<ReportJob>()
    .WithIdentity("nightly", "reports")
    .UsingJobData("region", "emea")                     // key and value
    .UsingJobData(j => j.LookbackDays, 30)              // name the property, not the key
    .UsingJobData(existingMap)                          // merge a whole map in
    .Build();
```
<!-- endSnippet -->

`UsingJobData(j => j.LookbackDays, 30)` uses the property's name as the key and its type for the value,
so a rename or a type change is a compile error instead of a silent no-op at fire time. It pairs with
[property injection](#property-injection-the-other-read-side).

Data for a single firing does not need a trigger:

<!-- snippet: sample_job_data_map_trigger_job_with_data -->
```csharp
await scheduler.TriggerJob(jobKey, new JobDataMap { ["reason"] = "manual re-run" }, cancellationToken);
```
<!-- endSnippet -->

## The read side: typed accessors

`JobDataMap` implements `IDictionary<string, object?>`: indexer, `TryGetValue`, `ContainsKey`,
`Remove`, `Count`, `Keys`, `Values`, `Clear`, plus `ContainsValue` and `IsEmpty`. It also has 17 typed
accessors, as extension members on `DataMapExtensions`, in two families that read values the same way.

### The seven named types

| Family | Members | Missing or unreadable value |
|---|---|---|
| throwing readers | `GetInt`, `GetLong`, `GetFloat`, `GetDouble`, `GetBoolean`, `GetString`, `GetDateTimeOffset` | throws |
| try readers | `TryGetInt`, `TryGetLong`, `TryGetFloat`, `TryGetDouble`, `TryGetBoolean`, `TryGetString`, `TryGetDateTimeOffset` | returns `false` |

Each accepts the value **as its own type or as an invariant-culture string**:

1. the stored type is matched first;
2. a string is parsed with `CultureInfo.InvariantCulture`;
3. any other stored type falls back to `Convert` semantics.

So the same job code works whether the store kept `30` as an `int` or as `"30"`.

`GetString` returns `string?` instead of throwing on a missing key; the others throw. Use the `TryGet…`
form when the key is optional. Neither form is faster.

### The three generic readers, for every other type

For a type the seven do not name (a `Guid`, a `TimeSpan`, a `decimal`, a `DateOnly`, an enum, or a class
of your own), use a generic reader:

| Accessor | Entry missing | Entry unreadable as `T` | Entry readable as `T` |
|---|---|---|---|
| `TryGet<T>(key, out T value)` | `false` | `false` | `true`, value out |
| `Get<T>(key)` | `KeyNotFoundException` | `InvalidCastException` naming both types | the value |
| `GetValueOrDefault<T>(key, defaultValue)` | `defaultValue` | `defaultValue` | the value |

"Readable" means: the stored type first, then the invariant string form for every type `PutAsString`
writes, and an enum by name, case-insensitively. `Get<Guid>("batchId")` reads a `Guid` stored as a
string, `Get<TimeSpan>("window")` reads `"06:00:00"`, and `Get<DayOfWeek>("day")` reads `"Monday"`. A
type with no Quartz string form, such as your own class, is a plain type test.

<!-- snippet: sample_job_data_map_generic_readers -->
```csharp
// False when the entry is missing and when it holds something else.
if (data.TryGet<ReportOptions>("options", out ReportOptions? options))
{
    // ...
}

// Throws KeyNotFoundException for a missing entry, InvalidCastException for a wrong one -
// the two mistakes told apart, where TryGet answers false to both.
ReportOptions required = data.Get<ReportOptions>("options");

// Neither throws nor distinguishes: missing and wrong-typed both give the fallback.
ReportOptions effective = data.GetValueOrDefault("options", new ReportOptions());
```
<!-- endSnippet -->

* Use `Get<T>` when the entry is required: it tells a missing entry from a wrong one.
* Do not use `GetValueOrDefault<T>` where a mistyped key must be noticed: it returns the fallback for
  both.

::: tip
`SchedulerContext` has the same readers. The `PutAsString` writers belong to `JobDataMap` only, because
they take part in its change tracking.
:::

::: warning Changed in 4.x
The `Get*Value` / `Get*ValueFromString` accessor pairs are gone, and so are the nullable getters
(`GetNullableInt` and friends). One `Get…`/`TryGet…` pair per type replaces both. The accessors also
moved off `StringKeyDirtyFlagMap`, which is internal now along with `DirtyFlagMap`. Call sites are
unchanged (`map.GetString(…)` still compiles), but nothing should name the old types, and the
`Quartz.Util` namespace is gone.

The less common named accessors became `Get<T>`, which now does the coercion:

| 3.x | 4.x |
|---|---|
| `GetGuid` | `Get<Guid>` |
| `GetTimeSpan` | `Get<TimeSpan>` |
| `GetDecimal` | `Get<decimal>` |
| `GetChar` | `Get<char>` |
| `GetDateTime` | `Get<DateTime>` |
| `GetDateOnly` | `Get<DateOnly>` |
| `GetTimeOnly` | `Get<TimeOnly>` |
| `GetEnum<T>` | `Get<T>` |

Each `TryGet…` changed likewise. Reading is unchanged, including the string forms `PutAsString` writes.
:::

## Storing values as strings

`PutAsString` writes a value in a form any store can keep:

<!-- snippet: sample_job_data_map_put_as_string -->
```csharp
JobDataMap data = new();
data.PutAsString("runAt", DateTimeOffset.UtcNow);   // "O": 2026-08-22T09:15:00.0000000+00:00
data.PutAsString("window", TimeSpan.FromHours(6));  // invariant "06:00:00"
data.PutAsString("batchId", Guid.NewGuid());
data.PutAsString("lookbackDays", 30);               // any IFormattable
```
<!-- endSnippet -->

| Overload | Written as |
|---|---|
| `PutAsString(string, DateTime)` | round-trip `"O"`, invariant |
| `PutAsString(string, DateTimeOffset)` | round-trip `"O"`, invariant |
| `PutAsString(string, DateOnly)` | round-trip `"O"`: `yyyy-MM-dd` |
| `PutAsString(string, TimeOnly)` | round-trip `"O"` |
| `PutAsString(string, TimeSpan)` | invariant default format |
| `PutAsString(string, Guid)` | invariant default format |
| `PutAsString(string, bool)` | invariant default format |
| `PutAsString(string, char)` | invariant default format |
| `PutAsString<T>(string, T) where T : IFormattable` | invariant, default format |

Each round-trips through the matching accessor. `PutAsString("runAt", offset)` then
`GetDateTimeOffset("runAt")` returns the same instant and offset. `Get<DateTime>` parses with round-trip
semantics, so a `DateTime` written as `"O"` keeps its original `Kind` instead of becoming an unspecified
local time. `Get<Guid>`, `Get<TimeSpan>`, `Get<DateOnly>` and `Get<TimeOnly>` read what `PutAsString`
wrote.

## Why string-safe storage matters

A persistent store reads job data back through the serializer or, in string mode, as strings.

**The serializer.** By default a persistent store serializes the whole map.

* Every value must be serializable by the configured serializer.
* A value whose type changes shape must stay readable by the new version. A renamed property on a stored
  options class makes the job throw on its next fire, possibly months after the deploy. Standard framework
  types are safe; your own types are a versioning commitment.
* A persistent store accepts the types the accessors cover (`string`, `bool`, `char`, the numeric types,
  `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, `DateOnly`, `TimeOnly` and enums) plus
  `Dictionary<string, string>`. It refuses anything else when the job is stored, instead of writing a
  blob that fails to load later.
* Both serializers refuse the same set and write it the same way, so you can switch between them.
* A string map's entries cannot use the name `$type`. Json.NET writes a value's type there, and both
  readers take it as metadata, so a map with an entry under it is refused.

To store a type of your own, do one of:

* declare it with `SystemTextJsonSerializerRegistry.AddTypeInfoResolver` on the default serializer (the
  same registration a trimmed or native-AOT publish needs);
* declare it with `NewtonsoftJsonSerializerRegistry.AddJobDataValueType<T>()` on the Newtonsoft
  serializer;
* serialize it yourself and store the string.

A declared type is read back only by the serializer that wrote it, so the string is the portable choice.

**String mode.** `AdoJobStoreOptions.StoreJobDataAsStrings` (flat key `quartz.jobStore.useProperties`)
stores the map as name/value string pairs instead of a serialized blob. Its other store options are in
the [Configuration Reference](../configuration/reference.md).

<!-- snippet: sample_job_data_map_store_as_strings -->
```csharp
q.UsePersistentStore(s =>
{
    s.UseSqlServer(connectionString);
    s.ConfigureStore(o => o.StoreJobDataAsStrings = true);
});
```
<!-- endSnippet -->

It removes the versioning problem and makes `QRTZ_JOB_DETAILS.JOB_DATA` readable in a query tool. The
rule: **every value must be a string**. Storing a job with a `DateTimeOffset` in its map fails under
string mode. Write values with `PutAsString`; the accessors read them the same way in either mode.

::: tip
Turn `StoreJobDataAsStrings` on at the start of a project, not in the middle. Switching it on with data
already in the tables leaves rows the store cannot read.
:::

## Property injection: the other read side

If a job has settable properties named like keys in the merged map, the default job factory sets them
before `Execute` runs:

<!-- snippet: sample_job_data_map_property_injection -->
```csharp
public sealed class ReportJob : IJob
{
    public string Region { get; set; } = "";
    public int LookbackDays { get; set; }

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // Region and LookbackDays are already set

        return default;
    }
}
```
<!-- endSnippet -->

* Conversion follows the accessors' rules: `"30"` in the map sets an `int LookbackDays`.
* A key with no matching property, or a value that cannot be converted, is handled by
  `PropertySettingJobFactory.PropertyMismatchBehavior`: `Ignore`, `Warn` or `Throw`. Use `Warn` in
  development; otherwise a property silently stays at its default.
* `UsingJobData(j => j.LookbackDays, 30)` is the matching write side: name the property, get the key.

## A typed input: the third read side

A job whose data is one payload (a message, a command, an event) can declare its type with
`IJob<TInput>`. The payload arrives as a parameter:

<!-- snippet: sample_job_data_map_typed_input -->
```csharp
public sealed record SendEmail(string To, string Subject);

public sealed class SendEmailJob : IJob<SendEmail>
{
    public ValueTask Execute(IJobExecutionContext context, SendEmail input, CancellationToken cancellationToken = default)
    {
        // input.To, input.Subject - no keys, no accessors, no casts
        return default;
    }
}

public static class TypedInputScheduling
{
    public static async ValueTask Schedule(IScheduler scheduler, CancellationToken cancellationToken)
    {
        await scheduler.ScheduleJob(
            JobBuilder.Create<SendEmailJob>()
                .WithIdentity("welcome", "email")
                .Build(),
            TriggerBuilder.Create<SendEmailJob>()
                .WithIdentity("welcome-3401", "email")
                .StartNow()
                .UsingInput(new SendEmail("someone@example.org", "Welcome"))
                .Build(),
            cancellationToken: cancellationToken);
    }
}
```
<!-- endSnippet -->

* `UsingInput` is on `JobBuilder<TJob>`, `TriggerBuilder<TJob>`, and the `AddJob` and `AddTrigger`
  configurators. It is only offered for a job that declares an input; using it on any other job is a
  compile error.
* The value is stored in the ordinary `JobDataMap` under the reserved key `SchedulerConstants.JobInput`
  (`QRTZ_JOB_INPUT`). The scheduler serializes it to a **string** when the job or trigger is stored, so it
  survives `StoreJobDataAsStrings`, the serializer's type check, the blob column and the HTTP API.
* An input on the trigger overrides an input on the job.
* A job that is not an `IJob<TInput>` can read the payload with `context.GetInput<SendEmail>()`, which
  returns `null` when there is none.
* An `IJob<TInput>` whose input is missing fails the firing with a `SchedulerException` naming the key,
  instead of running on a default payload.
* **The input type is inferred from the argument.** A `payload` held as a base type is stored and read as
  that base type. Pass the type argument when the static type is not the one you mean:
  `UsingInput<SendEmailJob, SendEmail>(payload)`.
* **Put a per-firing input on the trigger.** A `[PersistJobDataAfterExecution]` job re-stores its own map
  after every firing, so an input on the job is written back each time. That is harmless, since it is
  already a string.

The scheduler's `IJobInputSerializer` writes the payload. It defaults to
`SystemTextJsonJobInputSerializer`, built from the same registry as the store's serializer; see
[JSON Serialization](../packages/system-text-json.md). A trimmed or native-AOT application declares its
payload types with `SystemTextJsonSerializerRegistry.AddTypeInfoResolver`, as for a job data value type.

## Persisting changes across fires

By default a job's stored map is written once and read many times. With `[PersistJobDataAfterExecution]`
the job's own `JobDataMap` is saved after every execution, so a counter or a watermark survives:

<!-- snippet: sample_job_data_map_persist_across_fires -->
```csharp
[PersistJobDataAfterExecution]
[DisallowConcurrentExecution]
public sealed class IncrementalSyncJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobDataMap data = context.JobDetail.JobDataMap;
        data.PutAsString("lastSyncedAt", DateTimeOffset.UtcNow);
        return default;
    }
}
```
<!-- endSnippet -->

Always add `[DisallowConcurrentExecution]` too. Without it, two firings read the same map, both write,
and one write is lost.

The map tracks changes and is only written when it changed. To force a write the map did not detect,
such as an in-place change to a stored object, set the well-known key:

<!-- snippet: sample_job_data_map_force_dirty -->
```csharp
data[SchedulerConstants.ForceJobDataMapDirty] = "true";
```
<!-- endSnippet -->

## Thread safety

`JobDataMap` is not thread-safe. A job without `[DisallowConcurrentExecution]` can have several firings
running at once, and they share the stored `IJobDetail`'s map. Reading it concurrently is fine; do not
change it from a job that can run concurrently with itself.

Each firing gets its own `MergedJobDataMap`, so per-execution data is isolated.

## What does not belong in job data

Job data is durable. On a persistent store it is in `QRTZ_JOB_DETAILS.JOB_DATA` and
`QRTZ_TRIGGERS.JOB_DATA`, in every backup, in the fired-trigger history, and in the dashboard and HTTP
API for anyone who can read a job's detail. Keep out:

* **Credentials, tokens, connection strings.** Register the secret with the container instead. The shipped
  `SendMailJob` does this: its options type has no user name or password field. See
  [Keep the SMTP credential out of job data](../packages/quartz-jobs.md#keep-the-smtp-credential-out-of-job-data);
  the pattern applies to any job that needs a secret.
* **Large payloads.** Job data is read on every fire and, under `[PersistJobDataAfterExecution]`, written
  on every fire. Store an identifier and fetch the payload in the job.
* **Live objects.** A `DbConnection`, an `HttpClient` or a logger comes from the container through the
  job's constructor.

Job data is for the inputs that distinguish one scheduled instance from another.
