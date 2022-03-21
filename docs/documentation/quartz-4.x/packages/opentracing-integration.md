[Quartz.OpenTracing](https://www.nuget.org/packages/Quartz.OpenTracing)
provides integration with [OpenTracing](https://opentracing.io/). You may also consider
[Quartz.OpenTelemetry.Instrumentation](opentelemetry-integration.md) package which will supercede OpenTracing and OpenCensus
when OpenTelemetry project reaches maturity.

::: tip
Quartz 3.2.3 or later required.
:::

::: danger
The integration library can still live a bit and thus integration API can have breaking changes and change behavior.
:::

## Installation

You need to add NuGet package reference to your project which uses Quartz.

    Install-Package Quartz.OpenTracing

## Using

You can add Quartz configuration by invoking an extension method `AddQuartzOpenTracing` on `IServiceCollection`.


**Example Startup.ConfigureServices configuration**

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // make sure you configure logging and OpenTracing before quartz services
    services.AddQuartzOpenTracing(options =>
    {
        // these are the defaults
        options.ComponentName = "Quartz";
        options.IncludeExceptionDetails = false;
    });
}
```