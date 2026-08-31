---
title: 'Job Data'
---

[More About Jobs](more-about-jobs.md) introduces the `JobDataMap` and shows the two ends of it: putting
a value in with `UsingJobData`, taking one out with `GetString`. This page is the full inventory —
which map wins when two of them carry the same key, all the typed accessors, what `PutAsString` writes,
what survives a persistent store, and what has no business being in there at all.

## Two maps and a merge

A job's data can come from two places:

- `IJobDetail.JobDataMap` — stored with the job, the same for every trigger that fires it
- `ITrigger.JobDataMap` — stored with the trigger, so several triggers can drive one job with
  different inputs

`IJobExecutionContext.MergedJobDataMap` is the job's map with the trigger's map laid over it. Same key
in both, and **the trigger wins**. It is built once per firing, lazily, and it is the map a job should
read:

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

Writing into the merged map does nothing durable. It is a per-firing copy; values set into it are not
written back to the job's own map, and a job that wants to persist state across fires uses
[`[PersistJobDataAfterExecution]`](#persisting-changes-across-fires) on its own map instead.

::: warning Changed in 4.x
The scheduler context is **no longer merged into the per-fire map**. In 3.x
`context.MergedJobDataMap` also carried everything in `SchedulerContext`, which meant a scheduler-wide
key could silently shadow — or be shadowed by — a job's own. The merge is now job over trigger and
nothing else; read scheduler-wide values from `context.Scheduler.Context`.
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

The expression overload is worth knowing: `UsingJobData(j => j.LookbackDays, 30)` uses the property's
own name as the key and the property's own type for the value, so a rename or a type change is a
compile error rather than a silent no-op at fire time. It pairs with property injection, below.

Runtime data for a single firing does not need a trigger at all:

<!-- snippet: sample_job_data_map_trigger_job_with_data -->
```csharp
await scheduler.TriggerJob(jobKey, new JobDataMap { ["reason"] = "manual re-run" }, cancellationToken);
```
<!-- endSnippet -->

## The read side: typed accessors

`JobDataMap` implements `IDictionary<string, object?>`, so the dictionary surface is all there —
indexer, `TryGetValue`, `ContainsKey`, `Remove`, `Count`, `Keys`, `Values`, `Clear`, plus
`ContainsValue` and `IsEmpty`. On top of that come 17 typed accessors, as extension members on
`DataMapExtensions`. They fall into two families, and both read a value the same way.

### The seven named types

**Throwing readers** — the value must be there and must be readable:

`GetInt`, `GetLong`, `GetFloat`, `GetDouble`, `GetBoolean`, `GetString`, `GetDateTimeOffset`.

**Try readers** — the same seven, returning `false` instead of throwing:

`TryGetInt`, `TryGetLong`, `TryGetFloat`, `TryGetDouble`, `TryGetBoolean`, `TryGetString`,
`TryGetDateTimeOffset`.

These are the types job data is usually made of, and the ones twenty years of Quartz tutorials teach —
plus the `DateTimeOffset` Quartz's own times are. Each accepts the value **either as its own type or
as an invariant-culture string**. The stored type is matched first, a string is parsed with
`CultureInfo.InvariantCulture`, and only an exotic stored type falls back to `Convert` semantics. That
is what makes the same job code work whether the store kept `30` as an `int` or as `"30"`.

`GetString` is the one that returns `string?` rather than throwing on a missing key — the rest throw.
Reach for the `TryGet…` form whenever the key is genuinely optional; there is no performance argument
either way, it is about whether absence is an error.

### The three generic readers, for every other type

For a type the seven do not name — a `Guid`, a `TimeSpan`, a `decimal`, a `DateOnly`, an enum, or an
options class of your own — there are three more, and they read exactly what a named accessor for
that type would have read:

| Accessor | Entry missing | Entry unreadable as `T` | Entry readable as `T` |
|---|---|---|---|
| `TryGet<T>(key, out T value)` | `false` | `false` | `true`, value out |
| `Get<T>(key)` | `KeyNotFoundException` | `InvalidCastException` naming both types | the value |
| `GetValueOrDefault<T>(key, defaultValue)` | `defaultValue` | `defaultValue` | the value |

"Readable" is the same rule as above: the stored type first, then the invariant string form for every
type `PutAsString` writes one of, and an enum by name, case-insensitively. So `Get<Guid>("batchId")`
reads a `Guid` the store kept as a string, `Get<TimeSpan>("window")` reads `"06:00:00"`, and
`Get<DayOfWeek>("day")` reads `"Monday"`. A type Quartz has no string form for — your own class — is a
plain type test, which is all it could ever have been.

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

`Get<T>` is the one to reach for when the entry is a contract rather than an option: it reads what
`TryGet<T>` reads, but it says *which* of the two things went wrong instead of answering `false` to
both. `GetValueOrDefault<T>` deliberately does not distinguish them — an unreadable entry gives the
fallback exactly as a missing one does, so do not use it where a mistyped key needs to be noticed.

::: tip
The same accessors are available on `SchedulerContext`, which is the other string-keyed map in the
system. `SchedulerContext` gets the readers only — the `PutAsString` writers below belong to
`JobDataMap`, because they participate in its change tracking.
:::

::: warning Changed in 4.x
The `Get*Value` / `Get*ValueFromString` accessor pairs are gone, and so are the nullable getters
(`GetNullableInt` and friends) — one `Get…`/`TryGet…` pair per type replaces both. The accessors also
moved off `StringKeyDirtyFlagMap`, which is internal now along with `DirtyFlagMap`; call sites are
unchanged (`map.GetString(…)` still compiles) but nothing should name the old types, and the
`Quartz.Util` namespace is gone.

A named accessor per readable type is a set that only ever grows, so the exotic half of it went and
`Get<T>` grew the coercion instead: `GetGuid` → `Get<Guid>`, `GetTimeSpan` → `Get<TimeSpan>`,
`GetDecimal` → `Get<decimal>`, `GetChar` → `Get<char>`, `GetDateTime` → `Get<DateTime>`,
`GetDateOnly` → `Get<DateOnly>`, `GetTimeOnly` → `Get<TimeOnly>`, `GetEnum<T>` → `Get<T>`, and each
`TryGet…` likewise. The reading is unchanged, including the string forms `PutAsString` writes.
:::

## Storing values as strings

`PutAsString` writes a value in a form that survives anything:

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
| `PutAsString(string, DateOnly)` | round-trip `"O"` — `yyyy-MM-dd` |
| `PutAsString(string, TimeOnly)` | round-trip `"O"` |
| `PutAsString(string, TimeSpan)` | invariant default format |
| `PutAsString(string, Guid)` | invariant default format |
| `PutAsString(string, bool)` | invariant default format |
| `PutAsString(string, char)` | invariant default format |
| `PutAsString<T>(string, T) where T : IFormattable` | invariant, default format |

Every one of these round-trips through the matching accessor: `PutAsString("runAt", offset)` then
`GetDateTimeOffset("runAt")` gives back the same instant, including the offset. The same holds for the
types read through `Get<T>` — `Get<DateTime>` parses with round-trip semantics, so a `DateTime`
written as `"O"` comes back with its original `Kind` rather than as an unspecified local time, and
`Get<Guid>`, `Get<TimeSpan>`, `Get<DateOnly>` and `Get<TimeOnly>` read what `PutAsString` wrote.

## Why string-safe storage matters

Two things read job data back out of a database, and neither is your code:

**The serializer.** With the default settings a persistent store serializes the whole map. Anything in
it has to be serializable by the configured serializer, and anything you *change the shape of* has to
stay readable by the new version — a renamed property on a stored options class is a job that throws on
its next fire, months after the deploy that renamed it. Standard framework types are safe; your own
types are a versioning commitment.

A persistent store accepts exactly the types the accessors above cover (`string`, `bool`, `char`, the
numeric types, `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, `DateOnly`, `TimeOnly` and enums) plus
`Dictionary<string, string>`, and refuses anything else when the job is stored rather than writing a blob
that fails to load later. Both serializers refuse the same set and write it the same way, so a value you
can store is one either of them can be switched to. The one name a string map's own entries cannot use is
`$type`, which is where Json.NET writes a value's type: both readers take it as metadata rather than data,
so a map storing an entry under it is refused with the rest. To store a type of your own, declare it — with
`SystemTextJsonSerializerRegistry.AddTypeInfoResolver` on the default serializer, which is the same
registration a trimmed or native-AOT publish needs, or with
`NewtonsoftJsonSerializerRegistry.AddJobDataValueType<T>()` on the Newtonsoft one — or serialize it
yourself and store the result as a string. A declared type is read back by the serializer that wrote it
and by no other, so the string is the portable answer.

**String mode.** `AdoJobStoreOptions.StoreJobDataAsStrings` (the flat key is still
`quartz.jobStore.useProperties`) makes the store persist the map as name/value string pairs instead of
a serialized blob:

<!-- snippet: sample_job_data_map_store_as_strings -->
```csharp
q.UsePersistentStore(s =>
{
    s.UseSqlServer(connectionString);
    s.ConfigureStore(o => o.StoreJobDataAsStrings = true);
});
```
<!-- endSnippet -->

That removes the versioning problem entirely and makes `QRTZ_JOB_DETAILS.JOB_DATA` readable in a query
tool — at the cost of a hard rule: **every value must be a string**. Put a `DateTimeOffset` in the map
under string mode and storing the job fails. This is what `PutAsString` is for, and the accessors are
what make the reading side identical either way.

::: tip
Turn `StoreJobDataAsStrings` on at the start of a project, not in the middle. Switching it on with data
already in the tables leaves rows the store cannot read.
:::

## Property injection: the other read side

If a job has settable properties whose names match keys in the merged map, the default job factory
assigns them before `Execute` runs, and the job never touches the map:

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

The conversion rules are the accessors' rules: a `"30"` in the map sets an `int LookbackDays`. What
happens when a key has no matching property, or the value cannot be converted, is
`PropertySettingJobFactory.PropertyMismatchBehavior` — `Ignore`, `Warn` or `Throw`. `Warn` is a good
default in development, because the failure mode this replaces is a property that silently stays at its
default.

`UsingJobData(j => j.LookbackDays, 30)` is the write side of exactly this: name the property, get the
key for free.

## A typed input: the third read side

A job whose data is really *one payload* — a message, a command, an event — can say so. `IJob<TInput>`
declares the type, and the payload arrives as a parameter:

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

`UsingInput` is available on `JobBuilder<TJob>`, `TriggerBuilder<TJob>` and the two configurators
`AddJob` and `AddTrigger` hand you, and it is only offered for a job that declares an input — putting
one on a job that takes none is a compile error. The value lands in the ordinary `JobDataMap` under the
reserved key `SchedulerConstants.JobInput` (`QRTZ_JOB_INPUT`), serialized to a **string** by the
scheduler as the job or trigger is stored, so it survives `StoreJobDataAsStrings`, the JSON write gate
above, the blob column and the HTTP API alike. Precedence is the ordinary one: an input on the trigger
overrides an input on the job.

A job that is not an `IJob<TInput>` can read the same payload with `context.GetInput<SendEmail>()`,
which answers `null` when there is none. An `IJob<TInput>` whose input is missing fails the firing with
a `SchedulerException` naming the key, rather than running on a default payload.

Two things worth knowing:

- **The input type is inferred from the argument.** `UsingInput(payload)` where `payload` is held as a
  base type stores and reads it as that base type; pass the type argument explicitly —
  `UsingInput<SendEmailJob, SendEmail>(payload)` — when the static type is not the one you mean.
- **Put a per-firing input on the trigger.** A `[PersistJobDataAfterExecution]` job re-stores its own
  map after every firing, so an input on the *job* is written back each time. That is harmless — it is
  already the string it will be read as — but the trigger is where an input that differs per firing
  belongs, and it is where one trigger per payload puts it anyway.

The payload is written by the scheduler's `IJobInputSerializer`, which defaults to
`SystemTextJsonJobInputSerializer` and is built from the same registry as the store's serializer — see
[JSON Serialization](../packages/system-text-json.md). A trimmed or native-AOT application declares its
payload types with `SystemTextJsonSerializerRegistry.AddTypeInfoResolver`, exactly as it declares a job
data value type.

## Persisting changes across fires

By default a job's stored map is written once and read many times. `[PersistJobDataAfterExecution]`
changes that — the job's own `JobDataMap` is re-persisted after every execution, so a counter or a
watermark survives:

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

Note the second attribute. `[PersistJobDataAfterExecution]` without `[DisallowConcurrentExecution]` is
a race: two firings read the same map, both write, and one of the writes is lost. Use them together.

The map tracks whether it changed and is only written when it did. To force a write the map did not
notice — an in-place mutation of a stored object, for instance — put the well-known key in it:

<!-- snippet: sample_job_data_map_force_dirty -->
```csharp
data[SchedulerConstants.ForceJobDataMapDirty] = "true";
```
<!-- endSnippet -->

## Thread safety

`JobDataMap` is not thread-safe. That matters in one specific place: a job without
`[DisallowConcurrentExecution]` can have several firings in flight at once, and they share the stored
`IJobDetail`'s map. Reading it concurrently is fine; mutating it from a job that can run concurrently
with itself is not.

Each firing gets its own `MergedJobDataMap`, so anything scoped to one execution is naturally isolated.

## What does not belong in job data

Job data is *durable*. On a persistent store it lives in `QRTZ_JOB_DETAILS.JOB_DATA` and
`QRTZ_TRIGGERS.JOB_DATA`, it is in every backup, it is in the fired-trigger history, and it appears in
the dashboard and the HTTP API to anyone who can read a job's detail.

So: **no credentials, no tokens, no connection strings.** The shipped `SendMailJob` makes the point —
its options type has no user name or password field at all, and the credential is registered with the
container instead. See
[Keep the SMTP credential out of job data](../packages/quartz-jobs.md#keep-the-smtp-credential-out-of-job-data)
for the pattern; it generalizes to every job that needs a secret.

Two more things to keep out:

- **Large payloads.** Job data is read on every fire and, under `[PersistJobDataAfterExecution]`,
  written on every fire. Put an identifier in the map and fetch the payload in the job.
- **Live objects.** A `DbConnection`, an `HttpClient`, a logger — these come from the container through
  the job's constructor. Job data is for the *inputs that distinguish one scheduled instance from
  another*, and nothing else.

## See also

- [More About Jobs](more-about-jobs.md) — job details, the job factory, and property injection in context
- [JSON Serialization](../packages/system-text-json.md) — what a persistent store does with the map
- [Configuration Reference](../configuration/reference.md) — `StoreJobDataAsStrings` and the rest of the store options
