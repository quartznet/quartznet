# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture & scope | Keaton | System design, API contracts, project structure decisions |
| UI/UX design & layout | McManus | Component design, responsive layout, dashboard wireframes, visual hierarchy |
| Blazor components & pages | Fenster | Blazor pages, Razor components, client-side integration, SignalR |
| Backend APIs & services | Hockney | .NET API endpoints, services, data layer, Quartz.NET integration |
| Testing & quality | Verbal | Unit tests, integration tests, edge cases, test coverage |
| Code review | Keaton | Review PRs, check quality, suggest improvements |
| Scope & priorities | Keaton | What to build next, trade-offs, decisions |
| Session logging | Scribe | Automatic — never needs routing |

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
