---

title: OpenTracing Integration
---

[Quartz.OpenTracing](https://www.nuget.org/packages/Quartz.OpenTracing)
provides integration with [OpenTracing](https://opentracing.io/). You may also consider
[OpenTelemetry.Instrumentation.Quartz](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz) package which will supersede OpenTracing and OpenCensus.

::: tip
Quartz 3.2.3 or later required.
:::

::: danger
The integration library can still live a bit and thus integration API can have breaking changes and change behavior.
:::

## Installation

You need to add NuGet package reference to your project which uses Quartz.

```shell
Install-Package Quartz.OpenTracing
```

## Using

You can add Quartz configuration by invoking an extension method `AddQuartzOpenTracing` on `IServiceCollection`.

**Example Startup.ConfigureServices configuration**

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // make sure you configure logging and OpenTracing before Quartz services
    services.AddQuartzOpenTracing(options =>
    {
        // these are the defaults
        options.ComponentName = "Quartz";
        options.IncludeExceptionDetails = false;
    });
}
```
