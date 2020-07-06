---
layout: default
title: Quartz.NET Quick Start Guide
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

	Install-Package Quartz

If you want to add JSON Serialization, just add the **Quartz.Serialization.Json** package the same way.


### Zip Archive

**Short version**: Once you've downloaded Quartz.NET, unzip it somewhere, grab the Quartz.dll from bin directory and start to use it.

Quartz core library does not have any hard binary dependencies. You can opt-in to more dependencies when you choose to use JSON serialization package, which requires JSON.NET.
You need to have at least Quartz.dll beside your app binaries to successfully run Quartz.NET. So just add it as a references to your Visual Studio project that uses them.
You can find these dlls from extracted archive from path **bin\your-target-framework-version\release\Quartz**.

## Configuration

This is the big bit! Quartz.NET is a very configurable library. There are three ways (which are not mutually exclusive) to supply Quartz.NET configuration information:

* Programmatically via providing NameValueCollection parameter to scheduler factory
* Via standard youapp.exe.config configuration file using quartz-element (full .NET framework only)
* quartz.config file in your application's root directory (works both with .NET Core and full .NET Framework)

You can find samples of all these alternatives in the Quartz.NET zip file.

Full documentation of available properties is available in the [Quartz Configuration Reference](configuration/index.html).

To get up and running quickly, a basic quartz.config looks something like this:

	quartz.scheduler.instanceName = MyScheduler
	quartz.jobStore.type = Quartz.Simpl.RAMJobStore, Quartz
    quartz.threadPool.threadCount = 3

Remember to set the **Copy to Output Directory** on Visual Studio's file property pages to have value **Copy always**. Otherwise the config will not be seen if it's not in build directory.
	
The scheduler created by this configuration has the following characteristics:

* quartz.scheduler.instanceName - This scheduler's name will be "MyScheduler".
* quartz.threadPool.threadCount - Maximum of 3 jobs can be run simultaneously.
* quartz.jobStore.type - All of Quartz's data, such as details of jobs and triggers, is held in memory (rather than in a database). 
Even if you have a database and want to use it with Quartz, I suggest you get Quartz working with the RamJobStore before you open up a whole new dimension by working with a database.

*Actually you don't need to define these properties if you don't want to, Quartz.NET comes with sane defaults*

## Starting a Sample Application

Now you've downloaded and installed Quartz, it's time to get a sample application up and running. The following code obtains an instance of the scheduler, starts it, then shuts it down:

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
        private static void Main(string[] args)
        {
            // trigger async evaluation
            RunProgram().GetAwaiter().GetResult();
        }

        private static async Task RunProgram()
        {
            try
            {
                // Grab the Scheduler instance from the Factory
                NameValueCollection props = new NameValueCollection
                {
                    { "quartz.serializer.type", "binary" }
                };
                StdSchedulerFactory factory = new StdSchedulerFactory(props);
                IScheduler scheduler = await factory.GetScheduler();

                // and start it off
                await scheduler.Start();

                // some sleep to show what's happening
                await Task.Delay(TimeSpan.FromSeconds(60));

                // and last shut down the scheduler when you are ready to close your program
                await scheduler.Shutdown();
            }
            catch (SchedulerException se)
            {
                await Console.Error.WriteLineAsync(se.ToString());
            }
        }
    }
}
```

As of Quartz 3.0 your application will terminate when there's no code left to execute after scheduler.Shutdown(), because there won't be any active threads. You should manually block exiting of application if you want scheduler to keep running also after the Task.Delay and Shutdown has been processed.

Now running the program will not show anything. When 10 seconds have passed the program will just terminate. Lets add some logging to console.

## Adding logging

[LibLog](https://github.com/damianh/LibLog/wiki) can be configured to use different logging frameworks under the hood; namely Log4Net, NLog and Serilog.

When LibLog does not detect any other logging framework to be present, it will be silent. We can configure a custom logger provider that just logs to console show the output
if you don't have logging framework setup ready yet.


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

    public IDisposable OpenMappedContext(string key, string value)
    {
        throw new NotImplementedException();
    }
}
```

## Trying out the application and adding jobs

Now we should get a lot more information when we start the application.

```
[12.51.10] [Info] Quartz.NET properties loaded from configuration file 'C:\QuartzSampleApp\quartz.config'
[12.51.10] [Info] Initialized Scheduler Signaller of type: Quartz.Core.SchedulerSignalerImpl
[12.51.10] [Info] Quartz Scheduler v.0.0.0.0 created.
[12.51.10] [Info] RAMJobStore initialized.
[12.51.10] [Info] Scheduler meta-data: Quartz Scheduler (v0.0.0.0) 'MyScheduler' with instanceId 'NON_CLUSTERED'
  Scheduler class: 'Quartz.Core.QuartzScheduler' - running locally.
  NOT STARTED.
  Currently in standby mode.
  Number of jobs executed: 0
  Using thread pool 'Quartz.Simpl.DefaultThreadPool' - with 3 threads.
  Using job-store 'Quartz.Simpl.RAMJobStore' - which does not support persistence. and is not clustered.

[12.51.10] [Info] Quartz scheduler 'MyScheduler' initialized
[12.51.10] [Info] Quartz scheduler version: 0.0.0.0
[12.51.10] [Info] Scheduler MyScheduler_$_NON_CLUSTERED started.
```

We need a simple test job to test the functionality, lets create HelloJob that outputs greetings to console.

```csharp
public class HelloJob : IJob
{
	public async Task Execute(IJobExecutionContext context)
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
		.WithIntervalInSeconds(10)
		.RepeatForever())
	.Build();

// Tell quartz to schedule the job using our trigger
await scheduler.ScheduleJob(job, trigger);
```

The complete console application will now look like this

```csharp
using System;
using System.Threading.Tasks;

using Quartz;
using Quartz.Impl;
using Quartz.Logging;

namespace QuartzSampleApp
{
    public class Program
    {
        private static void Main(string[] args)
        {
            LogProvider.SetCurrentLogProvider(new ConsoleLogProvider());

            RunProgramRunExample().GetAwaiter().GetResult();

            Console.WriteLine("Press any key to close the application");
            Console.ReadKey();
        }

        private static async Task RunProgramRunExample()
        {
            try
            {
                // Grab the Scheduler instance from the Factory
                NameValueCollection props = new NameValueCollection
                {
                    { "quartz.serializer.type", "binary" }
                };
                StdSchedulerFactory factory = new StdSchedulerFactory(props);
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

                // Tell quartz to schedule the job using our trigger
                await scheduler.ScheduleJob(job, trigger);

                // some sleep to show what's happening
                await Task.Delay(TimeSpan.FromSeconds(60));

                // and last shut down the scheduler when you are ready to close your program
                await scheduler.Shutdown();
            }
            catch (SchedulerException se)
            {
                Console.WriteLine(se);
            }
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

            public IDisposable OpenMappedContext(string key, string value)
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

Now go have some fun exploring Quartz.NET! You can continue by reading [the tutorial](tutorial/index.html).
