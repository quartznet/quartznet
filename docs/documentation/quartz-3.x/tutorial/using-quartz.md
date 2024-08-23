---

title: 'Using Quartz'
---

Before you can use the scheduler, it needs to be instantiated (who'd have guessed?).
To do this, you use an implementor of ISchedulerFactory.

Once a scheduler is instantiated, it can be started, placed in stand-by mode, and shutdown.
Note that once a scheduler is shutdown, it cannot be restarted without being re-instantiated.
Triggers do not fire (jobs do not execute) until the scheduler has been started, nor while it is
in the paused state.

Here's a quick snippet of code, that instantiates and starts a scheduler, and schedules a job for execution:

### Install Quartz.NET NuGets

```sh
Install-Package Microsoft.Extensions.Hosting
Install-Package Quartz
Install-Package Quartz.Extensions.DependencyInjection
Install-Package Quartz.Extensions.Hosting
```

### Configure `Program.cs`

A minimal style example of configuring Quartz.NET with the Microsoft Hosting framework
looks like this.

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

Let's add a job to this.

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

As you can see, working with Quartz.NET is rather simple. In [Lesson 2](jobs-and-triggers.md) we'll give a quick overview of Jobs and Triggers, so that you can more fully understand this example.

## Traditional Program.cs

If you are working in a pre-minimal API project, you can use the same old `Program.cs` structure
as well.

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
