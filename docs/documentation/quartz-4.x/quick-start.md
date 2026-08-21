---

title: Quartz 4 Quick Start
---

Welcome to the Quick Start Guide for Quartz.NET. As you read this guide, expect to see details of:

* Downloading Quartz.NET
* Installing Quartz.NET
* Configuring Quartz to your own particular needs
* Starting a sample application

## Download and Install

You can either download the zip file or use the NuGet package.
NuGet package contains only the binaries needed to run Quartz.NET, zip file comes with source code, samples and Quartz.NET server sample application.

## NuGet Package

Couldn't get any simpler than this. Just fire up Visual Studio (with NuGet installed) and add reference to package **Quartz** from package manager extension:

* Right-click on your project's References and choose **Manage NuGet Packages...**
* Choose **Online** category from the left
* Enter **Quartz** to the top right search and hit enter
* Choose **Quartz.NET** from search results and hit install
* Done!

or from NuGet Command-Line:

```shell
Install-Package Quartz
```

JSON serialization with `System.Text.Json` is part of the main package. If you want Newtonsoft.Json instead, add the [Quartz.Serialization.Newtonsoft](packages/json-serialization) package the same way.

### Zip Archive

**Short version**: Once you've downloaded Quartz.NET, unzip it, get the `Quartz.dll` from bin directory and start to use it.

Quartz core library does not have any hard binary dependencies. You can opt-in to more dependencies when you choose to use JSON serialization package, which requires JSON.NET.
You need to have at least `Quartz.dll` beside your app binaries to successfully run Quartz.NET. So just add it as a references to your Visual Studio project that uses them.
You can find these dlls from extracted archive from path **bin\your-target-framework-version\release\Quartz**.

## Configuration

Quartz is configured with strongly typed options. An option has the same name in code and in
configuration files, so there is one vocabulary to learn.

### In an application with a host

Most applications register Quartz into their service collection:

```csharp
builder.Services.AddQuartz(q =>
{
    q.ConfigureScheduler(options => options.InstanceName = "MyScheduler");

    // default max concurrency is 10
    q.UseDefaultThreadPool(maxConcurrency: 5);

    q.UsePersistentStore(store =>
    {
        // there are other databases supported too
        store.UseSqlServer("my connection string");
        store.UseClustering();

        // this requires the Quartz.Serialization.Newtonsoft package;
        // UseSystemTextJsonSerializer() is built in
        store.UseNewtonsoftJsonSerializer();

        store.Configure(options =>
        {
            // store job data as strings, which avoids surprises when a serialized
            // type changes shape later
            options.UseProperties = true;
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

builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```

The hosted service starts the scheduler with the application and shuts it down with it.

### Without a host

Console applications and tests build a scheduler directly. The configuration API is the same:

```csharp
var builder = QuartzSchedulerBuilder.Create();
builder.ConfigureScheduler(options => options.InstanceName = "MyScheduler")
    .UseDefaultThreadPool(maxConcurrency: 5)
    .UseInMemoryStore();

IScheduler scheduler = await builder.BuildScheduler();

await scheduler.Start();
```

The builder is held in a variable rather than configured and built in one expression: its configuration
methods are the same ones `AddQuartz` hands out, so they return that interface rather than the builder.
`WebApplicationBuilder` is used the same way.

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

```csharp
builder.Services.AddQuartz(builder.Configuration.GetSection("Quartz"));
```

Flat `quartz.*` keys from earlier versions are still accepted and mean the same thing. Full details are
in the [Quartz Configuration Reference](configuration/reference).

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

## Starting a Sample Application

Now you've downloaded and installed Quartz, it's time to get a sample application up and running. The following code obtains an instance of the scheduler, starts it, then shuts it down:

**Program.cs**

```csharp
using System;
using System.Threading.Tasks;

using Quartz;

namespace QuartzSampleApp
{
    public class Program
    {
        private static async Task Main(string[] args)
        {
            // Build a scheduler with the default configuration
            IScheduler scheduler = await QuartzSchedulerBuilder.Create().BuildScheduler();

            // and start it off
            await scheduler.Start();

            // some sleep to show what's happening
            await Task.Delay(TimeSpan.FromSeconds(10));

            // and last shut down the scheduler when you are ready to close your program
            await scheduler.Shutdown();
        }
    }
}
```

As of Quartz 3.0 your application will terminate when there's no code left to execute after `scheduler.Shutdown()`, because there won't be any active threads. You should manually block exiting of application if you want scheduler to keep running also after the Task.Delay and Shutdown has been processed.

Now running the program will not show anything. When 10 seconds have passed the program will just terminate. Lets add some logging to console.

## Adding logging

When no logging is configured, Quartz will log to a NullLogger, essentially causing logging to be silent.
Quartz.net supports logging providers that are compatible with the Microsoft.Logging.Abstractions library.

