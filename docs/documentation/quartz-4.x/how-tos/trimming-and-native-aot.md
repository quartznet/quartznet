---
title: 'Publishing Trimmed and Native AOT'
---

# Publishing Trimmed and Native AOT

A trimmed publish removes code the application does not reach; native AOT compiles the trimmed result into
an executable that needs no installed runtime. Both need everything used at run time to be visible in IL.
Quartz's position:

* `Quartz` and six side packages declare `IsAotCompatible`. Each is published by ILCompiler and run on
  every pull request.
* Every other shipped package declares whether it is trimmable.
* A job type arriving as a string resolves from the types you registered. The reflective fallback is a
  [feature switch](#feature-switches), off in a trimmed publish, and so are the flat `quartz.*` keys that
  name a component and job data converted through `TypeConverter`.
* A trimmed publish reports no warning against Quartz.

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

Seven packages declare it, and only those: each is referenced by a canary that is published natively and
*run* on every pull request, and whose native publish reports no warning against any Quartz package. The
[table below](#which-packages-say-whether-they-can-be-trimmed) names the canary for each. To list
dependencies that have not declared it:

```xml
<VerifyReferenceAotCompatibility>true</VerifyReferenceAotCompatibility>
```

It reports `IL3058` per referenced assembly without `IsAotCompatible` metadata. Expect noise: the metadata
arrived in .NET 10, and many compatible libraries do not declare it. Treat the list as things to check.

### Which packages say whether they can be trimmed

| Package | Trimmable | `IsAotCompatible` | What proves it natively | What a trimmed publish reports against it |
|---|---|---|---|---|
| `Quartz` | yes | yes | `Quartz.Trimming.Canary`: a SQLite store, configuration binding, job data bound without `TypeConverter`, a type name refused once the switches are off | nothing |
| `Quartz.Jobs` | yes | yes | `Quartz.Trimming.Canary`: `DirectoryScanJob` finds its listener by name | nothing |
| `Quartz.Plugins` | yes | yes | `Quartz.Trimming.Canary`: JSON and XML schedule files, structured job logging | nothing |
| `Quartz.Plugins.TimeZoneConverter` | yes | yes | `Quartz.Trimming.Canary`, published without ICU: a zone only the converter resolves | nothing |
| `Quartz.HttpClient` | yes | yes | `Quartz.Trimming.Canary.Wire`: drives the HTTP API over loopback, reads its event stream | nothing |
| `Quartz.AspNetCore` | yes | yes | `Quartz.Trimming.Canary.Wire`: serves the HTTP API on Kestrel | nothing |
| `Quartz.Dashboard.Agent` | yes | yes | `Quartz.Trimming.Canary.Wire`: dials a hub, is driven down the tunnel | nothing |
| `Quartz.Extensions.Redis` | yes | no: a canary needs a Redis server | — | nothing |
| `Quartz.Aspire` | yes | no: its one path names a driver, which is `[RequiresUnreferencedCode]` | — | that call site, at your `AddQuartzPersistentStore` |
| `Quartz.Weasel` and the six dialect packages | yes | no: each dialect needs its database | — | nothing |
| `Quartz.Serialization.Newtonsoft` | **no** | no | — | see [below](#the-two-packages-that-are-not-trimmable) |
| `Quartz.Dashboard` | **no** | no | — | see [below](#the-two-packages-that-are-not-trimmable) |

* Every trimmable package builds with the trim, AOT and single-file analyzers on and warnings as errors.
  No package records a warning, and the build fails a canary publish that reports one against Quartz.
* Nothing is suppressed in shipped assemblies to get there: an `UnconditionalSuppressMessage` in a library
  would hide the risk from your application.
* `IsAotCompatible` is set only where a canary runs the package natively; the claim's reach is the canary's.
  The packages without it are not known to be incompatible: nothing runs them natively on every pull request.

### Feature switches

Three switches put Quartz's reflection behind a setting. `PublishTrimmed` and `PublishAot` turn all three
off through the targets file in the `Quartz` package; nothing else changes.

| Switch | MSBuild property | Off means |
|---|---|---|
| `Quartz.TypeNameResolution.IsSupported` | `QuartzTypeNameResolutionSupport` | a type name resolves only to a type you registered; an unknown name is refused naming the registration to add |
| `Quartz.LegacyComponentKeys.IsSupported` | `QuartzLegacyComponentKeysSupport` | a flat `quartz.*` key that names a component by type, or sets its properties by string, fails `AddQuartz` naming the code form |
| `Quartz.JobData.IsTypeConverterCoercionSupported` | `QuartzJobDataTypeConverterSupport` | a job data value that no [built-in conversion](#job-data-conversions) handles is refused naming both types |

All three default to `true`; under `PublishTrimmed` or `PublishAot` the package defaults them to `false`.
A `dotnet run` of a `PublishTrimmed` project has them off too, through `runtimeconfig.json`, so what you
see in development is what the publish does.

What registers a type, for the first switch: `AddJob<TJob>()`, `AddJobType<TJob>()`,
`JobBuilder.Create<TJob>()`, `OfType<TJob>()`, `ScheduleJob<TJob>()`, `TriggerBuilder.Create<TJob>()`,
`new JobType(typeof(TJob))`, `UseTypeLoader(o => o.Map(name, typeof(TJob)))`. `[QuartzJob]` registers
through the generated `AddDeclaredJobs()`. A name is matched as stored, with or without its version, and in
its 3.x spelling. `Quartz.Jobs` registers nothing by itself: a trimmed application that schedules
`NoOpJob` calls `AddJobType<NoOpJob>()`.

What stops working, and the message:

| Off | What fails | Message |
|---|---|---|
| Type names | `ITypeLoader.LoadType` of an unregistered name: a schedule file, the `Quartz:Schedule` section, a `.type` key | `TypeLoadException`: *Type 'X' is not registered, and this publish does not resolve type names by reflection (Quartz.TypeNameResolution.IsSupported is false). Register the job type with `AddJob<T>()` or `AddJobType<T>()` …* |
| Type names | a stored `JOB_CLASS_NAME`, or a job type in a request body, nothing registered | the job lists and loads; its first fire fails to instantiate with the same text, and the trigger goes to `ERROR` |
| Type names | `UseSqlite(connectionString)` and its siblings, `quartz.dataSource.<name>.provider` | `SchedulerConfigException` at startup: *Driver 'X' is described by type names, which this publish does not resolve … Pass the driver's DbProviderFactory* |
| Component keys | `quartz.jobStore.type`, `quartz.threadPool.type`, `quartz.serializer.type`, `quartz.scheduler.typeLoadHelper.type`, `quartz.scheduler.jobFactory.type`, `quartz.scheduler.instanceIdGenerator.type`, `quartz.timeProvider.type`, `quartz.jobStore.driverDelegateType`, `quartz.jobStore.driverDelegateInitString`, `quartz.jobStore.lockHandler.type`, `quartz.dataSource.<name>.connectionProvider.type`, `quartz.dbprovider.*`, `quartz.plugin.*`, and a component's leftover keys | `SchedulerConfigException` from `AddQuartz` (`GetScheduler()` for `quartz.plugin.*`): *Configuration property 'X' names a component or sets its properties by string, which this publish does not do … In code, `UseJobStore<T>()` …* |
| Job data | a value onto a job property that no built-in conversion handles: a string onto `Version`, `CultureInfo` or a type of your own; a `DateTime` onto a `string` | `NotSupportedException`, behind the property's name: *A System.String cannot be converted to System.Version without a TypeConverter, and this publish does not convert through TypeConverter (Quartz.JobData.IsTypeConverterCoercionSupported is false) …* |
| Job data | `null` for a struct property whose type declares a parameterless constructor | not an error: the property gets the struct's zeroed default, and the constructor does not run |

The job factory logs a refused value as a warning (event `4009`) even under the default
`PropertyMismatchBehavior.Ignore`, and the job runs with that property as its constructor set it; under
`Throw` the job fails to instantiate instead.

Quartz's own spellings keep working with the second switch off, mapped to the type with no reflection:
`quartz.jobStore.type` = `RAMJobStore`; `quartz.threadPool.type` = `SimpleThreadPool`/`DefaultThreadPool`;
`quartz.serializer.type` = `stj`/`json`; `quartz.scheduler.instanceId` = `SYS_PROP`. Every value key binds
as before. `quartz.jobStore.type` = `JobStoreTX`/`LocalTransactionJobStore` or
`JobStoreCMT`/`ExternalTransactionJobStore` is accepted beside a `UsePersistentStore` call in code, which it
loses to as it always did, and refused on its own: a persistent store named by keys alone names its driver
by type, which the first switch refuses, and mapping the key to the store's type would root the whole
ADO.NET store in every trimmed application.

#### Job data conversions

These convert with the third switch off, exactly as their `TypeConverter` does: under the invariant
culture, hexadecimal integers after `#`, `0x` or `&h`, enum names in any case and combined with commas, a
blank string as `MinValue` for a date or a time, and an empty one as `null` for `Nullable<T>`.

| From | To |
|---|---|
| `string` | a primitive, `decimal`, an enum, `Guid`, `TimeSpan`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `Half`, `Int128`, `UInt128`, `Uri`, or the `Nullable<T>` of one |
| a number | a primitive; an enum, from an integral number; `string` |
| an enum | `string`; any target above, through its number |
| `bool`, `char`, `Guid`, `TimeSpan`, `Uri` | `string` |

An enum that declares its own `[TypeConverter]` is left to that converter, which needs the switch on.

To opt back in:

```xml
<PropertyGroup>
  <QuartzTypeNameResolutionSupport>true</QuartzTypeNameResolutionSupport>
  <QuartzLegacyComponentKeysSupport>true</QuartzLegacyComponentKeysSupport>
  <QuartzJobDataTypeConverterSupport>true</QuartzJobDataTypeConverterSupport>
</PropertyGroup>
```

The publish then keeps the reflection and reports `IL2026` at each guarded call site in `Quartz`. The
trimmer may still have removed what the reflection looks for; the switches exist because that is not
something a warning can rule out.

::: warning A `ProjectReference` to Quartz needs the targets file
The switches reach a `PackageReference` consumer through `buildTransitive`. A project that references
`Quartz.csproj` directly imports `src/Quartz/buildTransitive/net10.0/Quartz.targets` by path, as the
canaries do. Without a setting, ILLink and ILCompiler ignore the guard and report `IL2026` at every guarded
call, and the publish keeps the reflection.
:::

### What still warns, and what to do about it

Nothing in Quartz warns on its own. Job data coerced onto a job property through `TypeDescriptor` was the
last (`ValueConverter`'s `IL2026` and `IL2067`), and is now the third switch; the
[built-in conversions](#job-data-conversions) cover what job data carries in practice.

What is left is a decision you make at compile time, and the compiler tells you:

| What names a type as text | Where you meet it | What to do |
|---|---|---|
| A driver, chosen by name | `UseSqlServer(connectionString)` and its siblings, `[RequiresUnreferencedCode]` | [hand over the driver's factory](#register-the-store-with-the-driver-s-factory); trimmed, the name fails at startup |
| A job's type, spelled as a string | `JobType(string)`, the `string` → `JobType` cast, a schedule file, a request body, `JOB_CLASS_NAME` | register the type with `AddJob<TJob>()` or `AddJobType<TJob>()`; that is what the name resolves from |
| The flat `quartz.*` keys | `AddQuartz(NameValueCollection)`, `quartz.plugin.*`, `quartz.dbprovider.*` | configure in code or from `appsettings.json`; the [value keys](#feature-switches) still bind |

Retry classification reports nothing. It reads `SqlError.Number`, `SqliteException.SqliteErrorCode` and
Firebird's `SQLSTATE` by name, and Quartz keeps each with `[DynamicDependency]` whenever that driver is in
the application. (Earlier releases let the trimmer remove them, and a SQLite store then gave up on a busy
database instead of retrying it.)

The only real work is registering job types, and `AddJob<TJob>()` does that.

## The recipe

### Register the store with the driver's factory

Quartz references no driver, so `UseSqlite(connectionString)` resolves the driver's connection, command and
parameter types from strings. With the [type-name switch](#feature-switches) off, the container fails to
build with a `SchedulerConfigException` naming this section; with it on, the trimmer may have removed the
types and the failure is `Cannot instantiate type which has no empty constructor`. Every `Use<Db>` also
takes the driver's `DbProviderFactory`, which names nothing:

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
  `ScheduleJob<TJob>`, `TriggerBuilder.Create<TJob>()`. Each also registers the type by name, which is
  what a string — `JobType(string)`, a schedule file, a stored row — resolves from in a trimmed publish.
* Wrapping one in your own generic method: annotate the forwarding type parameter too; the trim analyzer
  says where.
* A type the trimmed application never registers is refused by name, not found by reflection. The message
  says which call to add.

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

The flat `quartz.*` keys bind their values as before. A key that names a component by type, or sets a
component's properties by string, is refused under trimming unless `QuartzLegacyComponentKeysSupport` is
`true`; Quartz's own spellings of a store, a thread pool, a serializer and `SYS_PROP` are the exception.
See [Feature switches](#feature-switches).

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
4. **The side packages.** `DirectoryScanJob` scans a directory and finds its listener in the container by
   the name in its job data (`Quartz.Jobs`); a `quartz_jobs.json` and a `quartz_jobs.xml` each name a job's
   type as text, and the job fires, with the structured job history plugin listening (`Quartz.Plugins`);
   `TimeZones.FindById` answers an id .NET alone refuses, through `UseTimeZoneConverter`
   (`Quartz.Plugins.TimeZoneConverter`). The canary publishes with `InvariantGlobalization` for the last:
   with ICU present .NET converts every IANA and Windows id itself, and no id ever reaches a resolver.
5. **Retry classification.** A `SqliteException` reporting busy or locked, wrapped the way the store wraps
   it, is transient; a constraint failure is not. The answer comes from `SqliteErrorCode`, read by name.
6. **Type names.** Asserts both [feature switches](#feature-switches) read `false`. A registered job's name
   resolves through the container's `ITypeLoader`; `NoOpJob`, registered with `AddJobType<NoOpJob>()`,
   resolves under its 4.x spelling and the 3.x one a database may hold; a type that exists in the
   executable but was never registered is refused with the `TypeLoadException` that names `AddJob<T>()`
   and the switch. That line is what shows reflection is off rather than unset: with it on, the runtime
   finds the type. Then `AddQuartz` over a property bag naming `Quartz.Simpl.RAMJobStore` builds a
   scheduler, and one naming a store Quartz does not ship is refused naming `UseJobStore<T>()`.
7. **Job data.** Asserts the third switch reads `false`. A job fires whose `int`, enum, `Guid`,
   `TimeSpan`, `DateOnly`, `TimeOnly`, `Half`, `Int128` and `UInt128` properties are set from string job
   data, the last five written as their own `ToString` writes them; a missing value for an `int?` is `null` and for a
   struct its zeroed default; a `Version` from `"1.2.3"` is refused with the message that names the property,
   both types and the switch. With the switch on, `VersionConverter` would have read it.

`src/Quartz.Trimming.Canary.Wire` does the same for the three packages that put a scheduler on a wire. One
process serves a scheduler with `Quartz.AspNetCore` on Kestrel, drives it with `Quartz.HttpClient` over
loopback, and dials a second scheduler out through `Quartz.Dashboard.Agent` to a hub on the same Kestrel.
Each step is a round trip:

| Step | What it does over HTTP |
|---|---|
| metadata | reads the scheduler's name, status and job store |
| schedule | schedules a job with a cron trigger |
| read-back | reads the job, its data, the trigger and the group listing |
| not-found | reads a missing job as `null`, from a `404` with problem details |
| events-open | opens the server-sent event stream and waits until it is live |
| pause, resume | changes the trigger's state and reads it back |
| trigger | triggers the job with extra data and waits for it to run on the server |
| events | reads the pause, the resume, the firing and the execution off the stream |
| history | lists the execution, then reads it back alone with its captured log |
| statistics | counts the run in buckets of a day |
| delete | deletes the job, and its trigger with it |
| newer-host | reads a listing carrying names this client has no member for |

Its host calls `AddJobType<TJob>()` for the job the client names, which is what a trimmed host serving the
API has to do for every job type a caller may send.

The agent half speaks the [dashboard agent](../packages/dashboard-agent.md) protocol against a hub the canary
carries, because the real hub lives in `Quartz.Dashboard`, which is not trimmable. The hub uses the
protocol's own records, so the payloads cannot drift from what a dashboard sends:

| Step | What goes down the tunnel |
|---|---|
| register | the agent connects with its bearer token and registers; the hub reads its routes |
| heartbeat | a heartbeat arrives on the interval the hub set |
| details | a `GET` of the scheduler, answered as a `SchedulerDto` |
| schedule | a `POST` with a job and a cron trigger; the job's type travels as a string |
| read-back | the job, read back as a `JobDetailDto` |
| watch | the hub asks for events; the agent opens its client-to-server stream |
| trigger | the job is triggered, runs in the agent's scheduler, and its `JobExecuted` comes up the stream |
| refused | a `Shutdown` the agent was configured to refuse, answered `403` with problem details |
| not-found | a missing job, answered `404` with problem details |
| unregister | the agent's scheduler shuts down and the agent says goodbye |

Both csprojs:

```xml
<PublishTrimmed>true</PublishTrimmed>
<TrimMode>full</TrimMode>
<ILLinkTreatWarningsAsErrors>false</ILLinkTreatWarningsAsErrors>
<IlcTreatWarningsAsErrors>false</IlcTreatWarningsAsErrors>
```

* **No** `TrimmerRootAssembly` and no suppressions file, so a missing `JsonTypeInfo` or removed constructor
  shows up.
* Warnings are not errors, because each canary reports one on purpose: `TrimWitness`, a `Type.GetType` over
  a string the analysis cannot see. The build target reads the report, requires that `IL2057`, and fails on
  any warning against a Quartz type. A publish without the witness is one whose trimmer or compiler did not
  run. (ILCompiler has no `--link-attributes`, so reading the report is the one check both publishes share.)
* Both import `src/Quartz/buildTransitive/net10.0/Quartz.targets` by path, which is what a
  `PackageReference` consumer gets for free: it turns the three switches off, and without it the publish
  reports `IL2026` at every guarded call and fails the check.
* CI publishes and *starts* both for the runner's RID, on Windows, Linux and macOS, on every pull request
  that touches code.

```shell
dotnet fallout PublishTrimmed
dotnet fallout PublishAot
```

### Reading the warnings your own publish reports

A trimmed publish with the switches off reports nothing against Quartz.

With a switch set back to `true`, the publish reports `IL2026` at each call site it guards. That is the
reflection the setting chose to keep:

| Property set to `true` | Reported against |
|---|---|
| `QuartzTypeNameResolutionSupport` | `SimpleTypeLoader`, `JobType`, `BuiltInDbMetadataFactory`, `ValueConverter` |
| `QuartzLegacyComponentKeysSupport` | `QuartzPropertyBridge`, `SchedulerPluginFactory`, `ConfigurationBasedDbMetadataFactory` |
| `QuartzJobDataTypeConverterSupport` | `ValueConverter` |

* **Warnings are about reachability.** Everything in the closure is analysed, whether or not your
  configuration reaches it; the switches are what take the unreachable half out of the closure.
* **Warnings are not failures, and failures may not warn.** What breaks a trimmed app is a removed type,
  found at run time. Both real bugs found in this work passed a successful publish; one broke every
  persistent store in every trimmed application with no new warning. That is why the canary runs.
* **Report** any warning naming a Quartz member with the switches off. Quartz records none, and every
  pull request checks that.

What remains is [#3341](https://github.com/quartznet/quartznet/issues/3341): the reflection in the drivers
themselves, which is not Quartz's to record. It is why `IsAotCompatible` is
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
| Quartz | yes, narrowly (see [above](#what-quartz-claims)) | a type name in `JOB_CLASS_NAME`, answered from a table of registered types; reflection only when `Quartz.TypeNameResolution.IsSupported` is on |
| [TickerQ](https://github.com/Arcenox-co/TickerQ) | yes | against a `FrozenDictionary` its source generator fills at compile time |
| [Wolverine 6](https://wolverinefx.io/guide/aot) | yes | handler code generated ahead of time and loaded with `TypeLoadMode.Static` |
| [Hangfire](https://github.com/HangfireIO/Hangfire) | no ([#2478](https://github.com/HangfireIO/Hangfire/issues/2478) open) | an assembly-qualified string, through `Type.GetType` |
| MassTransit | no ([discussion](https://github.com/MassTransit/MassTransit/discussions/4772)) | |
| Rebus | no ([open issue](https://github.com/rebus-org/Rebus/issues/1095)) | |

Coravel, NCronJob, FluentScheduler and Silverback do not mention it.

The claim is cheap when a stored name is resolved against a compile-time table, and expensive when it is a
*type* name. `JOB_CLASS_NAME` is a persisted contract that cannot change without breaking existing
databases, so Quartz keeps the name and changes what answers it: the types you registered, which is a
table like TickerQ's filled at startup rather than at compile time. It claims no runtime code generation,
reports the one reflective call site left, and runs a real store from a native executable on every pull
request.

Json.NET upstream also runs the trim and AOT analyzers without setting either flag, so no `IsTrimmable`
metadata reaches consumers who publish with `TrimMode=partial`. Quartz 3.x makes no statement:
`EnableTrimAnalyzer` is commented out in its csproj. See the
[migration guide](../migration-guide.md#trimming-annotations).
