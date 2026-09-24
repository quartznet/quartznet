---
title: 'Publishing Trimmed and Native AOT'
---

# Publishing Trimmed and Native AOT

A trimmed publish removes code the application does not reach; native AOT compiles the trimmed result into
an executable that needs no installed runtime. Both need everything used at run time to be visible in IL.
Quartz's position:

* `Quartz` declares `IsAotCompatible`.
* Eight other packages declare whether they are trimmable.
* Paths that cannot be analysed, such as a job type arriving as a string, are reported, not suppressed.

Deciding whether to try: [what Quartz claims](#what-quartz-claims). Ready to publish:
[the recipe](#the-recipe).

## What Quartz claims

`<IsTrimmable>true</IsTrimmable>` neither promises that nothing reflects nor decides whether Quartz is
trimmed. Under `TrimMode=full` (default for console apps since .NET 7 and the Web SDK since .NET 8) every
assembly is trimmed member by member; under `TrimMode=partial` an unmarked assembly is copied whole. What
the mark always changes is which warnings you see:

* A trimmer collapses a `PackageReference` assembly's warnings into one
  `IL2104: Assembly 'X' produced trim warnings` (`TrimmerSingleWarn`, true by default).
* `IL2026` (calling a `[RequiresUnreferencedCode]` API) is *not* collapsed for an assembly marked
  `IsTrimmable`. You see each of Quartz's "this API needs reflection" messages, plus one line for the rest.

A quiet build is not necessarily safe. To see everything:

```xml
<TrimmerSingleWarn>false</TrimmerSingleWarn>
```

`<IsAotCompatible>true</IsAotCompatible>` claims only that nothing needs code *generated* at run time: no
`IL3050`. It turns on `IsTrimmable` and the trim, AOT and single-file analyzers. It does **not** claim that
no `IL2xxx` remains.

No other package declares it yet. To list dependencies that have not:

```xml
<VerifyReferenceAotCompatibility>true</VerifyReferenceAotCompatibility>
```

It reports `IL3058` per referenced assembly without `IsAotCompatible` metadata. Expect noise: the metadata
arrived in .NET 10, and many compatible libraries do not declare it. Treat the list as things to check.

### Which packages say whether they can be trimmed

| Package | Trimmable | What a trimmed publish reports against it |
|---|---|---|
| `Quartz` | yes, and `IsAotCompatible` | the string-named paths in the next section |
| `Quartz.Jobs` | yes | one: `DirectoryScanJob` finds its listener by a type name in job data |
| `Quartz.Plugins` | yes | two: the XML and JSON schedule-file plugins name each job's type as text |
| `Quartz.HttpClient` | yes | one: a job read back over HTTP carries its type as a name |
| `Quartz.AspNetCore` | yes | one: a job posted to the HTTP API names its type as a string |
| `Quartz.Extensions.Redis` | yes | nothing |
| `Quartz.Plugins.TimeZoneConverter` | yes | nothing |
| `Quartz.Serialization.Newtonsoft` | **no** | see [below](#the-two-packages-that-are-not-trimmable) |
| `Quartz.Dashboard` | **no** | see [below](#the-two-packages-that-are-not-trimmable) |

* The side packages' remaining warnings are all `IL2026`, which is not collapsed, so you see those five.
* The seven trimmable packages build with the trim, AOT and single-file analyzers on and warnings as
  errors. What remains is recorded in a `TrimAnalysisBaseline.cs` beside each csproj (Redis and
  TimeZoneConverter need none).
* Those files do not ship, and nothing is suppressed in shipped assemblies: an
  `UnconditionalSuppressMessage` in a library would hide the risk from your application.

### What still warns, and what to do about each

Nearly all of it is a type or member named by a string: either an API marked `[RequiresUnreferencedCode]`,
which you avoid at compile time, or a path only one configuration style reaches.

| What names a type as text | Where you meet it | What to do |
|---|---|---|
| A job's type, spelled as a string | `JobType(string)`, `OfType(string)`, the `string` → `JobType` cast | use the typed forms; they carry `[DynamicallyAccessedMembers]` and warn about nothing |
| The persisted `JOB_CLASS_NAME` column | any ADO.NET job store, when it reads a job back | register the job type with `AddJob<TJob>()` or `AddJobType<TJob>()` — that call is what the trimmer follows |
| A schedule file | `job_scheduling_data` XML and its JSON twin, from `Quartz.Plugins` | the same: register the types the file names, or root them |
| Jobs declared in configuration | the `Quartz:Schedule` section, and the type loader named by `quartz.scheduler.typeLoaderType` | the same again |
| A job type in a request body | the HTTP API, and `Quartz.HttpClient` reading a job back | register the types a caller may name |
| `DirectoryScanJob`'s listener | `Quartz.Jobs`, named in the job data map | register the listener in the container, or name it in a trimmer root descriptor |
| A driver, chosen by name | `UseSqlServer(connectionString)` and its siblings | [hand over the driver's factory](#register-the-store-with-the-driver-s-factory) instead |
| The flat `quartz.*` keys | `AddQuartz(NameValueCollection)`, `quartz.plugin.*`, `quartz.*.listener.*`, `quartz.dbprovider.*` | configure in code or from `appsettings.json`; neither reaches these |

Two warnings need no action:

* **`Quartz.Util.ValueConverter`** (`IL2026`, `IL2067`): coercing a `JobDataMap` value onto a job property
  of another type goes through `TypeDescriptor`, and `PropertyInfo.PropertyType` cannot be annotated. Keep
  job data to the types the map has accessors for and it is never reached.
* **`TransientErrorDetector`** (`IL2070`): reads `SQLSTATE` and `Errors[n].Number` from driver exception
  types Quartz does not reference. It asks `DbException.SqlState` first (no reflection; most drivers answer
  it), and the fallbacks tolerate null: a trimmed property makes an error non-transient, costing a retry,
  not the scheduler.

The only real work is registering job types, and `AddJob<TJob>()` does that.

## The recipe

### Register the store with the driver's factory

Quartz references no driver, so `UseSqlite(connectionString)` resolves the driver's connection, command and
parameter types from strings. The trimmer removes them, and the container fails to build with
`Cannot instantiate type which has no empty constructor`. Every `Use<Db>` also takes the driver's
`DbProviderFactory`, which names nothing:

<!-- snippet: sample_trimming_provider_factory -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    // The driver's own factory, rather than its name. Nothing is resolved from a string, so
    // there is nothing for the trimmer to have removed.
    store.UseSqlite(SqliteFactory.Instance, connectionString);
}));
```
<!-- endSnippet -->

The factory creates the connection, which creates commands and their parameters. `SqlClientFactory.Instance`,
`NpgsqlFactory.Instance`, `MySqlConnectorFactory.Instance` and the others work the same; the
[configuration reference](../configuration/reference.md#naming-a-driver-or-handing-over-its-factory) lists
them, including Oracle, which needs two settings a factory cannot carry.

::: warning The factory overload is the one to use, not merely one of two
A registered `DbDataSource` (pooling, type mappers, logging, Aspire) does not solve this. Its overload,
`UseSqlite(db => db.UseRegisteredDataSource = true)`, carries `[RequiresUnreferencedCode]` because it can
also name a connection string, and the data-source path still resolves the driver's description by type
loading. It works in practice (an application holding a `DbDataSource` roots the driver's types), but
without the factory's guarantee, and the warning is real.
:::

### Name job types as types

The generic registrations point the trimmer at the job type and declare what Quartz reflects over: public
constructors, public properties and implemented interfaces.

<!-- snippet: sample_trimming_job_types -->
```csharp
services.AddQuartz(q =>
{
    // AddJob<T> declares what Quartz reflects over on a job — its public constructors, its
    // public properties and the interfaces it implements — so the trimmer keeps exactly those,
    // and the store finds the type when it reads JOB_CLASS_NAME back as a string.
    q.AddJob<ReportingJob>(job => job.WithIdentity("reporting").StoreDurably());

    q.AddTrigger(trigger => trigger
        .ForJob("reporting")
        .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever()));
});
```
<!-- endSnippet -->

* Annotated: `JobBuilder.Create<TJob>()`, `OfType<TJob>()`, `AddJob<TJob>()`, `AddJobType<TJob>()`,
  `ScheduleJob<TJob>`, `TriggerBuilder.Create<TJob>()`. Their string-taking counterparts carry
  `[RequiresUnreferencedCode]`.
* Wrapping one in your own generic method: annotate the forwarding type parameter too; the trim analyzer
  says where.

### Declare your own job-data value types

`PublishTrimmed` (implied by `PublishAot`) sets the `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault`
feature switch to false. That matters only with a **persistent job store**, which writes triggers,
calendars and `JobDataMap`s through `IObjectSerializer`.

The default serializer's source-generated contract already covers:

* every trigger and calendar type, `CronExpression`, and the `NameValueCollection` written under
  `useProperties`;
* `JobDataMap` values of the types `DataMapExtensions` has accessors for: `string`, `bool`, `char`, `int`,
  `long`, `float`, `double`, `decimal`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`,
  `Dictionary<string, string>`;
* your own trigger and calendar types, via `AddTriggerSerializer<TTrigger>` and
  `AddCalendarSerializer<TCalendar>`.

A job-data value of your own type (an enum, say) has no metadata, and writing it throws
`NotSupportedException` naming it. Generate the metadata:

<!-- snippet: sample_trimming_job_data_context -->
```csharp
// A job data value type of this application's own, which no contract of Quartz's can name.
public enum ReportFormat
{
    Csv,
    Pdf
}

// The metadata the registry is handed. Only a trimmed or native AOT publish needs it: with reflection
// on, the resolver chain still ends in reflection and this changes nothing.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ReportFormat))]
internal sealed partial class ReportJobDataContext : JsonSerializerContext;
```
<!-- endSnippet -->

and hand it to the registry:

<!-- snippet: sample_trimming_job_data_resolver -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlite(SqliteFactory.Instance, connectionString);
    store.UseSystemTextJsonSerializer(registry => registry.AddTypeInfoResolver(ReportJobDataContext.Default));
}));
```
<!-- endSnippet -->

`AddTypeInfoResolver` may be called repeatedly. Resolvers are asked in order, after Quartz's contract and
before reflection. With reflection on it changes nothing, so configure it unconditionally.

::: warning Turning reflection back on is not a way round this
`<JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>` does not
help: the trimmer has already removed what reflection needs. In one console application the write then
failed with `FileNotFoundException: Could not load file or assembly 'System.Private.Uri'`, under
`TrimMode=partial` as well as `full`. The .NET documentation lists reflection-based serializers as
incompatible with trimming; use source generation.
:::

### Configuration binds as it is

No action needed. The `Quartz` section of `appsettings.json` binds to `QuartzSchedulerOptions` and its
siblings through a compiler-generated binder, not `ConfigurationBinder`'s reflection, so file configuration is
as AOT-safe as code. (With the reflection binder, the canary published natively but silently left
`MaxBatchSize`, `ShutdownJobInterruption` and the scheduler context at their defaults; this was the last
`IL3050`.)

The flat `quartz.*` keys still work, with warnings: they name components and set properties by string.

## The worked example

`src/Quartz.Trimming.Canary` in the repository publishes both ways and runs. It checks, in order:

1. **The serializer.** Asserts `JsonSerializer.IsReflectionEnabledByDefault` is false, then round-trips
   every blob a job store writes (all five trigger types, all seven calendar types plus a chained one, a
   `JobDataMap`, a `NameValueCollection`, a `CronExpression`) through `SystemTextJsonObjectSerializer`,
   reading each back as the type `StdAdoDelegate` asks for and comparing re-serialized payloads byte for
   byte.
2. **The store.** Creates SQLite from Quartz's schema with `UseSqlite(SqliteFactory.Instance, …)` and
   `SchemaProvisioning.Validate`, schedules a job, waits for it to fire (the job type comes back out of
   `JOB_CLASS_NAME` as a string), and reads the job and trigger back through `IScheduler`.
3. **The binding.** Builds a scheduler from an in-memory `IConfiguration` and reads ten values back from
   the components: the scheduler name, thread pool size, scheduler context, and the rest from options.

Its csproj:

```xml
<PublishTrimmed>true</PublishTrimmed>
<TrimMode>full</TrimMode>
<ILLinkTreatWarningsAsErrors>false</ILLinkTreatWarningsAsErrors>
<IlcTreatWarningsAsErrors>false</IlcTreatWarningsAsErrors>
```

* **No** `TrimmerRootAssembly` and no suppressions file, so a missing `JsonTypeInfo` or removed constructor
  shows up.
* Warnings are not errors, because the canary reaches the recorded reflection. The build target instead
  checks them against `src/Quartz/ILLink.Suppressions.xml` and fails on any unrecorded Quartz warning.
  (ILCompiler has no `--link-attributes`, so a suppressions file cannot be passed to a native publish.)
* CI publishes and *starts* it for the runner's RID, on Windows, Linux and macOS, on every pull request
  that touches code.

```shell
dotnet fallout PublishTrimmed
dotnet fallout PublishAot
```

### Reading the warnings your own publish reports

A trimmed canary publish reports about thirty warnings against Quartz. Yours will be a subset, mostly the
`IL2026` rows unless `TrimmerSingleWarn` is false.

| Group | Codes | Reached by |
|---|---|---|
| `QuartzPropertyBridge`, `SchedulerPluginFactory` | `IL2026`, `IL2067`, `IL2072` | configuring with flat `quartz.*` keys |
| `SimpleTypeLoader`, `JobType` | `IL2057` | a job type named as a string, anywhere |
| `StdAdoDelegate.CreateJobType` | `IL2057`, `IL2072` | reading a job back out of an ADO.NET store |
| `BuiltInDbMetadataFactory`, `ConfigurationBasedDbMetadataFactory` | `IL2026`, `IL2057` | the ADO.NET store being in the closure at all |
| `ValueConverter`, `TransientErrorDetector`, `JsonSchedulingHelper` | `IL2026`, `IL2067`, `IL2070`, `IL2072` | job data coerced onto job properties, retry classification, jobs declared in configuration |

* **Warnings are about reachability.** The canary never names a driver, yet `BuiltInDbMetadataFactory`'s
  `IL2057` appears because that code is in the closure.
* **Warnings are not failures, and failures may not warn.** What breaks a trimmed app is a removed type,
  found at run time. Both real bugs found in this work passed a successful publish; one broke every
  persistent store in every trimmed application with no new warning. That is why the canary runs.
* **Report** any `IL3050` naming a Quartz member, or a warning in a Quartz type missing from its package's
  `TrimAnalysisBaseline.cs`. Those files are the complete record, checked on every pull request.

Closing the remaining string-named `IL2xxx` is tracked by
[#3341](https://github.com/quartznet/quartznet/issues/3341). They are why `IsAotCompatible` is
[the narrow claim](#what-quartz-claims).

## The two packages that are not trimmable

`Quartz.Serialization.Newtonsoft` and `Quartz.Dashboard` declare `IsTrimmable=false`, in the csproj and on
nuget.org.

* **Newtonsoft:** Json.NET reflects over types and has no source-generated form. Marked trimmable, it would
  let the trimmer remove members a job data map is deserialized into, failing when a job fires. Use the
  default System.Text.Json serializer, built into `Quartz`.
* **Dashboard:** Blazor Server sets `[Parameter]` properties by name and finds pages by type, and the Blazor
  packages are not trimmable either. A trimmed application drives its schedulers over the
  [HTTP API](../packages/http-api.md) instead; `Quartz.AspNetCore` and `Quartz.HttpClient` are trimmable.

## How this compares

| Library | Declares `IsAotCompatible` | How a stored job is resolved |
|---|---|---|
| Quartz | yes, narrowly (see [above](#what-quartz-claims)) | a type name in `JOB_CLASS_NAME`, turned back into a `Type` |
| [TickerQ](https://github.com/Arcenox-co/TickerQ) | yes | against a `FrozenDictionary` its source generator fills at compile time |
| [Wolverine 6](https://wolverinefx.io/guide/aot) | yes | handler code generated ahead of time and loaded with `TypeLoadMode.Static` |
| [Hangfire](https://github.com/HangfireIO/Hangfire) | no ([#2478](https://github.com/HangfireIO/Hangfire/issues/2478) open) | an assembly-qualified string, through `Type.GetType` |
| MassTransit | no ([discussion](https://github.com/MassTransit/MassTransit/discussions/4772)) | |
| Rebus | no ([open issue](https://github.com/rebus-org/Rebus/issues/1095)) | |

Coravel, NCronJob, FluentScheduler and Silverback do not mention it.

The claim is cheap when a stored name is resolved against a compile-time table, and expensive when it is a
*type* name. `JOB_CLASS_NAME` is a persisted contract that cannot change without breaking existing
databases, so Quartz claims no runtime code generation, reports every reflective call site, and runs a real
store from a native executable on every pull request; it does not claim the string-named paths are gone.

Json.NET upstream also runs the trim and AOT analyzers without setting either flag, so no `IsTrimmable`
metadata reaches consumers who publish with `TrimMode=partial`. Quartz 3.x makes no statement:
`EnableTrimAnalyzer` is commented out in its csproj. See the
[migration guide](../migration-guide.md#trimming-annotations).
