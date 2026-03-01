# Decisions

> Team decisions that all agents must respect. Managed by Scribe.

---

## 2026-02-16: Model preferences
**By:** Marko Lahma (via Copilot)
**What:** Use `claude-opus-4.6` for complex work and large designs. Use `gpt-5.3-codex` for general implementation.
**Why:** User directive — captured for team memory

---

## 2026-02-16: Tolerant DTO rendering in dashboard pages
**By:** Fenster  

### What
Dashboard page components render API DTOs using `DisplayValueHelper` path accessors (including nested paths like `Key.Group`) instead of hard-coding a single DTO shape.

### Why
The dashboard consumes HTTP API contracts that can differ slightly between endpoints and may evolve while the UI is being developed in parallel. Tolerant access keeps pages resilient to minor DTO shape differences without blocking UI feature delivery.

### Impact
- List/detail pages can display both flat (`Group`/`Name`) and nested (`Key.Group`/`Key.Name`) key structures.
- Trigger/job execution pages remain functional if API payloads are represented as concrete DTOs or `JsonElement`-backed objects.
- If DTO contracts are finalized later, the helper can be simplified centrally without rewriting each page.

---

## 2026-02-16: Dashboard DTO boundary for HTTP API contracts
**By:** Hockney

### Decision
`Quartz.Dashboard` introduces its own public DTOs for dashboard-facing API client contracts instead of directly using `Quartz.HttpClient.HttpApiContract` types.

### Why
The `Quartz.HttpClient.HttpApiContract` DTOs are currently `internal` and not visible to `Quartz.Dashboard`. Keeping dashboard DTOs local allows scaffolding to compile immediately without changing visibility in `Quartz.HttpClient`.

### Impact
- Dashboard service interfaces are unblocked and can evolve independently.
- If shared contract types are desired later, we should either make selected DTOs public in `Quartz.HttpClient` or add `InternalsVisibleTo` for `Quartz.Dashboard`.

---

## 2026-02-16: Blazor render mode for Quartz.NET Dashboard
**By:** Keaton (Lead Architect)  
**Status:** DECIDED

### Decision
**Use Blazor Server (Interactive Server render mode) exclusively.**

Do not use Blazor WebAssembly or Blazor Auto (Server + WASM hybrid).

### Context
The Quartz.NET Dashboard is an administrative/operations tool for managing job schedulers. It will be embedded into existing ASP.NET Core applications as middleware.

### Key characteristics of the dashboard
- **Admin/intranet tool** — accessed by a small number of ops/dev users, not the general public
- **Real-time updates** — must show live job execution events via SignalR
- **Embedded middleware** — runs inside the host application, not as a standalone SPA
- **Data-heavy** — displays scheduler metadata, job lists, trigger states, execution history
- **Mutating operations** — pause, resume, trigger, delete jobs

### Rationale
1. **"Always-connected" requirement is not a trade-off** — The dashboard already requires a server connection to function. SignalR for live events means a persistent connection is needed regardless of render mode.
2. **Concurrent user count is low** — Admin dashboards typically have 1–10 concurrent users. At ~250KB per Blazor Server circuit, even 100 simultaneous users would use only 25MB of server memory.
3. **No offline requirement** — Nobody needs to manage a scheduler offline.
4. **Proven pattern** — Hangfire Dashboard uses server-rendered HTML with SignalR for updates. This is the established pattern for .NET admin dashboards.

### Why WASM is actively harmful here
1. **Packaging complexity** — Blazor WASM requires separate project, hosting config, and WASM-specific publishing. This triples complexity for NuGet distribution.
2. **CORS configuration burden** — WASM runs in the browser and requires CORS setup. Blazor Server avoids this entirely since HttpClient calls are server-side.
3. **Duplicate SignalR** — With WASM, we'd need a separate SignalR connection just for live events — two persistent connections instead of one.
4. **Assembly trimming risks** — WASM requires aggressive trimming. Quartz.NET's serialization and reflection-heavy patterns are fragile under trimming.
5. **No benefit for admin UIs** — WASM's advantages (offline, CDN-hostable, reduced server load) are irrelevant for an admin dashboard used by a handful of internal users.

### Consequences
**Positive:**
- Simpler project structure (one Razor class library)
- No CORS configuration needed
- SignalR connection shared between Blazor infrastructure and live events
- Fast startup (no WASM download)
- Full .NET API surface available

**Negative:**
- Requires persistent WebSocket connection (standard for admin dashboards)
- Minor server memory per connected user (~250KB)
- UI latency on high-latency networks (typically not an issue for intranet tools)

---

## 2026-02-16: UI framework decision
**By:** Keaton

### Decision
**Use vanilla CSS with hand-built components. Do not adopt MudBlazor or any third-party component library.**

### Rationale
We are building embeddable middleware, not a standalone app. This changes everything.

**Global CSS pollution:** MudBlazor injects global styles that target base HTML elements. If the host app has its own styles — and it will — MudBlazor's globals will fight them. There is no "scoped MudBlazor" option. This is a dealbreaker for middleware.

**Version coupling:** If the host app already uses MudBlazor 6 and we ship with MudBlazor 7, NuGet can't resolve two versions. The host developer is forced to upgrade or downgrade — we've created a dependency conflict.

**Bundle size:** MudBlazor's CSS is ~300KB minified, JS interop another ~100KB. Our custom CSS will be under 20KB.

**Transitive dependencies:** Every transitive dependency is a potential conflict with the host app's dependency graph. Quartz.NET's core library is famously dependency-light — the dashboard should maintain that discipline.

