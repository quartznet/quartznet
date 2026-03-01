# Quartz.NET Blazor Dashboard — Architecture Document

**Author:** Keaton (Lead Architect)  
**Date:** 2025-02-16  
**Status:** PROPOSED  
**Replaces:** `src/Quartz.Web` (Aurelia/net6.0 — to be deleted)

---

## 1. Executive Summary

### What We're Building

A modern, embeddable Blazor Server dashboard for Quartz.NET that provides real-time monitoring and management of job schedulers. The dashboard ships as a NuGet package (`Quartz.Dashboard`) that developers plug into existing ASP.NET Core applications with two lines of code — mirroring the simplicity of Hangfire's `app.UseHangfireDashboard()`.

### Why

The existing `Quartz.Web` is a dead Aurelia/net6.0 SPA that was never shipped as a NuGet package. The community has requested a proper dashboard since 2015 (GitHub issue #251). Competing libraries (Hangfire, CrystalQuartz, Quartzmin) all ship embeddable dashboards — Quartz.NET is the only major .NET scheduler without one.

### How It Fits Into Quartz.NET

The dashboard sits on top of the existing `Quartz.AspNetCore` HTTP API (45+ REST endpoints already built). It does **not** access `IScheduler` directly — it consumes the REST API, meaning it works identically whether the scheduler is in-process or remote. This separation also means the dashboard UI and API can evolve independently.

```
┌─────────────────────────────────────────────────────┐
│                  Host Application                    │
│                                                      │
│  ┌──────────────────┐  ┌──────────────────────────┐  │
│  │ Quartz.Dashboard │  │   Quartz.AspNetCore      │  │
│  │  (Blazor Server) │──│   (REST API endpoints)   │  │
│  │                  │  │                           │  │
│  │  /quartz/*       │  │  /quartz-api/*            │  │
│  └──────────────────┘  └──────────────────────────┘  │
│           │                       │                   │
│           │        ┌──────────────┘                   │
│           ▼        ▼                                  │
│  ┌──────────────────────────┐                        │
│  │    ISchedulerFactory     │                        │
│  │    IScheduler (1..N)     │                        │
│  └──────────────────────────┘                        │
└─────────────────────────────────────────────────────┘
```

---

## 2. Project Structure

### New NuGet Packages

| Package | Description | Dependencies |
|---------|-------------|--------------|
| `Quartz.Dashboard` | Blazor Server dashboard UI + SignalR hub | `Quartz.AspNetCore` |
| `Quartz.Dashboard.History` | Job execution history plugin + DB schema | `Quartz` |

The history plugin is deliberately a separate package so the dashboard can function without database-backed history (in-memory-only schedulers). Developers who want execution history opt in explicitly.

### Project Layout

```
src/
├── Quartz.Dashboard/
│   ├── Quartz.Dashboard.csproj
│   ├── QuartzDashboardOptions.cs              # Configuration options
│   ├── QuartzDashboardServiceCollectionExtensions.cs  # AddQuartzDashboard()
│   ├── QuartzDashboardEndpointRouteBuilderExtensions.cs  # MapQuartzDashboard()
│   ├── Components/
│   │   ├── _Imports.razor                     # Global usings
│   │   ├── QuartzDashboardApp.razor           # Root component
│   │   ├── Layout/
│   │   │   ├── DashboardLayout.razor          # Main layout with sidebar
│   │   │   ├── NavMenu.razor                  # Navigation sidebar
│   │   │   └── SchedulerSelector.razor        # Scheduler dropdown
│   │   ├── Pages/
│   │   │   ├── Dashboard.razor                # Overview / home
│   │   │   ├── Jobs.razor                     # Job list
│   │   │   ├── JobDetail.razor                # Single job detail
│   │   │   ├── Triggers.razor                 # Trigger list
│   │   │   ├── TriggerDetail.razor            # Single trigger detail
│   │   │   ├── Calendars.razor                # Calendar list
│   │   │   ├── CalendarDetail.razor           # Calendar detail
│   │   │   ├── History.razor                  # Execution history
│   │   │   ├── CurrentlyExecuting.razor       # Running jobs
│   │   │   └── LiveLogs.razor                 # Real-time event feed
│   │   └── Shared/
│   │       ├── KeyBadge.razor                 # JobKey/TriggerKey display
│   │       ├── StatCard.razor                 # Metric card widget
│   │       ├── StateIndicator.razor           # Trigger state dot
│   │       ├── CronNextFires.razor            # Next N fire times
│   │       ├── JobDataMapEditor.razor         # Typed data map editor
│   │       ├── ConfirmDialog.razor            # Confirmation modal
│   │       ├── SearchFilter.razor             # Group/name filter
│   │       ├── Pagination.razor               # Paged list control
│   │       └── TimeAgo.razor                  # Relative time display
│   ├── Services/
│   │   ├── IQuartzApiClient.cs                # API client interface
│   │   ├── QuartzApiClient.cs                 # HttpClient-based impl
│   │   ├── SchedulerState.cs                  # Active scheduler state
│   │   └── DashboardAuthorizationService.cs   # Auth abstraction
│   ├── Hubs/
│   │   └── QuartzDashboardHub.cs              # SignalR hub
│   ├── Plugins/
│   │   └── DashboardLiveEventsPlugin.cs       # ISchedulerPlugin for SignalR
│   └── wwwroot/
│       ├── css/
│       │   └── quartz-dashboard.css           # Scoped styles
│       └── js/
│           └── quartz-dashboard.js            # Minimal JS interop (if any)
│
├── Quartz.Dashboard.History/
│   ├── Quartz.Dashboard.History.csproj
│   ├── ExecutionHistoryPlugin.cs              # ISchedulerPlugin + IJobListener
│   ├── JobHistoryDelegate.cs                  # ADO.NET data access
│   ├── JobHistoryEntry.cs                     # Data model
│   ├── HistoryEndpoints.cs                    # Extra API endpoints for history
│   ├── HistoryServiceCollectionExtensions.cs  # AddQuartzDashboardHistory()
│   └── Sql/
│       ├── tables_sqlServer.sql
│       ├── tables_postgres.sql
│       ├── tables_mysql.sql
│       ├── tables_sqlite.sql
│       └── tables_oracle.sql
│
└── Quartz.Tests.Dashboard/
    ├── Quartz.Tests.Dashboard.csproj
    ├── Services/
    │   └── QuartzApiClientTests.cs
    ├── Components/
    │   └── ... (bUnit component tests)
    └── Plugins/
        └── DashboardLiveEventsPluginTests.cs
```

### Project File — `Quartz.Dashboard.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">

  <PropertyGroup>
    <Description>Quartz.NET Blazor Server Dashboard; $(Description)</Description>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>Quartz.Dashboard</RootNamespace>
    <GenerateDocumentationFile>True</GenerateDocumentationFile>
    <Authors>Marko Lahma, Quartz.NET</Authors>
    <PackageReadmeFile>dashboard.md</PackageReadmeFile>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\Quartz.AspNetCore\Quartz.AspNetCore.csproj" />
  </ItemGroup>

  <ItemGroup>
    <SupportedPlatform Include="browser" />
  </ItemGroup>

</Project>
```

---

## 3. Embedding Strategy

### Design Principle

Follow the **Hangfire pattern** — two extension methods, zero boilerplate. The dashboard must work out-of-the-box for the common case while remaining extensible for advanced scenarios.

### Registration API

```csharp
// Program.cs — minimal setup
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddQuartz(q =>
{
    // ... existing scheduler config ...
    q.AddHttpApi();  // existing — enables REST API
});
builder.Services.AddQuartzDashboard();  // NEW — registers dashboard services

var app = builder.Build();

app.MapQuartzApi();        // existing — maps REST endpoints
app.MapQuartzDashboard();  // NEW — maps Blazor dashboard at /quartz

app.Run();
```

### Implementation — `AddQuartzDashboard()`

```csharp
namespace Quartz;

public static class QuartzDashboardServiceCollectionExtensions
{
    public static IServiceCollection AddQuartzDashboard(
        this IServiceCollection services,
        Action<QuartzDashboardOptions>? configure = null)
    {
        services.AddOptions<QuartzDashboardOptions>()
            .Validate(o => !string.IsNullOrWhiteSpace(o.DashboardPath)
                        && o.DashboardPath.StartsWith('/'),
                      "DashboardPath must start with '/'");

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddSignalR();
        services.TryAddScoped<IQuartzApiClient, QuartzApiClient>();
        services.TryAddScoped<SchedulerState>();
        services.TryAddSingleton<IDashboardAuthorizationService, DefaultDashboardAuthorizationService>();

        return services;
    }
}
```

### Implementation — `MapQuartzDashboard()`

```csharp
namespace Quartz;

public static class QuartzDashboardEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapQuartzDashboard(
        this IEndpointRouteBuilder builder,
        string path = "/quartz")
    {
        QuartzDashboardOptions options = builder.ServiceProvider
            .GetRequiredService<IOptions<QuartzDashboardOptions>>().Value;

        string dashboardPath = path.TrimEnd('/');

        // Map SignalR hub
        builder.MapHub<QuartzDashboardHub>($"{dashboardPath}/hub");

        // Map Blazor Server with sub-path isolation
        RazorComponentsEndpointConventionBuilder blazor = builder
            .MapRazorComponents<QuartzDashboardApp>()
            .AddInteractiveServerRenderMode();

        // Apply authorization if configured
        IEndpointConventionBuilder conventionBuilder = blazor;
        if (options.AuthorizationPolicy is not null)
        {
            conventionBuilder = blazor.RequireAuthorization(options.AuthorizationPolicy);
        }

        return conventionBuilder;
    }
}
```

### Static Assets

Blazor Server components include static assets (CSS/JS) via `_content/Quartz.Dashboard/`. The host app needs no additional bundling or npm steps.

---

## 4. Technology Choices

### Blazor Render Mode: **Interactive Server** (Blazor Server)

**Decision:** Use Blazor Server exclusively. See `keaton-blazor-render-mode.md` for full rationale.

**Summary:** The dashboard is an admin/intranet tool accessed by a handful of ops users, not a public-facing app. Blazor Server provides:
- **Zero download size** — no WASM runtime to ship
- **Full server-side access** — SignalR for real-time, direct HttpClient to API
- **Simpler deployment** — no CORS, no separate API host, no WASM AOT
- **Consistent with Hangfire** — proven pattern for .NET admin dashboards

### UI Framework: **Vanilla CSS + Minimal Component Library**

- Ship custom CSS (`quartz-dashboard.css`) with CSS custom properties for theming
- No dependency on Bootstrap, Tailwind, or MudBlazor — avoids conflicts with host app styles
- Use CSS scoping (`::deep` / component isolation) to prevent style leaking
- Design is utilitarian/data-dense — this is an ops dashboard, not a marketing site
- CSS custom properties enable light/dark theme support from day one

**Revisited 2026-02-16:** MudBlazor was evaluated at the project owner's request. Decision: stay with vanilla CSS. The core issue is that MudBlazor injects global styles, creates NuGet version coupling with host apps, and adds ~400KB to bundle size — all incompatible with embeddable middleware. The dashboard's UI needs (~10 pages, simple data tables, confirmation dialogs) don't justify the integration risk. See `keaton-ui-framework-decision.md` for full analysis.

### SignalR: **Built-in ASP.NET Core SignalR**

- No external dependency (ships with `Microsoft.AspNetCore.App`)
- Hub at `{dashboardPath}/hub` for live events
- Backed by `DashboardLiveEventsPlugin` (an `ISchedulerPlugin` + `ISchedulerListener` + `IJobListener` + `ITriggerListener`)

---

## 5. Authentication & Authorization

### Design Philosophy

Authentication is the **host application's responsibility**. The dashboard provides authorization hooks that integrate with ASP.NET Core's existing auth infrastructure. This mirrors how `Quartz.AspNetCore`'s HTTP API works today — the `IEndpointConventionBuilder` returned from `MapQuartzApi()` supports `.RequireAuthorization()`.

### Configuration Options

```csharp
public class QuartzDashboardOptions
{
    /// <summary>
    /// Base path for the dashboard. Default: "/quartz".
    /// </summary>
    public string DashboardPath { get; set; } = "/quartz";

