[Quartz.Extensions.Hosting](https://www.nuget.org/packages/Quartz.Extensions.Hosting)
provides integration with [hosted services](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services).

::: tip
Quartz 3.2 or later required for Quartz.Extensions.Hosting. You can use package Quartz.AspNetCore with version 3.1.
:::

## Installation

You need to add NuGet package reference to your project which uses Quartz.

**Quartz 3.1**

    Install-Package Quartz.AspNetCore 

**Quartz 3.2 onwards**

    Install-Package Quartz.Extensions.Hosting

## Using

You can add Quartz configuration by invoking an extension method `AddQuartzHostedService` on `IServiceCollection`.
This will add a hosted quartz server into process that will be started and stopped based on applications lifetime.

::: tip
See [Quartz.Extensions.DependencyInjection documentation](microsoft-di-integration) to learn more about configuring Quartz scheduler, jobs and triggers.
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
