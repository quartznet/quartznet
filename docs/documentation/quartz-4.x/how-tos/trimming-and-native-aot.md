---
title: 'Publishing Trimmed and Native AOT'
---

# Publishing Trimmed and Native AOT

A trimmed publish removes the code an application does not reach. A native AOT publish is a trimmed
publish with an ahead-of-time compiler behind it, producing an executable that starts without a runtime
installed. Both ask a library the same question, and it is an awkward one for a scheduler: everything
you will use at run time has to be visible to a tool reading IL before the program has started.

Quartz answers it in three parts. The `Quartz` package declares `IsAotCompatible`; eight other packages
say yes or no to trimming and mean it; and the places that genuinely cannot answer — a job type that
arrives as a string — are written down rather than suppressed, so your publish tells you about them.

Start at [what is claimed](#what-quartz-claims) if you are deciding whether to try, and at
[the recipe](#the-recipe) if you have already decided.

## What Quartz claims

`<IsTrimmable>true</IsTrimmable>` is not a promise that nothing in the assembly reflects, and it is not
what decides whether Quartz gets cut into. Under `TrimMode=full` — the default for console applications
since .NET 7 and for the Web SDK since .NET 8 — every assembly is trimmed member by member whether it
is marked or not. The mark matters under `TrimMode=partial`, where an unmarked assembly is copied
whole; and it matters, everywhere, for what you are *told*.

That second half is the part worth knowing, because it is easy to misread a quiet build as a safe one.
A trimmer collapses the warnings it finds inside a `PackageReference` assembly into a single
`IL2104: Assembly 'X' produced trim warnings` — the `TrimmerSingleWarn` property, which defaults to
true. The exception is `IL2026`, the warning for calling an API that says `[RequiresUnreferencedCode]`:
it is *not* collapsed for an assembly marked `IsTrimmable`, on the reasoning that a library that went
to the trouble of marking itself meant those messages to reach you.

So an application referencing the Quartz package sees every deliberate "this API needs reflection"
message, and one line standing in for everything else. To see everything else as well:

```xml
<TrimmerSingleWarn>false</TrimmerSingleWarn>
```

`<IsAotCompatible>true</IsAotCompatible>` is a narrower claim layered on top: that nothing the package
does needs code to be *generated* at run time, so it produces no `IL3050`. Setting it turns on
`IsTrimmable` and the trim, AOT and single-file analyzers together. It is **not** a claim that no
`IL2xxx` remains — trimming and dynamic code are different questions, and Quartz answers only the second
one with a clean yes.

Quartz declares it; no other package does yet. If you want to be told which of your own dependencies
have not made the claim, .NET has an opt-in for it:

```xml
<VerifyReferenceAotCompatibility>true</VerifyReferenceAotCompatibility>
```

That reports `IL3058` for every referenced assembly carrying no `IsAotCompatible` metadata. It is
opt-in because plenty of libraries are compatible without having said so, and because the metadata
itself only arrived in .NET 10 — so expect noise, and read it as a list of things to check rather than
a list of things that are broken.

### Which packages say whether they can be trimmed

Every shipped package answers, because silence is worse than a "no".

| Package | Trimmable | What a trimmed publish reports against it |
|---|---|---|
| `Quartz` | yes, and `IsAotCompatible` | the string-named paths in the next section |
| `Quartz.Jobs` | yes | one: `DirectoryScanJob` finds the listener named in its job data by walking the loaded assemblies for a type of that name |
| `Quartz.Plugins` | yes | two: the XML and JSON schedule-file plugins name each job's type as text, which is what the file format is for |
| `Quartz.HttpClient` | yes | one: a job read back over HTTP carries its type as a name |
| `Quartz.AspNetCore` | yes | one: a job posted to the HTTP API names its type as a string |
| `Quartz.Extensions.Redis` | yes | nothing |
| `Quartz.Plugins.TimeZoneConverter` | yes | nothing |
| `Quartz.Serialization.Newtonsoft` | **no** | Json.NET decides what a type looks like by reflecting over it, and has no source-generated form to move to |
| `Quartz.Dashboard` | **no** | Blazor Server sets a component's `[Parameter]` properties by name and finds page components by type; that is the framework's model, not something the package can resolve |

Every one of the side packages' remaining warnings is an `IL2026`, which is exactly the kind that is not
collapsed — so those five lines are ones you will actually see, without setting anything.

The seven that say yes build with the trim, AOT and single-file analyzers on and warnings as errors,
and each that has anything left to record keeps it in a `TrimAnalysisBaseline.cs` beside its csproj.
Redis and TimeZoneConverter have no such file because they have nothing to put in one. Those files do
not ship, and nothing is suppressed in the shipped assemblies — deliberately, because an
`UnconditionalSuppressMessage` in a library hides a real risk from the application that inherited it.

### What still warns, and what to do about each

Nearly all of it is one habit: a type or a member named by a string. Each row is either an API that says
`[RequiresUnreferencedCode]` out loud — so avoiding it is a decision you make at compile time — or a
path that only one configuration style reaches.

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

Two entries are not about a type name and are worth separating out, because neither has an action.

`Quartz.Util.ValueConverter` reports `IL2026` and `IL2067` where a `JobDataMap` value is coerced onto a
job's property of a different type: the conversion goes through `TypeDescriptor`, which finds a
converter by reflecting over the target type, and a `PropertyInfo.PropertyType` is not something the BCL
lets anyone annotate. Keep job data to the types the map has accessors for and the converter is never
reached.

`TransientErrorDetector` reports `IL2070` reading `SQLSTATE` and `Errors[n].Number` off driver exception
types Quartz deliberately does not reference. It asks `DbException.SqlState` first, which needs no
reflection and is what most drivers answer with, and the reflective lookups behind it are null-tolerant
— so a property the trimmer removed makes an error classify as *not* transient rather than throwing.
You lose a retry, not a scheduler.

Registering job types is the only item on either list that is really work, and it is work you were
probably doing anyway: `AddJob<TJob>()` in the container is both the registration and the thing the
trimmer follows.

## The recipe

### Register the store with the driver's factory

Naming a database — `UseSqlite(connectionString)` — makes Quartz resolve the driver's connection,
command and parameter types from strings, because Quartz references no driver package. A trimmer does
not follow a string, so it removes what the name pointed at, and the registration fails while the
container is still being built with `Cannot instantiate type which has no empty constructor`. That is
not a warning to weigh up; it is a program that does not start.

Every `Use<Db>` therefore also takes the `DbProviderFactory` the driver ships, and that overload names
nothing:

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

The factory hands back an instance of every type the store uses — a connection, and the connection
makes the command, and the command makes its parameters. `SqlClientFactory.Instance`,
`NpgsqlFactory.Instance`, `MySqlConnectorFactory.Instance` and the rest work exactly the same way; the
[configuration reference](../configuration/reference.md#naming-a-driver-or-handing-over-its-factory)
lists them, and covers Oracle, which needs two settings a factory cannot carry.

::: warning The factory overload is the one to use, not merely one of two
A `DbDataSource` registered in the container is a good way to run a store for other reasons — pooling,
type mappers, logging, an Aspire-supplied connection — but it is not an equivalent answer to this
question. The overload that reaches it, `UseSqlite(db => db.UseRegisteredDataSource = true)`, carries
`[RequiresUnreferencedCode]`, because the same overload also lets you name a connection string and
nothing tells the two apart at compile time. Behind it, the data-source path still resolves the driver's
description the type-loading way, where the factory path alone skips that. In practice it works,
because an application holding a `DbDataSource` references the driver and roots its types — but it is
not the guarantee the factory overload gives, and the warning it reports is a real one.
:::

### Name job types as types

A job type survives trimming only if something the trimmer can see points at it. The generic
registrations do that, and they also declare what Quartz reflects over on a job — its public
constructors, its public properties, and the interfaces it implements:

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

`JobBuilder.Create<TJob>()`, `OfType<TJob>()`, `AddJob<TJob>()`, `AddJobType<TJob>()`,
`ScheduleJob<TJob>` and `TriggerBuilder.Create<TJob>()` all carry the annotation. Their string-taking
counterparts carry `[RequiresUnreferencedCode]` instead, so the choice shows up in your build rather
than at three in the morning.

If you wrap one of these in a generic method of your own and build with the trim analyzer on, the
forwarding type parameter needs the same annotation — the requirement travels, and the analyzer says
where it stopped.

### Declare your own job-data value types

`PublishTrimmed` sets the `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault` feature switch
to false, and `PublishAot` implies `PublishTrimmed`. An application that keeps its schedule in memory
never notices. One with a **persistent job store** writes every trigger, calendar and `JobDataMap`
through `IObjectSerializer`, and so depends on what System.Text.Json can resolve without reflecting.

Almost all of it is already answered. The default serializer carries a source-generated contract for
everything Quartz itself writes: every trigger type, every calendar type, `CronExpression`, the
`NameValueCollection` written under `useProperties`, and a `JobDataMap` holding any of the types
`DataMapExtensions` declares an accessor for — `string`, `bool`, `char`, `int`, `long`, `float`,
`double`, `decimal`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, `Dictionary<string, string>`. A
custom trigger or calendar type of your own is answered too, because `AddTriggerSerializer<TTrigger>`
and `AddCalendarSerializer<TCalendar>` know the type statically.

What is left open is a job-data value of a type only your application knows — an enum, or anything else
you put in the map. With reflection off there is no metadata for it, and the write fails with a
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

`AddTypeInfoResolver` may be called more than once. Resolvers are asked in the order they were added,
behind Quartz's own contract and in front of reflection, and one that does not know a type returns
nothing so the next is asked. With reflection on it changes nothing, so it is safe to configure
unconditionally rather than behind a build flag.

::: warning Turning reflection back on is not a way round this
`<JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>` looks
like an escape hatch and is not one: the trimmer has already removed what reflection would have needed,
so the failure moves rather than going away. In one console application the same write came back as
`FileNotFoundException: Could not load file or assembly 'System.Private.Uri'`, under `TrimMode=partial`
as much as `full`; a larger application keeps more and fails somewhere else. The .NET documentation
lists reflection-based serializers among the known trimming incompatibilities and says the answer is to
move to source generation, which is what the paragraphs above are.
:::

### Configuration binds as it is

Nothing is asked of you here, but it is worth knowing why. The `Quartz` section of `appsettings.json`
reaches `QuartzSchedulerOptions` and its siblings through a binder the compiler wrote, not through
`ConfigurationBinder`'s reflection, so an application configured from a file is as
ahead-of-time-safe as one configured in code.

That was the last `IL3050` in the package, and it was more than a warning. Built against the reflection
binder, the repository's own canary publishes natively, comes up, and reports `MaxBatchSize`,
`ShutdownJobInterruption` and the whole scheduler context sitting at their defaults — no exception, no
log line, just configuration that was read and then not used.

The flat `quartz.*` keys are the exception, and the table above says so: they name components and set
their properties by string, and no generator can help with that. They still work; they warn.

## The worked example

`src/Quartz.Trimming.Canary` in the repository is a complete application that publishes both ways and
runs. It is worth reading before you publish your own, because it is the shape of the thing that has
been proven rather than argued.

It does three checks, in order:

- **The serializer.** It asserts `JsonSerializer.IsReflectionEnabledByDefault` is false — so a green run
  cannot be one that happened to keep reflection — then round-trips every blob a job store writes
  through the ordinary `SystemTextJsonObjectSerializer`: all five trigger types, all seven calendar
  types plus a chained one, a `JobDataMap`, a `NameValueCollection` and a `CronExpression`. Each is
  serialized, read back under the very type `StdAdoDelegate` asks for, and serialized again, with the
  two payloads compared byte for byte.
- **The store.** It creates a SQLite file from the schema Quartz ships, registers it as
  `UseSqlite(SqliteFactory.Instance, …)` with `PerformSchemaValidation` on, schedules a job, waits for
  the job itself to signal that it fired, and reads the job and the trigger back through `IScheduler`.
  The firing is the point: that is where the job's type comes back out of `JOB_CLASS_NAME` as a string.
- **The binding.** It builds a whole scheduler from an in-memory `IConfiguration` and reads ten values
  back off the components that use them — the scheduler's own name, its thread pool's size, the
  scheduler context a job would read, and the rest from the container's options.

Its csproj is short, and every line of it is a decision:

```xml
<PublishTrimmed>true</PublishTrimmed>
<TrimMode>full</TrimMode>
<ILLinkTreatWarningsAsErrors>false</ILLinkTreatWarningsAsErrors>
<IlcTreatWarningsAsErrors>false</IlcTreatWarningsAsErrors>
```

There is deliberately **no** `TrimmerRootAssembly` and no suppressions file, because the point is an
application trimmed down to what it actually reaches — which is the shape in which a missing
`JsonTypeInfo` or a removed constructor shows up. The two `TreatWarningsAsErrors` are off for the same
reason: the canary reaches the recorded reflection, and a publish that stops at the first line of it is
a canary that never runs. The build target reads the warnings afterwards and checks them against
`src/Quartz/ILLink.Suppressions.xml` instead, failing on any Quartz warning that file does not record.

That check exists in that shape because ILCompiler has no `--link-attributes` option, so a suppressions
file cannot be handed to a native publish at all. One baseline, applied the one way that works for both
tools. Both legs publish the canary for the runner's own RID and then *start* it, on Windows, Linux and
macOS, on every pull request that touches code.

To run it yourself:

```shell
dotnet fallout PublishTrimmed
dotnet fallout PublishAot
```

### Reading the warnings your own publish reports

Publishing the canary trimmed reports about thirty warnings against Quartz, in five groups. Yours will
be a subset, because it depends on what your application reaches — and, from a `PackageReference`,
mostly the `IL2026` rows unless you have set `TrimmerSingleWarn` to false.

| Group | Codes | Reached by |
|---|---|---|
| `QuartzPropertyBridge`, `SchedulerPluginFactory`, `PropertyListenerFactory` | `IL2026`, `IL2067`, `IL2072`, `IL2075` | configuring with flat `quartz.*` keys |
| `SimpleTypeLoader`, `JobType` | `IL2057` | a job type named as a string, anywhere |
| `StdAdoDelegate.CreateJobType` | `IL2057`, `IL2072` | reading a job back out of an ADO.NET store |
| `BuiltInDbMetadataFactory`, `ConfigurationBasedDbMetadataFactory` | `IL2026`, `IL2057` | the ADO.NET store being in the closure at all |
| `ValueConverter`, `TransientErrorDetector`, `JsonSchedulingHelper` | `IL2026`, `IL2067`, `IL2070`, `IL2072` | job data coerced onto job properties, retry classification, jobs declared in configuration |

Two things are easy to misread here.

**A warning is about reachability, not about your configuration.** The canary registers its store with a
`DbProviderFactory` and never names a driver, and `BuiltInDbMetadataFactory`'s `IL2057` is reported all
the same — because the code that *would* resolve a driver by name is still in the closure. Seeing a
warning does not mean the path is one your application takes.

**A warning is not an error, and an error is not a warning.** The `IL2xxx` above are reports. What
actually breaks a trimmed application is quieter: a type the trimmer removed, discovered when something
asks for it. That is why the canary runs rather than only compiling — both of the real bugs this track
found were invisible to a publish that merely succeeded, and one of them broke every persistent store in
every trimmed application while producing no new warning at all.

What you should **not** see is any `IL3050` naming a Quartz member: `Quartz` produces none, and one
appearing would mean the claim had broken. Nor should you see a warning in a Quartz type that its
package's `TrimAnalysisBaseline.cs` does not list. Either of those is worth reporting — those files are
the whole record, and they are checked on every pull request.

## The two packages that are not trimmable

`Quartz.Serialization.Newtonsoft` and `Quartz.Dashboard` declare `IsTrimmable=false`, in their csproj
and on their nuget.org page, so it reads as a decision rather than an oversight.

Json.NET decides what a type looks like by reflecting over it — a contract resolver reads the members of
whatever it is handed — and there is no source-generated form of that to move to. Marking it trimmable
would tell the trimmer it may remove members that a job data map is about to be deserialized into, and
the failure would arrive when a job fires. Publish with the System.Text.Json serializer instead: it is
built into `Quartz`, it is the default, and the section above is all it asks of you.

Blazor Server is reflective by design: a component's `[Parameter]` properties are set by name from the
render tree, the router finds page components by type, and the Blazor packages themselves are not marked
trimmable either. An application that publishes trimmed does so without the dashboard, and drives its
schedulers over the [HTTP API](../packages/http-api.md) instead — both `Quartz.AspNetCore` and
`Quartz.HttpClient` are trimmable.

## How this compares

A scheduler declaring `IsAotCompatible` is unusual for the category, though no longer unique, and the
reason is structural rather than a matter of effort.

[Hangfire](https://github.com/HangfireIO/Hangfire) declares neither property, and
[#2478](https://github.com/HangfireIO/Hangfire/issues/2478) — opened by a user who hit exactly the
`IL2104: Assembly 'Hangfire.NetCore' produced trim warnings` described above — is open, with the
maintainer answering that support "requires a huge amount of work" and will take "considerable amount of
time". Its storage keeps a job's type as an assembly-qualified string and resolves it with
`Type.GetType`, which is structurally the same problem as Quartz's `JOB_CLASS_NAME` column.
[MassTransit](https://github.com/MassTransit/MassTransit/discussions/4772) is blunter: "There are no
plans in the near future, MassTransit is rich with reflection and it's almost an entire underlying
rewrite to eliminate it." [Rebus](https://github.com/rebus-org/Rebus/issues/1095) has an open issue and
a maintainer who would like to but has not tried. Coravel, NCronJob, FluentScheduler and Silverback do
not mention it.

Two do declare it, and both bought it the same way.
[TickerQ](https://github.com/Arcenox-co/TickerQ) resolves the string it stores against a
`FrozenDictionary` its source generator filled at compile time, rather than against the type system, and
[Wolverine 6](https://wolverinefx.io/guide/aot) requires handler code to be generated ahead of time and
loaded with `TypeLoadMode.Static`. That is the axis: the claim is cheap when the stored name is symbolic
and resolved against a compile-time table, and expensive when the stored name is a *type* name that
something has to turn back into a `Type`.

Quartz is in the second group and stays there, because `JOB_CLASS_NAME` is a persisted contract that
predates all of this and cannot be redefined without breaking every existing database. So the claim it
makes is the honest one for that position: no runtime code generation at all, every reflective call site
reported individually rather than hidden, and a canary that runs a real store out of a native executable
on every pull request. What it does not claim is that the string-named paths went away.

The two packages that say **no** have good company in how they say it: Json.NET upstream runs the trim
and AOT analyzers without setting either flag, precisely so that no `IsTrimmable` metadata reaches
consumers who publish with `TrimMode=partial`. The reasoning is the same one this page opened with —
the mark is a statement to the person publishing, and making it falsely is worse than not making it.

Quartz 3.x makes no statement at all: `EnableTrimAnalyzer` is in its csproj, commented out. The
[migration guide](../migration-guide.md#trimming-annotations) covers what changed.
