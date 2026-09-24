---

title: Hosted Services Integration
---

[Quartz.Extensions.Hosting](https://www.nuget.org/packages/Quartz.Extensions.Hosting)
integrates Quartz with [hosted services](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services).

::: tip
Quartz.Extensions.Hosting requires Quartz 3.2 or later. On 3.1, use the Quartz.AspNetCore package.
:::

## Installation

**Quartz 3.1**

```shell
Install-Package Quartz.AspNetCore 
```

**Quartz 3.2 onwards**

```shell
Install-Package Quartz.Extensions.Hosting
```

## Using

The `AddQuartzHostedService` extension method on `IServiceCollection` adds a hosted Quartz server to the process, started and stopped with the application's lifetime.

::: tip
The [Quartz.Extensions.DependencyInjection documentation](microsoft-di-integration) covers configuring the scheduler, jobs and triggers.

For several independent schedulers in one application, see [Multiple Schedulers](multiple-schedulers.md).
:::

**Example program utilizing hosted services configuration**

```csharp
public class Program
{
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();
        
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((hostContext, services) =>
            {
                // see Quartz.Extensions.DependencyInjection documentation about how to configure different configuration aspects
                services.AddQuartz(q =>
                {
                    // your configuration here
                });

                // Quartz.Extensions.Hosting hosting
                services.AddQuartzHostedService(options =>
                {
                    // when shutting down we want jobs to complete gracefully
                    options.WaitForJobsToComplete = true;
                });
            });
}

```
