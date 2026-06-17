# Quartz.NET

[Quartz.NET](https://www.nuget.org/packages/Quartz) is a full-featured, open source job scheduling system that can be used from the smallest apps to large scale enterprise systems.

## Installation

```shell
dotnet add package Quartz
```

To add JSON serialization for persistent job stores, also add [Quartz.Serialization.SystemTextJson](https://www.nuget.org/packages/Quartz.Serialization.SystemTextJson) (or [Quartz.Serialization.Json](https://www.nuget.org/packages/Quartz.Serialization.Json)).

## Quick start

```csharp
using Quartz;
using Quartz.Impl;

// grab the scheduler instance from the factory and start it
StdSchedulerFactory factory = new StdSchedulerFactory();
IScheduler scheduler = await factory.GetScheduler();
await scheduler.Start();

// define the job and tie it to our HelloJob class
IJobDetail job = JobBuilder.Create<HelloJob>()
    .WithIdentity("job1", "group1")
    .Build();

// trigger the job to run now, and then repeat every 10 seconds
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger1", "group1")
    .StartNow()
    .WithSimpleSchedule(x => x
        .WithIntervalInSeconds(10)
        .RepeatForever())
    .Build();

await scheduler.ScheduleJob(job, trigger);
```

```csharp
public class HelloJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await Console.Out.WriteLineAsync("Greetings from HelloJob!");
    }
}
```

> **Tip:** Quartz.NET comes with sane defaults — you only need explicit configuration when you want to change them.

## Documentation

📖 **Full documentation:** <https://www.quartz-scheduler.net/documentation/quartz-3.x/>

* [Quick start guide](https://www.quartz-scheduler.net/documentation/quartz-3.x/quick-start.html)
* [Tutorial](https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/)
* [Configuration reference](https://www.quartz-scheduler.net/documentation/quartz-3.x/configuration/reference.html)