    /// <summary>
    /// Authorization policy name to apply to all dashboard endpoints.
    /// When null, no authorization is required (suitable for development).
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// Custom authorization filter for fine-grained access control.
    /// Called before rendering any dashboard page.
    /// </summary>
    public IDashboardAuthorizationFilter? AuthorizationFilter { get; set; }

    /// <summary>
    /// Path to the HTTP API. Must match the path configured in AddHttpApi().
    /// Default: "/quartz-api".
    /// </summary>
    public string ApiPath { get; set; } = "/quartz-api";
}
```

### Authorization Filter Interface

```csharp
/// <summary>
/// Custom authorization filter for Quartz Dashboard pages.
/// Implement this to add fine-grained access control beyond ASP.NET Core policies.
/// </summary>
public interface IDashboardAuthorizationFilter
{
    /// <summary>
    /// Determines whether the current user is authorized to access the dashboard.
    /// </summary>
    ValueTask<bool> AuthorizeAsync(HttpContext httpContext);
}
```

### Usage Examples

**Basic — ASP.NET Core policy:**
```csharp
builder.Services.AddQuartzDashboard(options =>
{
    options.AuthorizationPolicy = "QuartzAdmin";
});

// Standard ASP.NET Core policy definition
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("QuartzAdmin", policy =>
        policy.RequireRole("Admin", "Ops"));
