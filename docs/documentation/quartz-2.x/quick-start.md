---

title: Quartz 2 Quick Start
---

Welcome to the Quick Start Guide for Quartz.NET. As you read this guide, expect to see details of:

* Downloading Quartz.NET
* Installing Quartz.NET
* Configuring Quartz to your own particular needs
* Starting a sample application

## Download and Install

You can either download the zip file or use the NuGet package. NuGet package contains only the binaries needed to run Quartz.NET, zip file comes with source code, samples and Quartz.NET server sample application.

### Zip Archive

**Short version**: Once you've downloaded Quartz.NET, unzip it somewhere, grab the Quartz.dll and Common.Logging.dll from bin directory and start to use them.

Quartz depends only on single third-party library called Common.Logging (which contains logging abstractions that allow you to use the logging provider that suites you the best).
You need to have Quartz.dll and Commong.Logging.dll beside your app binaries to successfully run Quartz.NET. So just add them as references to your Visual Studio project that uses them.
You can find these dlls from extracted archive from path **bin\your-target-framework-version\release\Quartz**.

## NuGet Package

Couldn't get any simpler than this. Just fire up Visual Studio (with NuGet installed) and add reference to package **Quartz** from package manager extension:

* Right-click on your project's References and choose **Manage NuGet Packages...**
* Choose **Online** category from the left
* Enter **Quartz** to the top right search and hit enter
* Choose **Quartz.NET** from search results and hit install
* Done!

or from NuGet Command-Line:

```powershell
Install-Package Quartz
```

## Configuration

This is the big bit! Quartz.NET is a very configurable library. There are three ways (which are not mutually exclusive) to supply Quartz.NET configuration information:

* Programmatically via providing NameValueCollection parameter to scheduler factory
* Via standard youapp.exe.config configuration file using quartz-element
* quartz.config file in your application's root directory

You can find samples of all these alternatives in the Quartz.NET zip file.

Full documentation of available properties is available in the [Quartz Configuration Reference](configuration/index.html).

To get up and running quickly, a basic quartz.config looks something like this:

 quartz.scheduler.instanceName = MyScheduler
 quartz.threadPool.threadCount = 3
 quartz.jobStore.type = Quartz.Simpl.RAMJobStore, Quartz

Remember to set the **Copy to Output Directory** on Visual Studio's file property pages to have value **Copy always**. Otherwise the config will not be seen if it's not in build directory.

The scheduler created by this configuration has the following characteristics:

* quartz.scheduler.instanceName - This scheduler's name will be "MyScheduler".
* quartz.threadPool.threadCount - There are 3 threads in the thread pool, which means that a maximum of 3 jobs can be run simultaneously.
* quartz.jobStore.type - All of Quartz's data, such as details of jobs and triggers, is held in memory (rather than in a database).
Even if you have a database and want to use it with Quartz, I suggest you get Quartz working with the RamJobStore before you open up a whole new dimension by working with a database.

*Actually you don't need to define these properties if you don't want to, Quartz.NET comes with sane defaults*

## Starting a Sample Application

Now you've downloaded and installed Quartz, it's time to get a sample application up and running. The following code obtains an instance of the scheduler, starts it, then shuts it down:

**Program.cs**

```csharp
using System;
using System.Threading;

using Quartz;
using Quartz.Impl;

namespace QuartzSampleApplication
{
    public class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                // Grab the Scheduler instance from the Factory 
                IScheduler scheduler = StdSchedulerFactory.GetDefaultScheduler();

                // and start it off
                scheduler.Start();

                // some sleep to show what's happening
                Thread.Sleep(TimeSpan.FromSeconds(60));

                // and last shut down the scheduler when you are ready to close your program
                scheduler.Shutdown();
            }
            catch (SchedulerException se)
            {
                Console.WriteLine(se);
            }
        }
    }
}
```

Once you obtain a scheduler using StdSchedulerFactory.GetDefaultScheduler(), your application will not terminate by default until you call scheduler.Shutdown(), because there will be active threads (non-daemon threads).

Now running the program will not show anything. When 10 seconds have passed the program will just terminate. Lets add some logging to console.

## Adding logging

