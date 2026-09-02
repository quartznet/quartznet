# Quartz.NET in an ASP.NET Core application

One web application serving all three of Quartz's HTTP surfaces at once: the
[HTTP API](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/http-api.html) at
`/quartz-api`, the
[dashboard](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/dashboard.html) at
`/quartz`, and a health check at `/healthz`, with an OpenAPI document over the API. It is also where
several extension points are shown as working code — a job store of your own, a connection provider of
your own, a type loader of your own, and every kind of listener.

## Running it

```shell
dotnet run --project src/Quartz.Examples.AspNetCore
```

It listens on `http://localhost:5000` and starts in the `Development` environment, both from
`Properties/launchSettings.json`. Nothing external is needed: the scheduler uses `CustomJobStore`,
which wraps the in-memory store. The SQL Server registration beside it in `Startup.cs` is commented
out, and the `ConnectionStrings:Quartz` entry in `appsettings.json` is there for when you uncomment it.

## Authenticating

**The HTTP API requires an API key, in a header:**

| | |
|---|---|
| Header | `X-Quartz-ApiKey` |
| Value | `MySuperSecretApiKey` |
| Where the value comes from | `QuartzHttpApiKey` in `appsettings.json` |

```shell
curl -H "X-Quartz-ApiKey: MySuperSecretApiKey" http://localhost:5000/quartz-api/schedulers
```

The handler behind it is `ApiKeyAuthenticationHandler`, registered as the `api-key` scheme — a
deliberately tiny stand-in for whatever your application already authenticates with. Quartz supplies
none: it authorizes, and something else authenticates.

Without the header the API answers `401`. Swagger UI is at `/swagger` in Development and has an
**Authorize** button that sends the same header, so the API can be driven from a browser.

**The dashboard is anonymous, on purpose.** A browser cannot send that header, and a mapping that said
nothing about authorization would refuse to start — so the sample says `AllowAnonymous()` out loud at
the map site. A real deployment authorizes it instead, with `RequireAuthorization()` there or
`QuartzDashboardOptions.AuthorizationPolicy`.

## What it shows

| File | What it shows |
|---|---|
| `Startup.cs` | The whole registration: `AddQuartz`, the HTTP API, the dashboard, the health check, the hosted service, and the endpoint mapping that authorizes each |
| `CustomJobStore.cs` | A store of your own over `DelegatingJobStore` |
| `CustomSqlServerConnectionProvider.cs` | An `IDbProvider` of your own, for a connection strategy Quartz does not ship |
| `CustomTypeLoader.cs` | A type loader of your own, for resolving job types by name |
| `ApiKeyAuthenticationHandler.cs` | The authentication scheme the API is authorized against |
| `ExampleJob.cs`, `SlowJob.cs`, `AsyncDisposableJob.cs` | Jobs with dependencies, with a timeout, and with `IAsyncDisposable` |
| `SampleJobListener.cs` and its five siblings | Listeners registered two ways: with a matcher through `AddJobListener`, and as plain container registrations |
| `quartz_jobs.config` | An XML schedule read by the plugin, alongside the code-declared jobs |