```

**Advanced — custom filter:**
```csharp
public class LocalRequestsOnlyFilter : IDashboardAuthorizationFilter
{
    public ValueTask<bool> AuthorizeAsync(HttpContext httpContext)
    {
        return ValueTask.FromResult(httpContext.Request.IsLocal());
    }
}

builder.Services.AddQuartzDashboard(options =>
{
    options.AuthorizationFilter = new LocalRequestsOnlyFilter();
});
```

**Endpoint-level auth (fluent):**
```csharp
app.MapQuartzDashboard()
   .RequireAuthorization("QuartzAdmin");

app.MapQuartzApi()
   .RequireAuthorization("QuartzAdmin");
```

### Read-Only Mode

For scenarios where users should view but not modify, the dashboard supports a read-only flag:

```csharp
builder.Services.AddQuartzDashboard(options =>
{
    options.ReadOnly = true;  // Hides all mutating actions (pause, resume, delete, trigger)
});
```

This is enforced both in the UI (buttons hidden) and in the API client layer (mutating calls rejected).

---

## 6. Multi-Scheduler Support

### Architecture

The existing `Quartz.AspNetCore` HTTP API already namespaces all endpoints under `/schedulers/{schedulerName}/`. The dashboard leverages this by maintaining a "current scheduler" selection in scoped state.

### Scheduler State Service

```csharp
/// <summary>
/// Scoped service tracking the user's active scheduler selection.
/// One instance per SignalR circuit (per browser tab).
/// </summary>
public class SchedulerState
{
    private string? _activeSchedulerName;

    /// <summary>
    /// Fires when the active scheduler changes.
    /// </summary>
    public event Action? OnSchedulerChanged;

