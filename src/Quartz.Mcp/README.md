# Quartz.Mcp

[Quartz.Mcp](https://www.nuget.org/packages/Quartz.Mcp) serves an MCP endpoint over Streamable HTTP, so an AI
agent can read and operate the Quartz.NET schedulers of a process, and of its fleet, under the same operator roles
as the dashboard and the HTTP API.

- Read-only by default. Changes need `ReadOnly = false` and an `OperationAuthorizationPolicy`.
- Stateless always: no session is bound to a caller.
- 19 tools: lists and details of schedulers, jobs, triggers, running firings and history, plus pause, resume,
  trigger now, interrupt, reset from error, reschedule and unschedule. The last two answer a preview and a
  confirmation token first.
- No data value is ever answered: job and trigger data appear as their keys only.
- Trimmable and native AOT compatible.

## Installation

```shell
dotnet add package Quartz.Mcp
```

## Usage

<!-- snippet: sample_readme_mcp -->
```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddQuartz();
builder.AddQuartzHostedService();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Quartz.Operations", policy => policy.RequireSchedulerRoles(roles =>
    {
        roles.Viewers.Add("ops-viewer");
        roles.Operators.Add("ops-operator");
        roles.Administrators.Add("ops-admin");
    }));

// Read-only until told otherwise; writing needs the operation policy.
builder.Services.AddQuartzMcp(options =>
{
    options.ReadOnly = false;
    options.OperationAuthorizationPolicy = "Quartz.Operations";
});

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapQuartzMcp().RequireAuthorization();
```
<!-- endSnippet -->

Authorize the endpoint with a bearer token, never a cookie: a browser sends a cookie on its own. It depends on
`ModelContextProtocol.AspNetCore` 2.3.1 or later, the first release outside GHSA-x863-g495-4cjc, which asks for
`Microsoft.Extensions.*.Abstractions` 10.0.13 and `Microsoft.Extensions.AI.Abstractions` 10.11.0.

Documentation: [Quartz.NET MCP endpoint](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/mcp.html)
