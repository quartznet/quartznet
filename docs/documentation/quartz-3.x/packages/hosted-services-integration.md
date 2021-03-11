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
                // base configuration from appsettings.json
                services.Configure<QuartzOptions>(Configuration.GetSection("Quartz"));

                // base configuration for DI
                services.AddQuartz(q =>
                {
                    // handy when part of cluster or you want to otherwise identify multiple schedulers
                    q.SchedulerId = "Scheduler-Core";
                    
                    // we take this from appsettings.json, just show it's possible
                    // q.SchedulerName = "Quartz ASP.NET Core Sample Scheduler";

                    q.UseMicrosoftDependencyInjectionJobFactory();

                    // or 
                    // q.UseMicrosoftDependencyInjectionScopedJobFactory();
                    
                    // these are the defaults
                    q.UseSimpleTypeLoader();
                    q.UseInMemoryStore();
                    q.UseDefaultThreadPool(tp =>
                    {
                        tp.MaxConcurrency = 10;
                    });
                    
                    // configure jobs with code
                    var jobKey = new JobKey("awesome job", "awesome group");
                    q.AddJob<ExampleJob>(j => j
                        .StoreDurably()
                        .WithIdentity(jobKey)
                        .WithDescription("my awesome job")
                    );

                    q.AddTrigger(t => t
                        .WithIdentity("Simple Trigger")    
                        .ForJob(jobKey)
                        .StartNow()
                        .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever())
                        .WithDescription("my awesome simple trigger")
                    );

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
