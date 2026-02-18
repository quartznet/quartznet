---

title: OpenTelemetry Integration
---

::: warning DEPRECATED
The `Quartz.OpenTelemetry.Instrumentation` package is **obsolete** and no longer maintained. It is incompatible with .NET 10 and later versions.

**Please use the official [OpenTelemetry.Instrumentation.Quartz](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz) package instead**, which is actively maintained by the OpenTelemetry community and fully compatible with the latest .NET versions.
:::

## Installation

You need to add NuGet package reference to your project which uses Quartz.

```shell
Install-Package OpenTelemetry.Instrumentation.Quartz
```

It also makes sense to install package for exporter to actually get the results somewhere.

::: tip
Quartz 3.1 or later required.
:::

## Using

You can add Quartz configuration by invoking an extension method `AddQuartzInstrumentation` on `TracerProviderBuilder`.

In the next example we will integrate with [Jaeger](https://www.jaegertracing.io/). We expect that you have also installed dependencies:

* [OpenTelemetry.Extensions.Hosting](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting)
* [OpenTelemetry.Exporter.Jaeger](https://www.nuget.org/packages/OpenTelemetry.Exporter.Jaeger)

You can run local Jaeger via docker using:

```shell
$ docker run -d --name jaeger \
  -e COLLECTOR_ZIPKIN_HTTP_PORT=9411 \
  -p 5775:5775/udp \
  -p 6831:6831/udp \
  -p 6832:6832/udp \
  -p 5778:5778 \
  -p 16686:16686 \
  -p 14268:14268 \
  -p 14250:14250 \
  -p 9411:9411 \
  jaegertracing/all-in-one:1.18
```

**Example Startup.ConfigureServices configuration**

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // make sure you configure logging and open telemetry before Quartz services

    services.AddOpenTelemetry(builder =>
    {
        builder
            .AddQuartzInstrumentation()
            .UseJaegerExporter(o =>
            {
                o.ServiceName = "My Software Name";

                // these are the defaults
                o.AgentHost = "localhost";
                o.AgentPort = 6831;
            });
    });
}
```