    /// <summary>
    /// Currently selected scheduler name. Null until initialized.
    /// </summary>
    public string? ActiveSchedulerName
    {
        get => _activeSchedulerName;
        set
        {
            if (_activeSchedulerName != value)
            {
                _activeSchedulerName = value;
                OnSchedulerChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Cached list of available scheduler names.
    /// </summary>
    public IReadOnlyList<string> AvailableSchedulers { get; set; } = [];
}
```

### UI Flow

1. On dashboard load, `QuartzApiClient` calls `GET /quartz-api/schedulers` to list all schedulers
2. The first scheduler is selected by default (or the one from a cookie/query param)
3. A `SchedulerSelector` dropdown in the top navigation bar allows switching
4. All page components consume `SchedulerState.ActiveSchedulerName` for API calls
5. Changing the scheduler triggers `OnSchedulerChanged`, which causes all visible components to re-fetch data

### Remote Scheduler Support (Future)

For connecting to remote Quartz instances, the architecture supports multiple API base URLs:

```csharp
builder.Services.AddQuartzDashboard(options =>
{
    // Future: connect to additional remote scheduler APIs
    options.RemoteApis.Add("Production", "https://prod-server/quartz-api");
    options.RemoteApis.Add("Staging", "https://staging-server/quartz-api");
});
```

This is a future consideration — v1 focuses on in-process schedulers.

---

## 7. Page / Route Architecture

All routes are prefixed with the configured `DashboardPath` (default: `/quartz`).

| Route | Page Component | Description | API Endpoints Used |
|-------|---------------|-------------|-------------------|
| `/quartz` | `Dashboard.razor` | Overview: scheduler status, job/trigger counts, stat cards, recent history | `GET /schedulers/{name}`, `GET /jobs`, `GET /triggers`, history |
| `/quartz/jobs` | `Jobs.razor` | Filterable list of all jobs grouped by group. Actions: pause, resume, trigger, delete | `GET /jobs`, `POST /jobs/.../pause`, etc. |
| `/quartz/jobs/{group}/{name}` | `JobDetail.razor` | Job metadata, JobDataMap, associated triggers, execution history for this job | `GET /jobs/{g}/{n}`, `GET /jobs/{g}/{n}/triggers` |
| `/quartz/triggers` | `Triggers.razor` | Filterable list of all triggers. State badges, type indicators | `GET /triggers` |
| `/quartz/triggers/{group}/{name}` | `TriggerDetail.razor` | Trigger config, schedule details, next fire times, state management | `GET /triggers/{g}/{n}`, `GET .../state` |
| `/quartz/calendars` | `Calendars.razor` | List of all calendars | `GET /calendars` |
| `/quartz/calendars/{name}` | `CalendarDetail.razor` | Calendar configuration and excluded dates | `GET /calendars/{name}` |
| `/quartz/executing` | `CurrentlyExecuting.razor` | Live list of running jobs with interrupt capability | `GET /jobs/currently-executing`, `POST .../interrupt` |
| `/quartz/history` | `History.razor` | Paginated execution history with error filtering | History plugin endpoints |
| `/quartz/live` | `LiveLogs.razor` | Real-time event stream (SignalR) | SignalR hub |

### Dashboard Page Detail

The main dashboard (`/quartz`) shows:

```
┌─────────────────────────────────────────────────────────┐
│  [Scheduler: MyScheduler ▼]              [Status: ● Running]  │
├─────────────┬─────────────┬─────────────┬─────────────┤
│  Total Jobs │ Total Trig. │ Executing   │ Error Trig. │
│     42      │     67      │      3      │      1      │
├─────────────┴─────────────┴─────────────┴─────────────┤
│                                                         │
│  Recent Executions                    [View All →]     │
│  ┌─────────────────────────────────────────────────┐   │
│  │ SendEmailJob  ● Success  2s ago    12ms         │   │
│  │ CleanupJob    ● Failed   5m ago    3.2s   [!]   │   │
│  │ ReportJob     ● Success  1h ago    45ms         │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  Upcoming Fires                                        │
│  ┌─────────────────────────────────────────────────┐   │
│  │ SendEmailJob   in 30s   (every 5 min)           │   │
│  │ ReportJob      in 2h    (0 0 */2 * * ?)         │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

---

## 8. Component Architecture

### Layout System

```
QuartzDashboardApp.razor
└── DashboardLayout.razor
    ├── NavMenu.razor (sidebar)
    │   ├── SchedulerSelector.razor (dropdown)
    │   └── NavLink items (Dashboard, Jobs, Triggers, ...)
    └── @Body (page content)
```

### Reusable Components

| Component | Purpose | Props |
|-----------|---------|-------|
| `StatCard` | Metric display card | `Title`, `Value`, `Icon`, `Color` |
| `KeyBadge` | Displays `{group}.{name}` with copy | `GroupName`, `ItemName` |
| `StateIndicator` | Color dot for trigger/scheduler state | `State` (enum) |
| `CronNextFires` | Shows next N fire times for cron expression | `CronExpression`, `Count` |
| `JobDataMapEditor` | Typed editor for JobDataMap entries | `DataMap`, `ReadOnly` |
| `ConfirmDialog` | Modal confirmation for destructive actions | `Title`, `Message`, `OnConfirm` |
| `SearchFilter` | Group + name filter with debounce | `OnFilterChanged` |
| `Pagination` | Page navigation for lists | `TotalItems`, `PageSize`, `CurrentPage` |
| `TimeAgo` | Relative time display ("5m ago") | `Timestamp` |
| `TriggerTypeIcon` | Icon indicating Cron/Simple/DailyTime/CalendarInterval | `TriggerType` |
| `ErrorAlert` | Error message display with retry | `Message`, `OnRetry` |
| `LoadingSpinner` | Consistent loading indicator | — |

### Component Interaction Pattern

Components use a cascading `SchedulerState` parameter and react to scheduler changes:

```razor
@* JobList.razor *@
@inject IQuartzApiClient Api
@inject SchedulerState Scheduler

@implements IDisposable

<h2>Jobs</h2>

@if (_jobs is null)
{
    <LoadingSpinner />
}
else
{
    @foreach (var group in _jobs.GroupBy(j => j.Group))
    {
        <h3>@group.Key</h3>
        @foreach (var job in group)
        {
            <KeyBadge GroupName="@job.Group" ItemName="@job.Name" />
        }
    }
}

@code {
    private IReadOnlyList<JobKeyDto>? _jobs;

    protected override async Task OnInitializedAsync()
    {
        Scheduler.OnSchedulerChanged += OnSchedulerChanged;
        await LoadJobsAsync();
    }

    private async void OnSchedulerChanged()
    {
        await LoadJobsAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadJobsAsync()
    {
        if (Scheduler.ActiveSchedulerName is not null)
        {
            _jobs = await Api.GetJobKeysAsync(Scheduler.ActiveSchedulerName);
        }
    }

    public void Dispose()
    {
        Scheduler.OnSchedulerChanged -= OnSchedulerChanged;
    }
}
```

---

## 9. API Client Layer

### Design

The `IQuartzApiClient` is the single abstraction the Blazor components use to talk to the scheduler. It wraps `HttpClient` calls to the existing `Quartz.AspNetCore` REST API. Because the dashboard runs as Blazor Server (same process), the HttpClient calls are in-process via the ASP.NET Core test server pipeline — no actual HTTP over the wire.

### Interface

```csharp
/// <summary>
/// Client for the Quartz.NET HTTP API. Consumed by dashboard components.
/// </summary>
public interface IQuartzApiClient
{
    // Schedulers
    ValueTask<IReadOnlyList<SchedulerHeaderDto>> GetSchedulersAsync();
    ValueTask<SchedulerDetailDto> GetSchedulerAsync(string schedulerName);
    ValueTask StartSchedulerAsync(string schedulerName);
    ValueTask StandbySchedulerAsync(string schedulerName);
    ValueTask ShutdownSchedulerAsync(string schedulerName);
    ValueTask PauseAllAsync(string schedulerName);
    ValueTask ResumeAllAsync(string schedulerName);

    // Jobs
    ValueTask<IReadOnlyList<JobKeyDto>> GetJobKeysAsync(string schedulerName, string? groupFilter = null);
    ValueTask<JobDetailDto> GetJobAsync(string schedulerName, string group, string name);
    ValueTask<IReadOnlyList<TriggerHeaderDto>> GetJobTriggersAsync(string schedulerName, string group, string name);
    ValueTask<IReadOnlyList<CurrentlyExecutingJobDto>> GetCurrentlyExecutingJobsAsync(string schedulerName);
    ValueTask PauseJobAsync(string schedulerName, string group, string name);
    ValueTask ResumeJobAsync(string schedulerName, string group, string name);
    ValueTask TriggerJobAsync(string schedulerName, string group, string name);
    ValueTask InterruptJobAsync(string schedulerName, string group, string name);
    ValueTask DeleteJobAsync(string schedulerName, string group, string name);
    ValueTask AddJobAsync(string schedulerName, AddJobRequest request);

    // Triggers
    ValueTask<IReadOnlyList<TriggerKeyDto>> GetTriggerKeysAsync(string schedulerName, string? groupFilter = null);
    ValueTask<TriggerDetailDto> GetTriggerAsync(string schedulerName, string group, string name);
    ValueTask<string> GetTriggerStateAsync(string schedulerName, string group, string name);
    ValueTask PauseTriggerAsync(string schedulerName, string group, string name);
    ValueTask ResumeTriggerAsync(string schedulerName, string group, string name);
    ValueTask ResetTriggerFromErrorStateAsync(string schedulerName, string group, string name);
    ValueTask ScheduleJobAsync(string schedulerName, ScheduleJobRequest request);
    ValueTask UnscheduleJobAsync(string schedulerName, string group, string name);
    ValueTask RescheduleJobAsync(string schedulerName, string group, string name, RescheduleRequest request);

    // Calendars
    ValueTask<IReadOnlyList<string>> GetCalendarNamesAsync(string schedulerName);
    ValueTask<CalendarDetailDto> GetCalendarAsync(string schedulerName, string calendarName);
    ValueTask AddCalendarAsync(string schedulerName, AddCalendarRequest request);
    ValueTask DeleteCalendarAsync(string schedulerName, string calendarName);

    // History (optional — only available if Quartz.Dashboard.History is configured)
    ValueTask<JobHistoryPageDto?> GetHistoryAsync(string schedulerName, int page = 1, int pageSize = 25);
}
```

### Implementation Strategy

```csharp
public class QuartzApiClient : IQuartzApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<QuartzDashboardOptions> _options;

    public QuartzApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<QuartzDashboardOptions> options)
    {
        _httpClient = httpClientFactory.CreateClient("QuartzDashboard");
        _options = options;
    }

    public async ValueTask<IReadOnlyList<SchedulerHeaderDto>> GetSchedulersAsync()
    {
        string path = $"{_options.Value.ApiPath.TrimEnd('/')}/schedulers";
        IReadOnlyList<SchedulerHeaderDto>? result =
            await _httpClient.GetFromJsonAsync<IReadOnlyList<SchedulerHeaderDto>>(path);
        return result ?? [];
    }

    // ... additional methods follow the same pattern
}
```

The `HttpClient` is registered via `IHttpClientFactory` with the host's base address, so calls stay in-process:

```csharp
// In AddQuartzDashboard()
services.AddHttpClient("QuartzDashboard", (sp, client) =>
{
    // Base address is set at request time via IHttpContextAccessor
    // to handle reverse proxies and path bases correctly
});
```

---

## 10. Real-time Features

### SignalR Hub

```csharp
namespace Quartz.Dashboard.Hubs;

/// <summary>
/// SignalR hub for real-time dashboard updates.
/// Clients join scheduler-specific groups to receive targeted events.
/// </summary>
public class QuartzDashboardHub : Hub
{
    /// <summary>
    /// Subscribe to events for a specific scheduler.
    /// </summary>
    public async Task JoinScheduler(string schedulerName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, schedulerName);
    }

    /// <summary>
    /// Unsubscribe from scheduler events.
    /// </summary>
    public async Task LeaveScheduler(string schedulerName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, schedulerName);
    }
}
```

### Hub Event Contract

```csharp
/// <summary>
/// Events sent from server to connected dashboard clients.
/// </summary>
public interface IQuartzDashboardHubClient
{
    Task JobExecuting(JobEventDto jobEvent);
    Task JobExecuted(JobExecutionResultDto result);
    Task TriggerFired(TriggerEventDto triggerEvent);
    Task TriggerCompleted(TriggerEventDto triggerEvent);
    Task TriggerMisfired(TriggerEventDto triggerEvent);
    Task TriggerPaused(TriggerKeyDto triggerKey);
    Task TriggerResumed(TriggerKeyDto triggerKey);
    Task JobPaused(JobKeyDto jobKey);
    Task JobResumed(JobKeyDto jobKey);
    Task SchedulerStateChanged(SchedulerStateDto state);
    Task SchedulerError(SchedulerErrorDto error);
}
```

### Live Events Plugin

The `DashboardLiveEventsPlugin` is an `ISchedulerPlugin` registered automatically when the dashboard is configured. It implements `IJobListener`, `ITriggerListener`, and `ISchedulerListener` to broadcast events through SignalR.

```csharp
namespace Quartz.Dashboard.Plugins;

/// <summary>
/// Scheduler plugin that broadcasts scheduler events to connected dashboard clients.
/// Registered automatically by AddQuartzDashboard().
/// </summary>
public class DashboardLiveEventsPlugin : ISchedulerPlugin, IJobListener, ITriggerListener, ISchedulerListener
{
    private IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>? _hubContext;
    private string _schedulerName = "";

    public string Name { get; private set; } = "QuartzDashboardLiveEvents";

    public ValueTask Initialize(
        string pluginName,
        IScheduler scheduler,
        CancellationToken cancellationToken)
    {
        Name = pluginName;
        _schedulerName = scheduler.SchedulerName;

        // Resolve hub context from the application's service provider
        _hubContext = scheduler.Context.Get<IServiceProvider>("ServiceProvider")
            ?.GetService<IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>>();

        scheduler.ListenerManager.AddJobListener(this, EverythingMatcher<JobKey>.AllJobs());
        scheduler.ListenerManager.AddTriggerListener(this, EverythingMatcher<TriggerKey>.AllTriggers());
        scheduler.ListenerManager.AddSchedulerListener(this);

        return default;
    }

    public async ValueTask JobToBeExecuted(
        IJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (_hubContext is not null)
        {
            JobEventDto dto = new()
            {
                JobKey = new(context.JobDetail.Key.Group, context.JobDetail.Key.Name),
                TriggerKey = new(context.Trigger.Key.Group, context.Trigger.Key.Name),
                FireTimeUtc = context.FireTimeUtc,
                FireInstanceId = context.FireInstanceId
            };
            await _hubContext.Clients.Group(_schedulerName).JobExecuting(dto);
        }
    }

    // ... remaining listener methods follow same pattern
}
```

### Client-Side Integration

Dashboard pages connect to SignalR on initialization:

```razor
@* LiveLogs.razor *@
@inject NavigationManager Navigation
@inject SchedulerState Scheduler
@implements IAsyncDisposable

<h2>Live Events</h2>
<div class="event-stream">
    @foreach (var evt in _events)
    {
        <div class="event-item event-@evt.Type.ToLowerInvariant()">
            <TimeAgo Timestamp="@evt.Timestamp" />
            <span>@evt.Description</span>
        </div>
    }
</div>

@code {
    private HubConnection? _hubConnection;
    private readonly List<LiveEvent> _events = new(capacity: 100);

    protected override async Task OnInitializedAsync()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(Navigation.ToAbsoluteUri("quartz/hub"))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<JobEventDto>("JobExecuting", OnJobExecuting);
        _hubConnection.On<JobExecutionResultDto>("JobExecuted", OnJobExecuted);
        _hubConnection.On<TriggerEventDto>("TriggerFired", OnTriggerFired);

        await _hubConnection.StartAsync();

        if (Scheduler.ActiveSchedulerName is not null)
        {
            await _hubConnection.InvokeAsync("JoinScheduler", Scheduler.ActiveSchedulerName);
        }
    }

    private async Task OnJobExecuting(JobEventDto evt)
    {
        _events.Insert(0, new LiveEvent("JobExecuting", $"Job {evt.JobKey} started"));
        if (_events.Count > 100) _events.RemoveAt(100);
        await InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
```

---

## 11. Job Execution History

### Separate Package: `Quartz.Dashboard.History`

History is opt-in because:
1. Not all schedulers use persistent stores (RAMJobStore has no database)
2. The history table requires DDL changes — must be explicit
3. Keeps the base dashboard lightweight

### Registration

```csharp
builder.Services.AddQuartz(q =>
{
    q.AddHttpApi();
    q.AddDashboardHistory(options =>  // NEW — from Quartz.Dashboard.History
    {
        options.DataSource = "default";
        options.TablePrefix = "QRTZ_";
        // Uses the same ADO.NET connection as the job store
    });
});
```

### Database Schema

Enhanced from the existing `Quartz.Web` schema to support pagination and filtering:

```sql
CREATE TABLE {0}JOB_HISTORY (
    ENTRY_ID        BIGINT          NOT NULL PRIMARY KEY IDENTITY,
    SCHED_NAME      NVARCHAR(120)   NOT NULL,
    INSTANCE_NAME   NVARCHAR(200)   NOT NULL,
    JOB_NAME        NVARCHAR(150)   NOT NULL,
    JOB_GROUP       NVARCHAR(150)   NOT NULL,
    TRIGGER_NAME    NVARCHAR(150)   NOT NULL,
    TRIGGER_GROUP   NVARCHAR(150)   NOT NULL,
    FIRED_TIME      BIGINT          NOT NULL,
    SCHED_TIME      BIGINT          NOT NULL,
    RUN_TIME        BIGINT          NOT NULL,
    ERROR           BIT             NOT NULL,
    ERROR_MESSAGE   NVARCHAR(2500)  NULL,
    VETOED          BIT             NOT NULL DEFAULT 0
);

CREATE INDEX IDX_{0}JH_SCHED     ON {0}JOB_HISTORY(SCHED_NAME);
CREATE INDEX IDX_{0}JH_JOB       ON {0}JOB_HISTORY(SCHED_NAME, JOB_NAME, JOB_GROUP);
CREATE INDEX IDX_{0}JH_TRIGGER   ON {0}JOB_HISTORY(SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP);
CREATE INDEX IDX_{0}JH_FIRED     ON {0}JOB_HISTORY(SCHED_NAME, FIRED_TIME DESC);
CREATE INDEX IDX_{0}JH_ERROR     ON {0}JOB_HISTORY(SCHED_NAME, ERROR);
```

Scripts provided for: SQL Server, PostgreSQL, MySQL, SQLite, Oracle.

### History Plugin

Evolves the existing `DatabaseExecutionHistoryPlugin` from `Quartz.Web` into a proper package:

```csharp
public class ExecutionHistoryPlugin : ISchedulerPlugin, IJobListener
{
    private readonly IServiceProvider _serviceProvider;

    public string Name { get; private set; } = "QuartzExecutionHistory";
    public string DataSource { get; set; } = "default";
    public string TablePrefix { get; set; } = "QRTZ_";
    public string DriverDelegateType { get; set; } = typeof(StdAdoDelegate).AssemblyQualifiedName!;

    public ValueTask Initialize(
        string pluginName,
        IScheduler scheduler,
        CancellationToken cancellationToken)
    {
        Name = pluginName;
        scheduler.ListenerManager.AddJobListener(this, EverythingMatcher<JobKey>.AllJobs());
        return default;
    }

    public ValueTask JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken)
    {
        // Insert history entry via JobHistoryDelegate
        return _delegate.InsertJobHistoryEntryAsync(context, jobException, cancellationToken);
    }

    // ... lifecycle methods
}
```

### History API Endpoints

The history package adds its own minimal API endpoints:

```
GET  /quartz-api/schedulers/{name}/history
     ?page=1&pageSize=25&jobGroup=&jobName=&errorsOnly=false
GET  /quartz-api/schedulers/{name}/history/stats
     → { totalExecutions, totalErrors, avgRunTimeMs, last24hExecutions }
```

---

## 12. Configuration & Setup

### Minimal Setup (2 lines)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(s => { /* ... */ });
    q.AddHttpApi();                    // REST API
});
builder.Services.AddQuartzHostedService();
builder.Services.AddQuartzDashboard(); // Dashboard

var app = builder.Build();

app.MapQuartzApi();        // REST at /quartz-api
app.MapQuartzDashboard();  // UI at /quartz

app.Run();
```

### Full Setup (with all options)

```csharp
var builder = WebApplication.CreateBuilder(args);

// Authentication (app's responsibility)
builder.Services.AddAuthentication().AddCookie();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("QuartzAdmin", p => p.RequireRole("Admin"));

builder.Services.AddQuartz(q =>
{
    q.SchedulerName = "ProductionScheduler";
    q.UsePersistentStore(s =>
    {
        s.UsePostgres();
        s.UseSystemTextJsonSerializer();
    });

    q.AddHttpApi(api =>
    {
        api.ApiPath = "/quartz-api";
    });

    // Optional: execution history
    q.AddDashboardHistory(h =>
    {
        h.DataSource = "default";
        h.TablePrefix = "QRTZ_";
    });
});

builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});

