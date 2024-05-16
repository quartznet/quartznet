---

title: ASP.NET Core Integration
---

[Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore)
provides integration with [ASP.NET Core hosted services](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services).

::: tip
If you only need the generic host, [generic host integration](hosted-services-integration) might suffice.
:::

## Installation

You need to add NuGet package reference to your project which uses Quartz.

```shell
Install-Package Quartz.AspNetCore
```

## Using

You can add Quartz configuration by invoking an extension method `AddQuartzServer` on `IServiceCollection`.
This will add a hosted Quartz server into ASP.NET Core process that will be started and stopped based on applications lifetime.

::: tip
See [Quartz.Extensions.DependencyInjection documentation](microsoft-di-integration) to learn more about configuring Quartz scheduler, jobs and triggers.
:::

**Example Startup.ConfigureServices configuration**

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddQuartz(q =>
    {
        // base Quartz scheduler, job and trigger configuration
    });

    // ASP.NET Core hosting
    services.AddQuartzServer(options =>
    {
        // when shutting down we want jobs to complete gracefully
        options.WaitForJobsToComplete = true;
    });
}
```

## A practical example of the setup

In the code below you can see a real application of the Quartz package within ASP.NET Core MVC.

To better illustrate the use of the Quartz library, imagine you have a `Program.cs` file that is always created when you choose the MVC architecture, and then imagine a `Jobs` folder where you have all the tasks you want Quartz to perform in the background when you run your web application.

After that, it's pretty straightforward.

In the `Jobs` folder, you create a class that will perform the tasks you specify.
The class should extend the `IJob` interface and implement the `Execute` method.

**Example SendEmailJob.cs configuration**

```csharp
public class SendEmailJob : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        // Code that sends a periodic email to the user (for example)
        // Note: This method must always return a value 
        // This is especially important for trigger listers watching job execution 
        return Task.CompletedTask;
    }
}        
```

After that, you just need to build Quartz trigger in `Program.cs`, which guarantees that the job will run according to the preset interval.

**Example Program.cs configuration**

```csharp
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionScopedJobFactory();
    // Just use the name of your job that you created in the Jobs folder.
    var jobKey = new JobKey("SendEmailJob");
    q.AddJob<SendEmailJob>(opts => opts.WithIdentity(jobKey));
    
    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity("SendEmailJob-trigger")
         //This Cron interval can be described as "run every minute" (when second is zero)
        .WithCronSchedule("0 * * ? * *")
    );
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
```

For more information on cron triggers and their format, you can use the tutorial directly from Quartz - [Cron Triggers](../tutorial/crontriggers.md).