[Common.Logging](http://netcommon.sourceforge.net/) can be configured to use different logging frameworks under the hood; namely Enterprise Library, Log4Net and NLog.

However, to keep things simple for our example we take the simple route and configure logging using code to just log to the console using Common.Logging basic logging mechanism.

Add the following line to the beginning of your Program.cs

```csharp
Common.Logging.LogManager.Adapter = new Common.Logging.Simple.ConsoleOutLoggerFactoryAdapter { Level = Common.Logging.LogLevel.Info};
```

## Trying out the application and adding jobs

Now we should get a lot more information when we start the application.

```text
11.1.2014 14:52:04 [INFO]  Quartz.Impl.StdSchedulerFactory - Quartz.NET properties loaded from configuration file 'c:\ConsoleApplication1\bin\Debug\quartz.config'
11.1.2014 14:52:04 [INFO]  Quartz.Impl.StdSchedulerFactory - Using default implementation for object serializer
11.1.2014 14:52:04 [INFO]  Quartz.Impl.StdSchedulerFactory - Using default implementation for ThreadExecutor
11.1.2014 14:52:04 [INFO]  Quartz.Core.SchedulerSignalerImpl - Initialized Scheduler Signaller of type: Quartz.Core.SchedulerSignalerImpl
11.1.2014 14:52:04 [INFO]  Quartz.Core.QuartzScheduler - Quartz Scheduler v.2.2.1.400 created.
11.1.2014 14:52:04 [INFO]  Quartz.Simpl.RAMJobStore - RAMJobStore initialized.
11.1.2014 14:52:04 [INFO]  Quartz.Core.QuartzScheduler - Scheduler meta-data: Quartz Scheduler (v2.2.1.400) 'MyScheduler' with instanceId 'NON_CLUSTERED'
  Scheduler class: 'Quartz.Core.QuartzScheduler' - running locally.
  NOT STARTED.
  Currently in standby mode.
  Number of jobs executed: 0
  Using thread pool 'Quartz.Simpl.SimpleThreadPool' - with 3 threads.
  Using job-store 'Quartz.Simpl.RAMJobStore' - which does not support persistence. and is not clustered.

11.1.2014 14:52:04 [INFO]  Quartz.Impl.StdSchedulerFactory - Quartz scheduler 'MyScheduler' initialized
11.1.2014 14:52:04 [INFO]  Quartz.Impl.StdSchedulerFactory - Quartz scheduler version: 2.2.1.400
11.1.2014 14:52:04 [INFO]  Quartz.Core.QuartzScheduler - Scheduler MyScheduler_$_NON_CLUSTERED started.
```

We need a simple test job to test the functionality, lets create HelloJob that outputs greetings to console.

```csharp
public class HelloJob : IJob
{
 public void Execute(IJobExecutionContext context)
 {
  Console.WriteLine("Greetings from HelloJob!");
 }
}
```

To do something interesting, you need code just after Start() method, before the Thread.Sleep.

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
scheduler.ScheduleJob(job, trigger);
```

The complete console application will now look like this

```csharp
using System;
using System.Threading;

using Quartz;
using Quartz.Impl;
using Quartz.Job;

namespace ConsoleApplication1
{
    public class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                Common.Logging.LogManager.Adapter = new Common.Logging.Simple.ConsoleOutLoggerFactoryAdapter {Level = Common.Logging.LogLevel.Info};

                // Grab the Scheduler instance from the Factory 
                IScheduler scheduler = StdSchedulerFactory.GetDefaultScheduler();

                // and start it off
                scheduler.Start();

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
                scheduler.ScheduleJob(job, trigger);

                // some sleep to show what's happening
                Thread.Sleep(TimeSpan.FromSeconds(60));

                // and last shut down the scheduler when you are ready to close your program
                scheduler.Shutdown();
            }
            catch (SchedulerException se)
            {
                Console.WriteLine(se);
            }

            Console.WriteLine("Press any key to close the application");
            Console.ReadKey();
        }
    }

    public class HelloJob : IJob
    {
        public void Execute(IJobExecutionContext context)
        {
            Console.WriteLine("Greetings from HelloJob!");
        }
    }
}
```

Now go have some fun exploring Quartz.NET! You can continue by reading [the tutorial](tutorial/index.html).
