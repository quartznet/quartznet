[Quartz.OpenTelemetry.Instrumentation](https://www.nuget.org/packages/Quartz.OpenTelemetry.Instrumentation)
provides integration with [OpenTelemetry](https://opentelemetry.io/).

::: tip
Quartz 3.1 or later required.
:::

::: danger
The OpenTelemetry integration libraries are in beta so this integration can break and change behavior.
:::

## Installation

You need to add NuGet package reference to your project which uses Quartz.

    Install-Package Quartz.OpenTelemetry.Instrumentation

It also makes sense to install package for exporter to actually get the results somewhere.

## Using

You can add Quartz configuration by invoking an extension method `AddQuartzInstrumentation` on `TracerProviderBuilder`.

In the next example we will integrate with [Jaeger](https://www.jaegertracing.io/). We expect that you have also installed dependencies:

* [OpenTelemetry.Extensions.Hosting](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting)
* [OpenTelemetry.Exporter.Jaeger](https://www.nuget.org/packages/OpenTelemetry.Exporter.Jaeger)

You can run local Jaeger via docker using:

```
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
    // make sure you configure logging and open telemetry before quartz services

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