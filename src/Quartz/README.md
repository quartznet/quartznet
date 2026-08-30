# Quartz.NET

[Quartz.NET](https://www.nuget.org/packages/Quartz) is a full-featured, open source job scheduling
system that can be used from the smallest apps to large scale enterprise systems.

## Installation

```shell
dotnet add package Quartz
```

That is everything a scheduler needs: dependency injection, hosting, the scheduler health check and
System.Text.Json serialization are part of this package, where Quartz 3 shipped them separately.

## Quick start

A job is a class with one method:

<!-- snippet: sample_readme_quartz_job -->
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

Register it with a trigger that fires it; the hosted service starts and stops the scheduler with the
application:

<!-- snippet: sample_readme_quartz_host -->
```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddQuartz(q => q.ScheduleJob<HelloJob>(trigger => trigger
    .WithIdentity("hello")
    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever())));

builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

Console applications and tests build a scheduler without a host, from the same configuration API:

<!-- snippet: sample_readme_quartz_standalone -->
```csharp
IScheduler scheduler = await QuartzSchedulerBuilder.Create().BuildScheduler();
await scheduler.Start();
```
<!-- endSnippet -->

## Optional packages

| Package | For |
|---|---|
| [Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore) | the HTTP API |
| [Quartz.Dashboard](https://www.nuget.org/packages/Quartz.Dashboard) | the web dashboard |
| [Quartz.HttpClient](https://www.nuget.org/packages/Quartz.HttpClient) | driving a remote scheduler over the HTTP API |
| [Quartz.Jobs](https://www.nuget.org/packages/Quartz.Jobs) | ready-made jobs: file scanning, sending mail, running a process |
| [Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) | history logging, XML and JSON schedule files, the interrupt monitor |
| [Quartz.Plugins.TimeZoneConverter](https://www.nuget.org/packages/Quartz.Plugins.TimeZoneConverter) | Windows and IANA time zone ids resolving on either operating system |
| [Quartz.Serialization.Newtonsoft](https://www.nuget.org/packages/Quartz.Serialization.Newtonsoft) | persisting with Newtonsoft.Json instead of System.Text.Json |
| [Quartz.Extensions.Redis](https://www.nuget.org/packages/Quartz.Extensions.Redis) | Redis distributed locks for a cluster |

## Documentation

- [Quick start](https://www.quartz-scheduler.net/documentation/quartz-4.x/quick-start.html)
- [Tutorial](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/)
- [Configuration reference](https://www.quartz-scheduler.net/documentation/quartz-4.x/configuration/reference.html)
- [Publishing trimmed and native AOT](https://www.quartz-scheduler.net/documentation/quartz-4.x/how-tos/trimming-and-native-aot.html)
- [Migrating from Quartz 3](https://www.quartz-scheduler.net/documentation/quartz-4.x/migration-guide.html)
