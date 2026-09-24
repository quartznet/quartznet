---

title: 'Using Quartz'
---

An `ISchedulerFactory` implementation creates the scheduler. Once created, a scheduler can be started, put in stand-by mode, and shut down.

* A shut-down scheduler cannot be restarted; create a new one.
* Triggers do not fire (jobs do not execute) before the scheduler is started, nor while it is paused.

The code below creates and starts a scheduler and schedules a job.

### Install Quartz.NET NuGets

```sh
Install-Package Microsoft.Extensions.Hosting
Install-Package Quartz
Install-Package Quartz.Extensions.DependencyInjection
Install-Package Quartz.Extensions.Hosting
```

### Configure `Program.cs`

A minimal Quartz.NET setup with the Microsoft Hosting framework:

```csharp
using Microsoft.Extensions.Hosting;
using Quartz;

var builder = Host.CreateDefaultBuilder()
    .ConfigureServices((cxt, services) =>
    {
        services.AddQuartz();
        services.AddQuartzHostedService(opt =>
        {
            opt.WaitForJobsToComplete = true;
        });
    }).Build();

// will block until the last running job completes
await builder.RunAsync();
```

With a job:

```csharp

using Microsoft.Extensions.Hosting;
using Quartz;

var builder = Host.CreateDefaultBuilder()
    .ConfigureServices((cxt, services) =>
    {
        services.AddQuartz(q =>
        {
            q.UseMicrosoftDependencyInjectionJobFactory();
        });
        services.AddQuartzHostedService(opt =>
        {
            opt.WaitForJobsToComplete = true;
        });
    }).Build();

var schedulerFactory = builder.Services.GetRequiredService<ISchedulerFactory>();
var scheduler = await schedulerFactory.GetScheduler();

// define the job and tie it to our HelloJob class
var job = JobBuilder.Create<HelloJob>()
    .WithIdentity("myJob", "group1")
    .Build();

// Trigger the job to run now, and then every 40 seconds
var trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger", "group1")
    .StartNow()
    .WithSimpleSchedule(x => x
        .WithIntervalInSeconds(40)
        .RepeatForever())
    .Build();

await scheduler.ScheduleJob(job, trigger);

// will block until the last running job completes
await builder.RunAsync();
```

[Jobs and Triggers](jobs-and-triggers.md) explains the parts of this example.

## Traditional Program.cs

A project without minimal APIs can use the classic `Program.cs` structure:

```csharp
using Microsoft.Extensions.Hosting;
using Quartz;

namespace Example;

public class Program
{
    public static async Task Main(string[] args) {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureServices((cxt, services) =>
            {
                services.AddQuartz(q =>
                {
                    q.UseMicrosoftDependencyInjectionJobFactory();
                });
                services.AddQuartzHostedService(opt =>
                {
                    opt.WaitForJobsToComplete = true;
                });
            }).Build();

        // will block until the last running job completes
        await builder.RunAsync();
    }
}
```
