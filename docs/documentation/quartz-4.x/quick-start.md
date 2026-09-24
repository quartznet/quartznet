---

title: Quartz 4 Quick Start
---

## Install

```shell
dotnet add package Quartz
```

That is all a scheduler needs. Dependency injection, hosting, the scheduler
[health check](packages/hosted-services-integration.md#health-checks) and System.Text.Json serialization
are in the core package. (3.x shipped them as `Quartz.Extensions.DependencyInjection`,
`Quartz.Extensions.Hosting`, `Quartz.AspNetCore` and `Quartz.Serialization.Json`.)

Optional packages:

| Package | For |
|---|---|
| [Quartz.Serialization.Newtonsoft](packages/json-serialization.md) | persisting with Newtonsoft.Json instead of System.Text.Json |
| [Quartz.Jobs](packages/quartz-jobs.md) | ready-made jobs — file scanning, sending mail, running a process |
| [Quartz.Plugins](packages/quartz-plugins.md) | history logging, JSON (and XML) schedule files, the interrupt monitor |
| [Quartz.Plugins.TimeZoneConverter](packages/timezoneconverter-integration.md) | Windows and IANA time zone ids resolving on either operating system |
| [Quartz.AspNetCore](packages/aspnet-core-integration.md) | the HTTP API |
| [Quartz.Dashboard](packages/dashboard.md) | the web dashboard |
| [Quartz.HttpClient](packages/http-client.md) | driving a remote scheduler over that API |
| [Quartz.Aspire](packages/aspire.md) | a persistent store, its telemetry and its health check from an Aspire connection name |
| [Quartz.Extensions.Redis](packages/redis.md) | Redis distributed locks for a cluster |

## The shortest thing that works

Two calls register a scheduler and start it with the host. A third schedules one firing of a job with a
payload; the compiler checks both the job type and the payload type.

<!-- snippet: sample_quick_start_shortest -->
```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddQuartz();
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

IHost host = builder.Build();
await host.StartAsync();

// Anything the container builds can do this — an endpoint, a consumer, a hosted service of
// your own. The job type and its payload type are both checked by the compiler.
IScheduler scheduler = host.Services.GetRequiredService<IScheduler>();
await scheduler.ScheduleJob<SendWelcomeEmail, string>("ada@example.com", TimeSpan.FromMinutes(5));

await host.WaitForShutdownAsync();
```
<!-- endSnippet -->

`IJob<TInput>` is the typed form of `IJob`: the payload arrives as a parameter instead of through a
`JobDataMap`.

<!-- snippet: sample_quick_start_typed_job -->
```csharp
public sealed class SendWelcomeEmail : IJob<string>
{
    public async ValueTask Execute(IJobExecutionContext context, string emailAddress, CancellationToken cancellationToken = default)
    {
        await Console.Out.WriteLineAsync($"Welcome, {emailAddress}");
    }
}
```
<!-- endSnippet -->

`AddQuartz()` with no arguments uses the defaults: the in-memory store, a thread pool of ten, and
System.Text.Json for anything serialized. Each call stores one durable job per job type and one trigger.
The returned `ScheduledOneOffJob` carries the `TriggerKey` to cancel or replace the firing by.
[One-Off Job](how-tos/one-off-job.md) covers the rest, including naming and grouping firings so a whole
conversation can be cancelled at once.

A recurring job can be declared on its class. The compiler reads the schedule, so an expression that does
not parse is a build error, not a start-up exception:

<!-- snippet: sample_declared_job -->
```csharp
[QuartzJob(Name = "cleanup", Group = "maintenance", Description = "removes rows nobody reads")]
[CronTrigger("0 0 0/6 * * ?")]
[CronTrigger("0 0 12 ? * MON-FRI", Name = "cleanup-weekday-noon", TimeZone = "Europe/Helsinki")]
public sealed class CleanupJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return default;
    }
}
```
<!-- endSnippet -->

One call registers every job the assembly declares:

<!-- snippet: sample_add_declared_jobs -->
```csharp
services.AddQuartz(q =>
{
    // Every job in this assembly that carries [QuartzJob], with the schedules it declares.
    q.AddDeclaredJobs();

    // Anything an attribute cannot say is still written here, beside it.
    q.AddTrigger<CleanupJob>(trigger => trigger
        .WithIdentity("cleanup-on-start")
        .ForJob("cleanup", "maintenance")
        .StartNow());
});

services.AddQuartzHostedService();
```
<!-- endSnippet -->

See [Declaring Jobs with Attributes](tutorial/declaring-jobs-with-attributes.md).

## Configuration

Options are strongly typed, and an option has the same name in code and in configuration files.

### In an application with a host

`Host.CreateApplicationBuilder` and `WebApplication.CreateBuilder` come from
`Microsoft.Extensions.Hosting`. The `worker` and `web` templates reference it; a plain `console` project
needs it added:

```shell
dotnet add package Microsoft.Extensions.Hosting
```

The job the registration below schedules:

<!-- snippet: sample_quick_start_job -->
```csharp
public sealed class HelloJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        await Console.Out.WriteLineAsync("Greetings from HelloJob!");
    }
}
```
<!-- endSnippet -->

A whole `Program.cs` — a worker service, a persistent store, and one job that runs every ten seconds:

<!-- snippet: sample_quick_start_host -->
```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddQuartz(q =>
{
    q.ConfigureScheduler(options => options.InstanceName = "MyScheduler");

    // default max concurrency is 10
    q.UseDefaultThreadPool(maxConcurrency: 5);

    q.UsePersistentStore(store =>
    {
        // there are other databases supported too
        store.UseSqlServer("my connection string");
        store.UseClustering();

        store.ConfigureStore(options =>
        {
            // store job data as strings, which avoids surprises when a serialized
            // type changes shape later
            options.StoreJobDataAsStrings = true;
        });
    });

    // run HelloJob now, and then every ten seconds
    q.ScheduleJob<HelloJob>(trigger => trigger
        .WithIdentity("helloTrigger")
        .StartNow()
        .WithSimpleSchedule(x => x
            .WithInterval(TimeSpan.FromSeconds(10))
            .RepeatForever()));
});

builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

IHost host = builder.Build();

// blocks until the host stops, and then until the last running job completes
await host.RunAsync();
```
<!-- endSnippet -->

* A job registered this way is built from the container for every fire, so its constructor can take a
  logger, a `DbContext` or a typed `HttpClient`.
* `WebApplication.CreateBuilder(args)` works the same way: `AddQuartz` and `AddQuartzHostedService` are
  extensions on `IHostApplicationBuilder`.
* The hosted service starts the scheduler with the application and shuts it down with it.

**Add the ADO.NET driver package yourself.** Quartz names the driver's types; your project references
the package. `UseSqlServer` needs:

```shell
dotnet add package Microsoft.Data.SqlClient
```

Without it the application compiles, then fails as the scheduler initializes with
`Could not load file or assembly 'Microsoft.Data.SqlClient'`. The package for each database is in
[Job Stores](tutorial/job-stores.md#configuring-a-persistent-store). No serializer needs naming:
System.Text.Json is the default.

To run the sample without a database, replace `UsePersistentStore(…)` with `UseInMemoryStore()` — no
driver package, no connection string. The cheapest persistent store to try next is
[a SQLite file](tutorial/job-stores.md#the-cheapest-persistent-store-to-try).

`ScheduleJob`, `AddJob` and `AddTrigger` cover the schedule for most applications;
[Lesson 1](tutorial/using-quartz.md) teaches them. A schedule that has to change without a rebuild can
come from a file, which needs the `Quartz.Plugins` package:

<!-- snippet: sample_quick_start_host_json_file -->
```csharp
// reads jobs and triggers from JSON; requires the Quartz.Plugins package
q.UseJsonSchedulingConfiguration(x =>
{
    x.Files.Add("~/quartz_jobs.json");
    x.FailOnSchedulingError = true;
});
```
<!-- endSnippet -->

`quartz_jobs.json`, scheduling one job every ten seconds:

```json
{
  "Schedule": {
    "Jobs": [
      {
        "Name": "helloJob",
        "JobType": "MyApp.HelloJob, MyApp",
        "Durable": true
      }
    ],
    "Triggers": [
      {
        "Name": "helloTrigger",
        "JobName": "helloJob",
        "Simple": {
          "RepeatCount": -1,
          "Interval": "00:00:10"
        }
      }
    ]
  }
}
```

[JSON Configuration](configuration/json.md) has the whole format: every trigger kind, the pre-processing
commands and the processing directives. `UseXmlSchedulingConfiguration` still reads XML files, but the
XML schema is [frozen](packages/quartz-plugins.md#xmlschedulingdataprocessorplugin), so write new
schedules in JSON.

### Without a host

Console applications and tests build a scheduler directly, with the same configuration API:

<!-- snippet: sample_quick_start_standalone -->
```csharp
IScheduler scheduler = await QuartzSchedulerBuilder
    .Create(q => q
        .ConfigureScheduler(options => options.InstanceName = "MyScheduler")
        .UseDefaultThreadPool(maxConcurrency: 5)
        .UseInMemoryStore())
    .BuildScheduler();

await scheduler.Start();
```
<!-- endSnippet -->

### From configuration files

Settings can come from `appsettings.json`, or anywhere else `IConfiguration` reads, under the same names:

```json
{
  "Quartz": {
    "Scheduler": { "InstanceName": "MyScheduler" },
    "ThreadPool": { "MaxConcurrency": 3 }
  }
}
```

`builder.AddQuartz(...)` reads that section by itself. On a bare `IServiceCollection`, pass it:

<!-- snippet: sample_quick_start_from_configuration -->
```csharp
services.AddQuartz(configuration.GetSection("Quartz"));
```
<!-- endSnippet -->

This configuration gives a scheduler that:

* is named "MyScheduler" (`Scheduler:InstanceName`);
* runs at most 3 jobs at once (`ThreadPool:MaxConcurrency`; the default is 10);
* keeps jobs, triggers and their state in memory, because no job store is configured.

Flat `quartz.*` keys from earlier versions are still accepted and mean the same thing — see the
[Quartz Configuration Reference](configuration/reference.md).

Get Quartz working with the in-memory store before adding a database.

::: tip
Every one of these settings is optional; the defaults work.
:::

## A first console application

This program builds a scheduler with the default configuration, starts it, and shuts it down:

**Program.cs**

<!-- The three whole-program listings on this page are hand-written rather than compiled from the
     samples project: they are top-level statements shown with their `using` directives, and a class
     library can host neither. Everything else here is a snippet. -->

```csharp
using Quartz;

// Build a scheduler with the default configuration
IScheduler scheduler = await QuartzSchedulerBuilder.Create().BuildScheduler();

// and start it off
await scheduler.Start();

// some sleep to show what's happening
await Task.Delay(TimeSpan.FromSeconds(10));

// and last shut down the scheduler when you are ready to close your program
await scheduler.Shutdown();
```

A running scheduler does not keep the process alive. The program ends when no code is left after
`scheduler.Shutdown()`; block explicitly, or use the host, to keep it running.

Run it now and nothing happens: ten seconds pass and the program ends. Add logging next.

## Adding logging

Quartz logs through `Microsoft.Extensions.Logging`. Under a host it uses the application's logging with
no setup. A console application without a container hands `LogProvider` a logger factory, before
building the scheduler, since that is when the loggers are created.

`AddSimpleConsole` is in its own package:

```shell
dotnet add package Microsoft.Extensions.Logging.Console
```

```csharp
using Microsoft.Extensions.Logging;
using Quartz.Diagnostics;

ILoggerFactory loggerFactory = LoggerFactory.Create(logging => logging
    .SetMinimumLevel(LogLevel.Debug)
    .AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    }));

LogProvider.SetLogProvider(loggerFactory);
```

## Trying out the application and adding jobs

Starting the application now logs:

```log
12:51:10 info: Quartz.Core.QuartzScheduler[0] Quartz Scheduler created
12:51:10 info: Quartz.Impl.RAMJobStore[0] RAMJobStore initialized.
12:51:10 info: Quartz.Impl.DefaultSchedulerFactory[0] Quartz Scheduler 4.0.0.0 - 'MyScheduler' with instanceId 'NON_CLUSTERED' initialized
12:51:10 info: Quartz.Impl.DefaultSchedulerFactory[0] Using thread pool 'Quartz.Impl.DefaultThreadPool', size: 10
12:51:10 info: Quartz.Impl.DefaultSchedulerFactory[0] Using job store 'Quartz.Impl.RAMJobStore', supports persistence: False, clustered: False
12:51:10 info: Quartz.Core.QuartzScheduler[0] Scheduler MyScheduler_$_NON_CLUSTERED started.
```

Using the `HelloJob` from [above](#in-an-application-with-a-host), add this just after `Start()`, before
the `Task.Delay`:

<!-- snippet: sample_quick_start_scheduling -->
```csharp
// define the job and tie it to our HelloJob class
IJobDetail job = JobBuilder.Create<HelloJob>()
    .WithIdentity("job1", "group1")
    .Build();

// Trigger the job to run now, and then repeat every 10 seconds forever
// (pass a repeat count as the second argument to stop after a while)
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger1", "group1")
    .StartNow()
    .WithSimpleSchedule(TimeSpan.FromSeconds(10))
    .Build();

// Tell Quartz to schedule the job using our trigger
await scheduler.ScheduleJob(job, trigger);

// several triggers for one job go together, in one call
// await scheduler.ScheduleJob(job, [trigger1, trigger2], ScheduleJobOptions.Replacing);
```
<!-- endSnippet -->

The complete console application:

```csharp
using Microsoft.Extensions.Logging;

using Quartz;
using Quartz.Diagnostics;

ILoggerFactory loggerFactory = LoggerFactory.Create(logging => logging
    .SetMinimumLevel(LogLevel.Debug)
    .AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    }));

LogProvider.SetLogProvider(loggerFactory);

// Build a scheduler with the default configuration
IScheduler scheduler = await QuartzSchedulerBuilder.Create().BuildScheduler();

await scheduler.Start();

IJobDetail job = JobBuilder.Create<HelloJob>()
    .WithIdentity("job1", "group1")
    .Build();

ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger1", "group1")
    .StartNow()
    .WithSimpleSchedule(x => x
        .WithInterval(TimeSpan.FromSeconds(10))
        .RepeatForever())
    .Build();

await scheduler.ScheduleJob(job, trigger);

// let it run for a while
await Task.Delay(TimeSpan.FromSeconds(60));

await scheduler.Shutdown();

public sealed class HelloJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        await Console.Out.WriteLineAsync("Greetings from HelloJob!");
    }
}
```

## Creating and initializing the database

SQL persistence, and features that depend on it such as clustering, need a database for Quartz. The
scheduler can create the tables and indexes, or you can.

**Let the store create them.** `ProvisionSchema()` runs the DDL for the configured database and creates
whatever is missing before the scheduler starts:

<!-- snippet: sample_quick_start_provision_schema -->
```csharp
q.UsePersistentStore(store =>
{
    store.UseSqlServer("my connection string");
    store.ProvisionSchema();
});
```
<!-- endSnippet -->

* It only creates; it never drops or alters. It is safe against a database that already has the
  tables, and with a second node starting at the same time.
* It is not an upgrade. A schema from an earlier Quartz version is moved forward by the migration
  scripts.
* It is off by default, because creating tables needs a permission production databases usually do not
  grant. Without that permission, startup fails and names the script to run.

**Run the script yourself** — what production usually wants. The DDL is in
[the Quartz.NET repository](https://github.com/quartznet/quartznet/tree/main/database/tables), one file
per database. Each script drops an existing Quartz schema before recreating it; its header says how to
turn that off. To upgrade a schema from an earlier version, see
[Database Schema Changes](../database/schema-changes.md). [Database Schema](db/) describes the tables,
and [Creating the schema](tutorial/job-stores.md#creating-the-schema) covers the setting and which
databases support it.

## Something to run

The repository has a console tour: thirteen small programs, each of which schedules something, starts a
scheduler and waits while it fires.

```shell
git clone https://github.com/quartznet/quartznet.git
cd quartznet
dotnet run --project src/Quartz.Examples
```

Pick one from the menu, or name it — `-- 5` runs the misfire example.
[The tour's readme](https://github.com/quartznet/quartznet/blob/main/src/Quartz.Examples/README.md) lists
what each one shows.

Continue with [the tutorial](tutorial/).
