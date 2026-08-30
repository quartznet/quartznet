---

title: Quartz 4 Quick Start
---

Welcome to the Quick Start Guide for Quartz.NET. As you read this guide, expect to see details of:

* Installing Quartz.NET
* Configuring Quartz to your own particular needs
* Running a first job, in a console application and under a host

## Install

```shell
dotnet add package Quartz
```

That is everything a scheduler needs. Dependency injection, hosting, the scheduler
[health check](packages/hosted-services-integration.md#health-checks) and System.Text.Json serialization
are part of the core package — 3.x shipped them as `Quartz.Extensions.DependencyInjection`,
`Quartz.Extensions.Hosting`, `Quartz.AspNetCore` and `Quartz.Serialization.Json`.

The optional packages, added the same way when you want them:

| Package | For |
|---|---|
| [Quartz.Serialization.Newtonsoft](packages/json-serialization.md) | persisting with Newtonsoft.Json instead of System.Text.Json |
| [Quartz.Jobs](packages/quartz-jobs.md) | the ready-made jobs — file scanning, sending mail, running a process |
| [Quartz.Plugins](packages/quartz-plugins.md) | history logging, XML/JSON schedule files, the interrupt monitor |
| [Quartz.AspNetCore](packages/aspnet-core-integration.md) | the HTTP API |
| [Quartz.Dashboard](packages/dashboard.md) | the web dashboard |

## Configuration

Quartz is configured with strongly typed options. An option has the same name in code and in
configuration files, so there is one vocabulary to learn.

### In an application with a host

Most applications register Quartz into their service collection:

<!-- snippet: sample_quick_start_host -->
```csharp
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

        // System.Text.Json is built in; the Newtonsoft one is a package away
        store.UseSystemTextJsonSerializer();

        store.ConfigureStore(options =>
        {
            // store job data as strings, which avoids surprises when a serialized
            // type changes shape later
            options.StoreJobDataAsStrings = true;
        });
    });

    // reads jobs and triggers from XML; requires the Quartz.Plugins package
    q.UseXmlSchedulingConfiguration(x =>
    {
        x.Files.Add("~/quartz_jobs.xml");
        x.FailOnFileNotFound = true;
        x.FailOnSchedulingError = true;
    });
});

builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

The hosted service starts the scheduler with the application and shuts it down with it.

### Without a host

Console applications and tests build a scheduler directly. The configuration API is the same, and the
whole chain is one expression:

<!-- snippet: sample_quick_start_standalone -->
```csharp
IScheduler scheduler = await QuartzSchedulerBuilder.Create()
    .ConfigureScheduler(options => options.InstanceName = "MyScheduler")
    .UseDefaultThreadPool(maxConcurrency: 5)
    .UseInMemoryStore()
    .BuildScheduler();

await scheduler.Start();
```
<!-- endSnippet -->

### From configuration files

Settings can come from `appsettings.json`, or anywhere else `IConfiguration` reads from, using the
same names:

```json
{
  "Quartz": {
    "Scheduler": { "InstanceName": "MyScheduler" },
    "ThreadPool": { "MaxConcurrency": 3 }
  }
}
```

`builder.AddQuartz(...)` reads that section by itself. On a bare `IServiceCollection`, name it:

<!-- snippet: sample_quick_start_from_configuration -->
```csharp
services.AddQuartz(configuration.GetSection("Quartz"));
```
<!-- endSnippet -->

Flat `quartz.*` keys from earlier versions are still accepted and mean the same thing. Full details are
in the [Quartz Configuration Reference](configuration/reference.md).

The scheduler created by this configuration has the following characteristics:

* `Scheduler:InstanceName` - This scheduler's name will be "MyScheduler".
* `ThreadPool:MaxConcurrency` - Maximum of 3 jobs can be run simultaneously (default is 10).
* No job store is configured, so Quartz's data — jobs, triggers and their state — is held in memory
  rather than in a database.

Even if you intend to use a database, it is worth getting Quartz working with the in-memory store
first, before adding a second thing that can go wrong.

::: tip
Actually you don't need to define these properties if you don't want to, Quartz.NET comes with sane defaults
:::

## A first console application

The following program builds a scheduler with the default configuration, starts it, and shuts it down:

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

Your application terminates once there is no code left to execute after `scheduler.Shutdown()`: a running
scheduler does not keep the process alive on its own. Block explicitly — or use the host, which does the
blocking for you — if the scheduler should keep running.

Run it now and nothing happens: ten seconds pass and the program ends. Let us add some logging.

## Adding logging

Quartz logs through `Microsoft.Extensions.Logging`. Under a host it uses whatever the application already
configured, and there is nothing to do. A console application like this one has no container of its own
to configure, so it tells Quartz where to log by handing `LogProvider` a logger factory — before building
the scheduler, since that is when the loggers are created:

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

Now starting the application says considerably more:

```log
12:51:10 info: Quartz.Core.QuartzScheduler[0] Quartz Scheduler created
12:51:10 info: Quartz.Impl.RAMJobStore[0] RAMJobStore initialized.
12:51:10 info: Quartz.Impl.DefaultSchedulerFactory[0] Quartz Scheduler 4.0.0.0 - 'MyScheduler' with instanceId 'NON_CLUSTERED' initialized
12:51:10 info: Quartz.Impl.DefaultSchedulerFactory[0] Using thread pool 'Quartz.Impl.DefaultThreadPool', size: 10
12:51:10 info: Quartz.Impl.DefaultSchedulerFactory[0] Using job store 'Quartz.Impl.RAMJobStore', supports persistence: False, clustered: False
12:51:10 info: Quartz.Core.QuartzScheduler[0] Scheduler MyScheduler_$_NON_CLUSTERED started.
```

We need a simple test job to try the scheduler out; let's create a HelloJob that greets the console.

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

To do something interesting, add code just after `Start()`, before the `Task.Delay`:

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

The complete console application now looks like this:

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

To use SQL persistence, and features such as clustering that depend on it, create a database for Quartz.
Its tables and indexes can then either be created by the scheduler or created by you.

The quickest way to a working database is to let the store do it. `ProvisionSchema()` runs the DDL for
whichever database you named, creating whatever is missing before the scheduler starts:

<!-- snippet: sample_quick_start_provision_schema -->
```csharp
q.UsePersistentStore(store =>
{
    store.UseSqlServer("my connection string");
    store.ProvisionSchema();
});
```
<!-- endSnippet -->

It only ever creates: nothing is dropped and nothing is altered, so it is safe against a database that
already has the tables, and a second node starting at the same time is fine. It is equally **not** an
upgrade — a schema built by an earlier Quartz version is moved forward by the migration scripts, not by
this. It is off by default because creating tables needs a permission a production database is usually
right not to grant; when the account has none, startup fails naming the script to run instead.

Running that script yourself is the other way, and the one production usually wants. The DDL is in
[the Quartz.NET repository](https://github.com/quartznet/quartznet/tree/main/database/tables), one file per
database. Each one drops an existing Quartz schema before it recreates it; the header of every script
says how to turn that off. Upgrading a schema created by an earlier version is a different script — see
[Database Schema Changes](../database/schema-changes.md). What the tables hold, and why one route is
automatic and the other is not, is described in [Database Schema](db/); the setting behind the first,
and which databases can use it, is in [Creating the schema](tutorial/job-stores.md#creating-the-schema).

## Something to run

The repository carries a console tour: thirteen small programs, each of which schedules something,
starts a scheduler and then waits while it fires, so the thing being taught happens in front of you.

```shell
git clone https://github.com/quartznet/quartznet.git
cd quartznet
dotnet run --project src/Quartz.Examples
```

Pick one from the menu, or name it — `-- 5` runs the misfire example. What each one shows is listed in
[the tour's readme](https://github.com/quartznet/quartznet/blob/main/src/Quartz.Examples/README.md).

Now go have some fun exploring Quartz.NET. Continue with [the tutorial](tutorial/).
