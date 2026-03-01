# ADR: Blazor Render Mode for Quartz.NET Dashboard

**Author:** Keaton (Lead Architect)  
**Date:** 2025-02-16  
**Status:** PROPOSED  
**Related:** `keaton-dashboard-architecture.md`

---

## Decision

**Use Blazor Server (Interactive Server render mode) exclusively.**

Do not use Blazor WebAssembly or Blazor Auto (Server + WASM hybrid).

---

## Context

The Quartz.NET Dashboard is an administrative/operations tool for managing job schedulers. It will be embedded into existing ASP.NET Core applications as middleware. We need to choose a Blazor render mode: Server, WebAssembly, or Auto.

### Key characteristics of the dashboard

- **Admin/intranet tool** — accessed by a small number of ops/dev users, not the general public
- **Real-time updates** — must show live job execution events via SignalR
- **Embedded middleware** — runs inside the host application, not as a standalone SPA
- **Data-heavy** — displays scheduler metadata, job lists, trigger states, execution history
- **Mutating operations** — pause, resume, trigger, delete jobs

---

## Options Considered

### Option 1: Blazor Server ✅ CHOSEN

The UI runs entirely on the server. A persistent SignalR connection streams DOM diffs to the browser.

### Option 2: Blazor WebAssembly ❌ REJECTED

The UI runs in the browser via a .NET WASM runtime.

### Option 3: Blazor Auto (Server + WASM) ❌ REJECTED

First render uses Server mode, then transitions to WASM after the runtime downloads.

---

## Rationale

### Why Blazor Server wins for this use case

| Factor | Server | WASM | Auto |
|--------|--------|------|------|
| **Zero client download** | ✅ No WASM runtime (~10MB) | ❌ Large initial download | ⚠️ Deferred but still downloads |
| **SignalR already required** | ✅ Built-in transport | ❌ Would need separate connection | ⚠️ Duplicate connections |
| **No CORS complexity** | ✅ Same origin, in-process | ❌ Must configure CORS for API calls | ❌ Must configure CORS for WASM phase |
| **Startup latency** | ✅ <100ms (just SignalR handshake) | ❌ Seconds (download + init WASM) | ⚠️ Fast first load, slow transition |
| **Packaging simplicity** | ✅ Single Razor class library | ❌ Separate WASM project + hosting | ❌ Two projects + coordination |
| **Offline support** | ❌ Requires connection | ✅ Can work offline | ⚠️ Partial | 
| **Server memory per user** | ⚠️ ~250KB per circuit | ✅ No server state | ⚠️ Both at once during transition |

### Why the trade-offs are acceptable

1. **"Always-connected" requirement is not a trade-off** — The dashboard already requires a server connection to function (it manages live schedulers). An admin dashboard with no server is useless. SignalR for live events means a persistent connection is needed regardless of render mode.

2. **Concurrent user count is low** — Admin dashboards typically have 1–10 concurrent users. At ~250KB per Blazor Server circuit, even 100 simultaneous users would use only 25MB of server memory. This is negligible.

3. **No offline requirement** — Nobody needs to manage a scheduler offline. If the server is down, there's nothing to manage.

4. **Proven pattern** — Hangfire Dashboard uses server-rendered HTML with SignalR for updates. This is the established pattern for .NET admin dashboards. Blazor Server is the natural evolution of this pattern.

### Why WASM is actively harmful here

1. **Packaging complexity** — Blazor WASM requires a separate project with `Microsoft.NET.Sdk.BlazorWebAssembly`, separate hosting configuration, and WASM-specific asset publishing. This triples the packaging complexity for the NuGet package.

2. **CORS configuration burden** — WASM runs in the browser and makes HTTP calls to the API. If the API and dashboard are at different paths (or behind a reverse proxy), CORS must be configured. Blazor Server avoids this entirely since HttpClient calls are server-side.

3. **Duplicate SignalR** — Blazor Server already uses SignalR for component updates. Live events also use SignalR. With WASM, we'd need a *separate* SignalR connection just for live events — two persistent connections instead of sharing one.

4. **Assembly trimming risks** — WASM requires aggressive trimming to reduce download size. Quartz.NET's serialization and reflection-heavy patterns are fragile under trimming.

5. **No benefit for admin UIs** — WASM's advantages (offline, CDN-hostable, reduced server load) are irrelevant for an admin dashboard used by a handful of internal users.

---

## Consequences

### Positive
- Simpler project structure (one Razor class library)
- No CORS configuration needed
- SignalR connection shared between Blazor infrastructure and live events
- Fast startup (no WASM download)
- Full .NET API surface available (no browser sandbox limitations)

### Negative
- Requires persistent WebSocket connection (standard for admin dashboards)
- Minor server memory per connected user (~250KB)
- UI latency on high-latency networks (typically not an issue for intranet tools)
- Cannot be hosted as a static site (not a requirement)

### Neutral
- `.AddInteractiveServerComponents()` and `.AddInteractiveServerRenderMode()` are required in the host app's service registration — the `AddQuartzDashboard()` extension method handles this automatically.

---

## References

- [Blazor render modes documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes)
- [Hangfire Dashboard architecture](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html) — server-rendered, established pattern
- Quartz.NET GitHub Issue #251 — 10 years of community requests, no mention of offline or static hosting needs
