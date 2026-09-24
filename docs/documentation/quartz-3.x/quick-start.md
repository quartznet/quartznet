---

title: Quartz 3 Quick Start
---

## Download and Install

Use the NuGet package or the zip file. The NuGet package has only the binaries needed to run Quartz.NET. The zip file adds source code, samples and the Quartz.NET server sample application.

## NuGet Package

In Visual Studio (with NuGet installed), add the **Quartz** package:

1. Right-click your project's References and choose **Manage NuGet Packages...**
2. Choose the **Online** category on the left.
3. Enter **Quartz** in the search box at the top right and press enter.
4. Choose **Quartz.NET** from the results and install it.

or from the NuGet command line:

```shell
Install-Package Quartz
```

For JSON serialization, add the [Quartz.Serialization.SystemTextJson](packages/system-text-json) or [Quartz.Serialization.Json](packages/json-serialization) package the same way.

### Zip Archive

Unzip the download, take `Quartz.dll` from the bin directory and reference it from your Visual Studio project. It is in the extracted archive under **bin\your-target-framework-version\release\Quartz**.

The Quartz core library has no hard binary dependencies; the JSON serialization packages add some. `Quartz.dll` beside your app binaries is the minimum to run Quartz.NET.

## Configuration

There are two ways to configure Quartz.NET, and you can combine them.

### Fluent Scheduler Builder API

Configure the scheduler with the C# fluent API, or pass the scheduler factory a `NameValueCollection` of configuration keys and values.

```csharp
// you can have base properties
var properties = new NameValueCollection();

// and override values via builder
IScheduler scheduler = await SchedulerBuilder.Create(properties)
    // default max concurrency is 10
    .UseDefaultThreadPool(x => x.MaxConcurrency = 5)
    // this is the default
    // .WithMisfireThreshold(TimeSpan.FromSeconds(60))
    .UsePersistentStore(x =>
    {
        // force job data map values to be considered as strings
        // prevents nasty surprises if object is accidentally serialized and then
        // serialization format breaks, defaults to false
        x.UseProperties = true;
        x.UseClustering();
        // there are other SQL providers supported too
        x.UseSqlServer("my connection string");
        // this requires Quartz.Serialization.SystemTextJson NuGet package
        x.UseSystemTextJsonSerializer();
    })
    // job initialization plugin handles our xml reading, without it defaults are used
    // requires Quartz.Plugins NuGet package
    .UseXmlSchedulingConfiguration(x =>
    {
        x.Files = new[] { "~/quartz_jobs.xml" };
        // this is the default
        x.FailOnFileNotFound = true;
        // this is not the default
        x.FailOnSchedulingError = true;
    })
    .BuildScheduler();

await scheduler.Start();
```

### Configuration files

Quartz reads known configuration properties from:

* `YourApplication.exe.config` configuration file using quartz-element (full .NET framework only)
* `appsettings.json` (.NET Core/NET5 onwards)
* `quartz.config` file in your application's root directory (works both with .NET Core and full .NET Framework)

All properties are in the [Quartz Configuration Reference](configuration/reference).

A basic quartz.config:

```text
 quartz.scheduler.instanceName = MyScheduler
 quartz.jobStore.type = Quartz.Simpl.RAMJobStore, Quartz
 quartz.threadPool.maxConcurrency = 3
```

Set **Copy to Output Directory** to **Copy always** in the file's Visual Studio properties; a config file outside the build directory is not found.

This configuration gives a scheduler with:

* `quartz.scheduler.instanceName`: the name "MyScheduler".
* `quartz.threadPool.maxConcurrency`: at most 3 jobs running at once (default is 10).
* `quartz.jobStore.type`: all of Quartz's data, such as jobs and triggers, held in memory instead of a database.

Even if you plan to use a database, get Quartz working with the RamJobStore first.

::: tip
These properties are optional; Quartz.NET has sensible defaults.
:::

## Starting a Sample Application

This code gets a scheduler instance, starts it, then shuts it down:

**Program.cs**

```csharp
using System;
using System.Threading.Tasks;

using Quartz;
using Quartz.Impl;

namespace QuartzSampleApp
{
    public class Program
    {
        private static async Task Main(string[] args)
        {
            // Grab the Scheduler instance from the Factory
            StdSchedulerFactory factory = new StdSchedulerFactory();
            IScheduler scheduler = await factory.GetScheduler();

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

Since Quartz 3.0 the application exits when no code is left to run after `scheduler.Shutdown()`, because no threads are active. To keep the scheduler running past the Task.Delay and Shutdown, block the application from exiting yourself.

The program shows nothing yet and exits after 10 seconds. Add logging to the console next.

## Adding logging

[LibLog](https://github.com/damianh/LibLog/wiki) can use Log4Net, NLog or Serilog. When it detects no logging framework, it is silent. Until you set one up, a custom log provider can write to the console:

```csharp
LogProvider.SetCurrentLogProvider(new ConsoleLogProvider());

