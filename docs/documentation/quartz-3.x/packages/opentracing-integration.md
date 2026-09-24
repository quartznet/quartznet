---

title: OpenTracing Integration
---

[Quartz.OpenTracing](https://www.nuget.org/packages/Quartz.OpenTracing)
integrates Quartz with [OpenTracing](https://opentracing.io/). Also consider the
[OpenTelemetry.Instrumentation.Quartz](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz) package, which will supersede OpenTracing and OpenCensus.

::: tip
Quartz 3.2.3 or later required.
:::

::: danger
The integration library may still change: its API can have breaking changes and change behavior.
:::

## Installation

```shell
Install-Package Quartz.OpenTracing
```

## Using

Call the `AddQuartzOpenTracing` extension method on `IServiceCollection`.

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