**Loss of visual control:** MudBlazor components have opinions about spacing, typography, and animation. An ops dashboard should be dense and fast-loading. MudBlazor's Material Design is designed for consumer apps.

### What custom CSS actually costs
- **Data table with sort/filter/pagination:** ~2 days of work. Showing 20-50 rows doesn't require virtualized scrolling or column reordering.
- **Confirmation dialog:** Half a day. HTML has native modal dialog support.
- **Form inputs:** We barely have forms. JobDataMap editor is a key-value list.
- **Theming:** CSS custom properties + `prefers-color-scheme` media query. One afternoon.
- **Total estimated UI component work:** **3-5 days**

### What we should do instead
1. **Document our component API clearly** — every shared component gets a usage example in its doc comment.
2. **Keep components simple** — if a component needs more than 100 lines of markup, it's doing too much.
3. **Establish visual patterns early** — build the data table and stat card first.
4. **Use CSS custom properties religiously** — no raw color values or magic numbers.

---

## 2026-02-16: CSS design system and layout components delivered
**By:** McManus

### What
Created the complete CSS design system (`quartz-dashboard.css`) and all layout Blazor components (`DashboardLayout.razor`, `NavMenu.razor`, `SchedulerSelector.razor`).

### Design tokens
All visual values are CSS custom properties under `:root`. Dark mode overrides via `@media (prefers-color-scheme: dark)`. Token categories: colors, spacing (4px increments), typography (system stack + monospace), border-radius, shadows, transitions, layout dimensions.

### CSS class inventory (all `qz-` prefixed)
**Layout:** `qz-dashboard`, `qz-layout`, `qz-sidebar`, `qz-main`, `qz-header`, `qz-content`

**Navigation:** `qz-nav-item`, `qz-nav-link`, `qz-nav-active`, `qz-nav-icon`, `qz-sidebar-brand`

**Data display:** `qz-stat-card`, `qz-badge`, `qz-state-dot`, `qz-tag`, `qz-time-ago`, `qz-event-item`

**Tables:** `qz-table`, `qz-table-header`, `qz-table-row`, `qz-table-cell`, `qz-pagination`

**Interactive:** `qz-btn`, `qz-btn-primary`, `qz-btn-danger`, `qz-btn-ghost`, `qz-dialog`, `qz-dialog-backdrop`

**Form:** `qz-form-group`, `qz-input`, `qz-select`, `qz-search`

**Feedback:** `qz-alert`, `qz-alert-error`, `qz-alert-success`, `qz-spinner`, `qz-empty`

**Utility:** `qz-tooltip`, `qz-mono`, `qz-truncate`, `qz-sr-only`

### Impact
- Fenster (page components): Use documented CSS classes. State dots: `.qz-state-dot .qz-state-{running|paused|error|complete|standby|shutdown|waiting|blocked|normal}`.
- Hockney (API layer): Expand `IQuartzApiClient` with full method set from architecture doc. `SchedulerState` uses `EventHandler<EventArgs>` per Meziantou analyzer.
- Verbal (tests): CSS file is ~36KB. All interactive elements have `:focus-visible` states. Contrast ratios target WCAG 2.1 AA.

---

## 2026-03-01: No code change due to non-repro unit failures
**By:** Hockney

### What
Ran `dotnet test src\Quartz.Tests.Unit\Quartz.Tests.Unit.csproj` twice (second run with `--no-build`) and both runs passed with 0 failures, so no backend/core code changes were made.

### Why
Could not reproduce a failing unit test state in this environment; any code edit without a failing signal would be speculative and risky.

---

## 2026-03-01: Stabilize AspNetCore test content root after .slnx migration
**By:** Hockney

### Context
`Quartz.Tests.AspNetCore` functional tests started failing with `Solution root could not be located` after moving from `.sln` to `.slnx`.

### Decision
Set `ASPNETCORE_TEST_CONTENTROOT_QUARTZ_TESTS_ASPNETCORE` in test setup before creating `WebApplicationFactory<Program>`, using a runtime-resolved path to `src\Quartz.Tests.AspNetCore`.

### Why
`WebApplicationFactory` falls back to searching for `*.sln` when no explicit content root is configured. This makes tests independent from solution-file format and avoids coupling to `.sln` presence.

---

## 2026-03-01: QA Finding — .slnx breaks default WebApplicationFactory content-root discovery
**By:** Verbal

### What was validated
1. Baseline run: `dotnet test src\Quartz.Tests.AspNetCore\Quartz.Tests.AspNetCore.csproj` failed (51/51).
2. Failure mode: all tests fail in `WebApiTest.OneTimeSetUp` with `InvalidOperationException`.
3. Candidate fix tested: `WithWebHostBuilder(builder => builder.UseSolutionRelativeContentRoot("src\\Quartz.Tests.AspNetCore", solutionName: "Quartz.slnx"))`.
4. Result: still failing (1/1 impacted rerun and 51/51 full project rerun).

### Team-relevant conclusion
The validated candidate fix is **insufficient**. `WebApplicationFactory` default `SetContentRoot` still runs first and throws before the custom builder override can recover.

---

## 2026-03-01: Treat CS2012 lock as environment issue
**By:** Verbal

### What
When `dotnet test src\Quartz.Tests.Unit\Quartz.Tests.Unit.csproj` fails with CS2012 due to `Quartz.Tests.Unit.dll` locked by `VBCSCompiler`, treat it as an environment/process-lock issue, not a product regression.

### Why
A rerun after stopping the specific locking PID and using `/p:UseSharedCompilation=false -m:1` completed with 0 failed tests (1280 total).

### Impact
QA triage should first clear compiler locks before escalating as failing unit tests.