builder.Services.AddQuartzDashboard(options =>
{
    options.DashboardPath = "/quartz";
    options.AuthorizationPolicy = "QuartzAdmin";
    options.ReadOnly = false;
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapQuartzApi()
   .RequireAuthorization("QuartzAdmin");

app.MapQuartzDashboard()
   .RequireAuthorization("QuartzAdmin");

app.Run();
```

### Configuration via `appsettings.json`

```json
{
  "Quartz": {
    "Dashboard": {
      "DashboardPath": "/quartz",
      "AuthorizationPolicy": "QuartzAdmin",
      "ReadOnly": false
    }
  }
}
```

```csharp
builder.Services.AddQuartzDashboard();
builder.Services.Configure<QuartzDashboardOptions>(
    builder.Configuration.GetSection("Quartz:Dashboard"));
```

---

## 13. Future Considerations

### Cluster View (v2)

For clustered schedulers (multiple instances sharing a database), add a cluster overview page:

- Show all scheduler instances (from `QRTZ_SCHEDULER_STATE` table)
- Show which instance owns which triggers
- Last checkin times, instance health
- Consolidated view of jobs across all nodes

This requires direct database access (not available via current REST API) — will need additional endpoints in `Quartz.AspNetCore` or a separate `IClusterInfoProvider` abstraction.

### Theming (v2)

CSS custom properties enable easy theming:

```css
:root {
    --quartz-bg-primary: #ffffff;
    --quartz-bg-secondary: #f8f9fa;
    --quartz-text-primary: #212529;
    --quartz-accent: #0d6efd;
    --quartz-success: #198754;
    --quartz-danger: #dc3545;
    --quartz-warning: #ffc107;
}

@media (prefers-color-scheme: dark) {
    :root {
        --quartz-bg-primary: #1a1a2e;
        --quartz-bg-secondary: #16213e;
        --quartz-text-primary: #e0e0e0;
    }
}
```

Users can override with their own CSS or set `options.Theme = QuartzDashboardTheme.Dark`.

### Extensibility (v2+)

- **Custom pages:** Allow registering additional Razor components as dashboard pages
- **Widget system:** Custom stat cards on the dashboard overview
- **Webhook integration:** Configure notifications for job failures
- **Export:** CSV/JSON export of history data

### Performance Considerations

- **Virtualization:** Use `<Virtualize>` for large job/trigger lists (100+ items)
- **Caching:** Short-lived (5s) cache on scheduler metadata to avoid hammering the API
- **SignalR backpressure:** Limit event broadcast rate under heavy load (batch updates)
- **History retention:** Configurable auto-purge of old history entries

### Accessibility

- WCAG 2.1 AA compliance
- Keyboard navigation for all actions
- ARIA labels on status indicators and interactive elements
- High contrast mode support via CSS custom properties

---

## Appendix A: Package Dependency Graph

```
Quartz.Dashboard
├── Quartz.AspNetCore
│   └── Quartz.HttpClient
│       └── Quartz
└── Microsoft.AspNetCore.App (framework reference)

Quartz.Dashboard.History
└── Quartz (direct dependency only)
```

## Appendix B: Naming Conventions

| Item | Convention | Example |
|------|-----------|---------|
| Namespace | `Quartz.Dashboard.*` | `Quartz.Dashboard.Services` |
| Extension methods | `QuartzDashboard` prefix | `AddQuartzDashboard()` |
| Options class | `QuartzDashboard` prefix | `QuartzDashboardOptions` |
| SignalR hub | Descriptive | `QuartzDashboardHub` |
| Razor components | PascalCase, descriptive | `JobDetail.razor` |
| CSS classes | `qz-` prefix (avoid conflicts) | `qz-stat-card`, `qz-nav-menu` |
| JS functions | `quartzDashboard.` namespace | `quartzDashboard.copyToClipboard()` |

## Appendix C: Feature Parity Matrix

| Feature | Hangfire | Quartzmin | CrystalQuartz | Quartz Dashboard (v1) |
|---------|----------|-----------|---------------|----------------------|
| Embeddable middleware | ✅ | ✅ | ✅ | ✅ |
| Job list & details | ✅ | ✅ | ✅ | ✅ |
| Trigger management | N/A | ✅ | ✅ | ✅ |
| Calendar management | N/A | ✅ | ❌ | ✅ |
| Trigger now | ✅ | ✅ | ✅ | ✅ |
| Pause/Resume | ✅ | ✅ | ✅ | ✅ |
| Delete jobs | ✅ | ✅ | ❌ | ✅ |
| Execution history | ✅ | ✅ | ❌ | ✅ (opt-in) |
| Live events | ❌ | ❌ | ❌ | ✅ (SignalR) |
| Authentication | ✅ | ❌ | ❌ | ✅ |
| Multi-scheduler | N/A | ✅ | ✅ | ✅ |
| Currently executing | ❌ | ❌ | ❌ | ✅ |
| Interrupt jobs | N/A | ❌ | ❌ | ✅ |
| Error state reset | N/A | ❌ | ❌ | ✅ |
| Add/modify jobs | ❌ | ✅ | ❌ | ✅ |
| Typed JobDataMap editor | N/A | ✅ | ❌ | ✅ |
| Cron next fires | N/A | ✅ | ✅ | ✅ |
| Dark mode | ✅ | ❌ | ❌ | ✅ (v2) |
| Cluster view | ❌ | ❌ | ❌ | Planned (v2) |