private class ConsoleLogProvider : ILogProvider
{
    public Logger GetLogger(string name)
    {
        return (level, func, exception, parameters) =>
        {
            if (level >= LogLevel.Info && func != null)
            {
                Console.WriteLine("[" + DateTime.Now.ToLongTimeString() + "] [" + level + "] " + func(), parameters);
            }
            return true;
        };
    }

    public IDisposable OpenNestedContext(string message)
    {
        throw new NotImplementedException();
    }

    public IDisposable OpenMappedContext(string key, object value, bool destructure = false)
    {
        throw new NotImplementedException();
    }
}
```

## Trying out the application

The application now logs at start-up:

```log
[12.51.10] [Info] Quartz.NET properties loaded from configuration file 'C:\QuartzSampleApp\quartz.config'
[12.51.10] [Info] Initialized Scheduler Signaller of type: Quartz.Core.SchedulerSignalerImpl
[12.51.10] [Info] Quartz Scheduler created
[12.51.10] [Info] RAMJobStore initialized.
[12.51.10] [Info] Scheduler meta-data: Quartz Scheduler (v3.0.0.0) 'MyScheduler' with instanceId 'NON_CLUSTERED'
  Scheduler class: 'Quartz.Core.QuartzScheduler' - running locally.
  NOT STARTED.
  Currently in standby mode.
  Number of jobs executed: 0
  Using thread pool 'Quartz.Simpl.DefaultThreadPool' - with 3 threads.
  Using job-store 'Quartz.Simpl.RAMJobStore' - which does not support persistence. and is not clustered.

[12.51.10] [Info] Quartz scheduler 'MyScheduler' initialized
[12.51.10] [Info] Quartz scheduler version: 3.0.0.0
[12.51.10] [Info] Scheduler MyScheduler_$_NON_CLUSTERED started.
```

A test job that writes a greeting to the console:

```csharp
public class HelloJob : IJob
{
 public async Task Execute(IJobExecutionContext context)
 {
  await Console.Out.WriteLineAsync("Greetings from HelloJob!");
 }
}
```

Schedule it after `Start()`, before the `Task.Delay`:

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
  .WithIntervalInSeconds(10)
  .RepeatForever())
 .Build();

// Tell Quartz to schedule the job using our trigger
await scheduler.ScheduleJob(job, trigger);

// You could also schedule multiple triggers for the same job with
// await scheduler.ScheduleJob(job, new List<ITrigger>() { trigger1, trigger2 }, replace: true);
```

The complete console application:

```csharp
using System;
using System.Threading.Tasks;

using Quartz;
using Quartz.Impl;
using Quartz.Diagnostics;

namespace QuartzSampleApp
{
    public class Program
    {
        private static async Task Main(string[] args)
        {
            LogProvider.SetCurrentLogProvider(new ConsoleLogProvider());

            // Grab the Scheduler instance from the Factory
            StdSchedulerFactory factory = new StdSchedulerFactory();
            IScheduler scheduler = await factory.GetScheduler();

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
                    .WithIntervalInSeconds(10)
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

        // simple log provider to get something to the console
        private class ConsoleLogProvider : ILogProvider
        {
            public Logger GetLogger(string name)
            {
                return (level, func, exception, parameters) =>
                {
                    if (level >= LogLevel.Info && func != null)
                    {
                        Console.WriteLine("[" + DateTime.Now.ToLongTimeString() + "] [" + level + "] " + func(), parameters);
                    }
                    return true;
                };
            }

            public IDisposable OpenNestedContext(string message)
            {
                throw new NotImplementedException();
            }

            public IDisposable OpenMappedContext(string key, object value, bool destructure = false)
            {
                throw new NotImplementedException();
            }
        }
    }

    public class HelloJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            await Console.Out.WriteLineAsync("Greetings from HelloJob!");
        }
    }
}
```

## Creating and initializing database

SQL persistence, and features such as clustering, need a database with the Quartz schema:

1. Create a database and credentials for Quartz.
2. Create the tables and indexes with the DDL scripts from [Quartz's GitHub repository](https://github.com/quartznet/quartznet/tree/main/database/tables). The ZIP archive distribution contains them too.

Third-party additions to Quartz support other storage, such as NoSQL databases; search for them on NuGet.

Continue with [the tutorial](tutorial/index.html).
