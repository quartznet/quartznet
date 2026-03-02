---
title: HTTP API
---

Quartz HTTP API is provided by [Quartz.AspNetCore](https://www.nuget.org/packages/Quartz.AspNetCore) and exposes scheduler management endpoints for ASP.NET Core apps.

## Installation

Add package references:

```shell
Install-Package Quartz.AspNetCore
Install-Package Quartz.Extensions.Hosting
```

## Basic setup

Configure Quartz and enable the HTTP API:

```csharp
services.AddQuartz(q =>
{
    q.AddHttpApi(options =>
    {
        options.ApiPath = "/quartz-api";
    });
});

services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```

Map endpoints:

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.UseEndpoints(endpoints =>
{
    endpoints.MapQuartzApi().RequireAuthorization();
});
```

By default, API endpoints are exposed under `/quartz-api`.

## Endpoint groups

- **Schedulers**: list schedulers, read metadata/context, start, stand-by, shutdown, clear, pause-all, resume-all
- **Jobs**: list keys/details, check existence, list currently executing, pause/resume, trigger, interrupt, add, delete
- **Triggers**: list keys/details/state, pause/resume, reset from error state, schedule/unschedule/reschedule
- **Calendars**: list names, get details, add/replace, delete

## Configuration options

`QuartzHttpApiOptions` supports:

- `ApiPath` (default: `/quartz-api`) - base path for all API endpoints
- `IncludeStackTraceInProblemDetails` (default: `false`) - includes stack traces in RFC 7807 error payloads

## Production hardening

- Require authentication/authorization on `MapQuartzApi()`
- Keep `IncludeStackTraceInProblemDetails` disabled in production
- Restrict mutating operations (schedule, delete, pause/resume, shutdown) to trusted operator roles
- In clustered setups, treat API calls as scheduler control operations that affect cluster-wide behavior
