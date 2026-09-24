---

title: OpenTelemetry Integration
---

::: warning DEPRECATED
The `Quartz.OpenTelemetry.Instrumentation` package is **obsolete** and no longer maintained. It is incompatible with .NET 10 and later.

**Use the official [OpenTelemetry.Instrumentation.Quartz](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz) package instead.** The OpenTelemetry community maintains it, and it supports the latest .NET versions.
:::

## Installation

```shell
Install-Package OpenTelemetry.Instrumentation.Quartz
```

Also install an exporter package, so the results go somewhere.

::: tip
Quartz 3.1 or later required.
:::

## Using

Call the `AddQuartzInstrumentation` extension method on `TracerProviderBuilder`.

This example exports to [Jaeger](https://www.jaegertracing.io/). It also needs:

* [OpenTelemetry.Extensions.Hosting](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting)
* [OpenTelemetry.Exporter.Jaeger](https://www.nuget.org/packages/OpenTelemetry.Exporter.Jaeger)

Run Jaeger locally with docker:

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