To configure a console logger, using the Microsoft.Logging library, construct a LoggerFactory, then set this is Quartz via the `LogProvider.SetLogProvider` method.

```csharp
var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.TimestampFormat = "hh:mm:ss ";
            });
    });
    LogProvider.SetLogProvider(loggerFactory);
```

## Trying out the application and adding jobs

Now we should get a lot more information when we start the application.

```log
[12.51.10] [Info] Initialized Scheduler Signaller of type: Quartz.Core.SchedulerSignalerImpl
[12.51.10] [Info] Quartz Scheduler created
[12.51.10] [Info] RAMJobStore initialized.
[12.51.10] [Info] Scheduler meta-data: Quartz Scheduler (v3.0.0.0) 'MyScheduler' with instanceId 'NON_CLUSTERED'
  Scheduler class: 'Quartz.Core.QuartzScheduler' - running locally.
  NOT STARTED.
  Currently in standby mode.
  Number of jobs executed: 0
  Using thread pool 'Quartz.Impl.DefaultThreadPool' - with 3 threads.
  Using job-store 'Quartz.Impl.RAMJobStore' - which does not support persistence. and is not clustered.

[12.51.10] [Info] Quartz scheduler 'MyScheduler' initialized
[12.51.10] [Info] Quartz scheduler version: 3.0.0.0
[12.51.10] [Info] Scheduler MyScheduler_$_NON_CLUSTERED started.
```

We need a simple test job to test the functionality, lets create HelloJob that outputs greetings to console.

```csharp
public class HelloJob : IJob
{
 public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
 {
  await Console.Out.WriteLineAsync("Greetings from HelloJob!");
 }
}
```

To do something interesting, you need code just after Start() method, before the Task.Delay.

```csharp
// define the job and tie it to our HelloJob class
IJobDetail job = JobBuilder.Create<HelloJob>()
 .WithIdentity("job1", "group1")
 .Build();

// Trigger the job to run now, and then repeat every 10 seconds
ITrigger trigger = TriggerBuilder.Create()
 .WithIdentity("trigger1", "group1")
 .StartNow()
 .WithSimpleSchedule(x => x
  .WithInterval(TimeSpan.FromSeconds(10))
  .RepeatForever())
 .Build();

// Tell Quartz to schedule the job using our trigger
await scheduler.ScheduleJob(job, trigger);

// You could also schedule multiple triggers for the same job with
// await scheduler.ScheduleJob(job, new List<ITrigger>() { trigger1, trigger2 }, replace: true);
```

The complete console application will now look like this

```csharp
using System;
using System.Threading.Tasks;

using Quartz;
using Quartz.Diagnostics;

namespace QuartzSampleApp
{
    public class Program
    {
        private static async Task Main(string[] args)
        {
        var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddSimpleConsole(options =>
                    {
                        options.IncludeScopes = true;
                        options.SingleLine = true;
                        options.TimestampFormat = "hh:mm:ss ";
                    });
            });
            LogProvider.SetLogProvider(loggerFactory);
            // Build a scheduler with the default configuration
            IScheduler scheduler = await QuartzSchedulerBuilder.Create().BuildScheduler();

            // and start it off
            await scheduler.Start();

            // define the job and tie it to our HelloJob class
            IJobDetail job = JobBuilder.Create<HelloJob>()
                .WithIdentity("job1", "group1")
                .Build();

            // Trigger the job to run now, and then repeat every 10 seconds
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger1", "group1")
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithInterval(TimeSpan.FromSeconds(10))
                    .RepeatForever())
                .Build();

            // Tell Quartz to schedule the job using our trigger
            await scheduler.ScheduleJob(job, trigger);

            // some sleep to show what's happening
            await Task.Delay(TimeSpan.FromSeconds(60));

            // and last shut down the scheduler when you are ready to close your program
            await scheduler.Shutdown();

            Console.WriteLine("Press any key to close the application");
            Console.ReadKey();
        }
    }

    public class HelloJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            await Console.Out.WriteLineAsync("Greetings from HelloJob!");
        }
    }
}
```

## Creating and initializing database

In order to use SQL persistence storage for Quartz and enabling features like clustering, you need to create a database and initialize the schema objects using SQL scripts.
First you need to create a database and credentials for Quartz. After you have a database that Quartz will be able to connect to, you also need to create database tables and indexes
that Quartz needs for successful operation.

You can find latest DDL scripts in [Quartz's GitHub repository](https://github.com/quartznet/quartznet/tree/main/database/tables) and they are also contained in the ZIP archive distribution.
There are also thirty party additions to Quartz that enable other types of storage, like NoSQL databases. You can search for them on NuGet.

Now go have some fun exploring Quartz.NET! You can continue by reading [the tutorial](tutorial/index.html).
