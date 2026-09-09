# Quartz.NET 4.x API reference

Generated from the XML documentation comments of the ten shipped packages. It is the reference:
what a type is, what every member takes and returns, and the remarks that say why. The prose - quick
start, tutorial, configuration, how-tos - lives on the
[documentation site](https://www.quartz-scheduler.net/documentation/), and the
[migration guide](https://www.quartz-scheduler.net/documentation/quartz-4.x/migration-guide.html)
maps the 3.x names onto these.

This set rolls forward with every 4.x minor release. The 3.x reference stays at
[/apidoc/3.0](https://docs.quartz-scheduler.net/apidoc/3.0/html).

## Where to start

| | |
|---|---|
| Schedule something | [JobBuilder](xref:Quartz.JobBuilder), [TriggerBuilder](xref:Quartz.TriggerBuilder), [IScheduler](xref:Quartz.IScheduler) |
| Register it with a host | [QuartzServiceCollectionExtensions.AddQuartz](xref:Quartz.QuartzServiceCollectionExtensions), [IQuartzBuilder](xref:Quartz.IQuartzBuilder) |
| Do without a container | [QuartzSchedulerBuilder](xref:Quartz.QuartzSchedulerBuilder) |
| Write an extension | the [Quartz.Extensibility](xref:Quartz.Extensibility) namespace |

## The packages

| Package | Namespaces |
|---|---|
| [Quartz](https://www.nuget.org/packages/Quartz) - the scheduler, its hosting and dependency injection, the in-memory and ADO.NET job stores, and `System.Text.Json` serialization | [Quartz](xref:Quartz), [Quartz.Diagnostics](xref:Quartz.Diagnostics), [Quartz.Extensibility](xref:Quartz.Extensibility), [Quartz.Impl](xref:Quartz.Impl), [Quartz.Impl.AdoJobStore](xref:Quartz.Impl.AdoJobStore), [Quartz.Impl.Calendar](xref:Quartz.Impl.Calendar), [Quartz.Impl.Triggers](xref:Quartz.Impl.Triggers), [Quartz.Listeners](xref:Quartz.Listeners), [Quartz.Serialization.SystemTextJson](xref:Quartz.Serialization.SystemTextJson) |
| [Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore) - the HTTP API and the ASP.NET Core wiring around it | [QuartzAspNetCoreConfigurationExtensions](xref:Quartz.QuartzAspNetCoreConfigurationExtensions), [QuartzHttpApiOptions](xref:Quartz.QuartzHttpApiOptions) |
| [Quartz.Aspire](https://www.nuget.org/packages/Quartz.Aspire) - an Aspire connection name turned into a persistent job store | [QuartzAspireHostApplicationBuilderExtensions](xref:Quartz.QuartzAspireHostApplicationBuilderExtensions), [QuartzAspireSettings](xref:Quartz.QuartzAspireSettings) |
| [Quartz.Dashboard](https://www.nuget.org/packages/Quartz.Dashboard) - the Blazor dashboard | [QuartzDashboardOptions](xref:Quartz.QuartzDashboardOptions), [Quartz.Dashboard.Hubs](xref:Quartz.Dashboard.Hubs), [Quartz.Dashboard.Plugins](xref:Quartz.Dashboard.Plugins), [Quartz.Dashboard.Services](xref:Quartz.Dashboard.Services) |
| [Quartz.Extensions.Redis](https://www.nuget.org/packages/Quartz.Extensions.Redis) - a Redis lock handler for a clustered scheduler | [Quartz.Extensions.Redis](xref:Quartz.Extensions.Redis), [RedisLockHandlerOptions](xref:Quartz.RedisLockHandlerOptions) |
| [Quartz.HttpClient](https://www.nuget.org/packages/Quartz.HttpClient) - an `IScheduler` that talks to the HTTP API | [HttpScheduler](xref:Quartz.HttpScheduler) |
| [Quartz.Jobs](https://www.nuget.org/packages/Quartz.Jobs) - ready-made jobs | [Quartz.Jobs](xref:Quartz.Jobs) |
| [Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) - history logging, and jobs loaded from XML or JSON | [Quartz.Plugins.History](xref:Quartz.Plugins.History), [Quartz.Plugins.Json](xref:Quartz.Plugins.Json), [Quartz.Plugins.Xml](xref:Quartz.Plugins.Xml) |
| [Quartz.Plugins.TimeZoneConverter](https://www.nuget.org/packages/Quartz.Plugins.TimeZoneConverter) - IANA and Windows time zone ids either way round | [TimeZonePluginConfigurationExtensions](xref:Quartz.TimeZonePluginConfigurationExtensions) |
| [Quartz.Serialization.Newtonsoft](https://www.nuget.org/packages/Quartz.Serialization.Newtonsoft) - `Newtonsoft.Json` for what a job store persists | [Quartz.Serialization.Newtonsoft](xref:Quartz.Serialization.Newtonsoft) |

The dashboard's Blazor components are left out: the Razor compiler emits a public class per `.razor`
file, but they are the dashboard's UI rather than API anyone calls.
