[Quartz.Extensions.DependencyInjection](https://www.nuget.org/packages/Quartz.Extensions.DependencyInjection)
provides integration with [Microsoft Dependency Injection](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection).

::: tip
Quartz 3.1 or later required.
:::

## Installation

You need to add NuGet package reference to your project which uses Quartz.

    Install-Package Quartz.Extensions.DependencyInjection

## Using

You can add Quartz configuration by invoking an extension method `AddQuartz` on `IServiceCollection`.
The configuration building wraps various [configuration properties](../configuration/reference) with strongly-typed API.
You can also configure properties using standard .NET Core `appsettings.json` inside configuration section `Quartz`.

**Example appsettings.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Quartz": {
    "quartz.scheduler.instanceName": "Quartz ASP.NET Core Sample Scheduler"
  }
}
````

**Example Startup.ConfigureServices configuration**

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddQuartz(q =>
    {
        // handy when part of cluster or you want to otherwise identify multiple schedulers
        q.SchedulerId = "Scheduler-Core";
        
        // we take this from appsettings.json, just show it's possible
        // q.SchedulerName = "Quartz ASP.NET Core Sample Scheduler";
        
        // we could leave DI configuration intact and then jobs need to have public no-arg constructor
        // the MS DI is expected to produce transient job instances 
        q.UseMicrosoftDependencyInjectionJobFactory(options =>
        {
            // if we don't have the job in DI, allow fallback to configure via default constructor
            options.AllowDefaultConstructor = true;
        });

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

        q.AddTrigger(t => t
            .WithIdentity("Cron Trigger")    
            .ForJob(jobKey)
            .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(3)))
            .WithCronSchedule("0/3 * * * * ?")
            .WithDescription("my awesome cron trigger")
        );

        q.AddTrigger(t => t
            .WithIdentity("Daily Trigger")    
            .ForJob(jobKey)
            .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(5)))
            .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))
            .WithDescription("my awesome daily time interval trigger")
        );
        
        // also add XML configuration and poll it for changes
        q.UseXmlSchedulingConfiguration(x =>
        {
            x.Files = new[] { "~/quartz_jobs.config" };
            x.ScanInterval = TimeSpan.FromSeconds(2);
            x.FailOnFileNotFound = true;
            x.FailOnSchedulingError = true;
        });

        // convert time zones using converter that can handle Windows/Linux differences
        q.UseTimeZoneConverter();
        
        // add some listeners
        q.AddSchedulerListener<SampleSchedulerListener>();
        q.AddJobListener<SampleJobListener>(GroupMatcher<JobKey>.GroupEquals(jobKey.Group));
        q.AddTriggerListener<SampleTriggerListener>();

        // example of persistent job store using JSON serializer as an example
        /*
        q.UsePersistentStore(s =>
        {
            s.UseProperties = true;
            s.RetryInterval = TimeSpan.FromSeconds(15);
            s.UseSqlServer(sqlServer =>
            {
                sqlServer.ConnectionString = "some connection string";
                // this is the default
                sqlServer.TablePrefix = "QRTZ_";
            });
            s.UseJsonSerializer();
            s.UseClustering(c =>
            {
                c.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                c.CheckinInterval = TimeSpan.FromSeconds(10);
            });
        });
        */
    });
}
```