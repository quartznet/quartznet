---

title: Hosted Services Integration
---

[Quartz](https://www.nuget.org/packages/Quartz)
provides integration with [hosted services](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services).

## Using

You can add Quartz configuration by invoking an extension method `AddQuartzHostedService` on the host
application builder, or on `IServiceCollection`. This will add a hosted Quartz server into process that
will be started and stopped based on applications lifetime.

::: tip
See [Quartz documentation](microsoft-di-integration) to learn more about configuring Quartz scheduler, jobs and triggers.

Need multiple independent schedulers in one application? See [Multiple Schedulers](multiple-schedulers.md).
:::

The hosted service starts every scheduler in the container, and resolves them when the host starts —
so `AddQuartz` and `AddQuartzHostedService` can be called in either order. The options apply to every
scheduler; one that has to differ is configured by name with
`AddQuartzHostedService("SchedulerName", options => …)`.

::: warning
Calling `AddQuartzHostedService()` without registering any scheduler throws at startup: the hosted
service was asked for, so something was meant to run. Register a scheduler with `AddQuartz(...)`.
:::

**Example program utilizing hosted services configuration**

```csharp
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog();

// see Quartz documentation about how to configure different configuration aspects
builder.AddQuartz(q =>
{
    // your configuration here
});

// Quartz hosting
builder.AddQuartzHostedService(options =>
{
    // when shutting down we want jobs to complete gracefully
    options.WaitForJobsToComplete = true;
});

builder.Build().Run();
```

`builder.AddQuartz(...)` is `builder.Services.AddQuartz(...)` with the application's configuration
already found: it reads the `Quartz` section, so anything described in `appsettings.json` is applied
before your callback. The `IServiceCollection` overloads are unchanged, and are what to use for a
configuration section under a different name:

```csharp
builder.Services.AddQuartz(builder.Configuration.GetSection("Scheduling"), q => { });
```

A string names a scheduler, here as everywhere else in Quartz — `builder.AddQuartz("reporting", …)`
registers a scheduler called `reporting`, reading its settings from `Quartz:Schedulers:reporting` when
the section describes several. `builder.AddQuartzSchedulers()` registers one per child of that
sub-section.
