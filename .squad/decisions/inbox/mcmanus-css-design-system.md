### 2026-02-16: CSS design system and layout components delivered

**By:** McManus
**What:** Created the complete CSS design system (`quartz-dashboard.css`) and all layout Blazor components (`DashboardLayout.razor`, `NavMenu.razor`, `SchedulerSelector.razor`).
**Why:** These are the foundation every page and shared component builds on.

#### Design tokens

All visual values are CSS custom properties under `:root`. Dark mode overrides via `@media (prefers-color-scheme: dark)`. Token categories: colors, spacing (4px increments), typography (system stack + monospace), border-radius, shadows, transitions, layout dimensions.

#### CSS class inventory (all `qz-` prefixed)

Layout: `qz-dashboard`, `qz-layout`, `qz-sidebar`, `qz-main`, `qz-header`, `qz-content`
Navigation: `qz-nav-item`, `qz-nav-link`, `qz-nav-active`, `qz-nav-icon`, `qz-sidebar-brand`
Data display: `qz-stat-card`, `qz-badge`, `qz-state-dot`, `qz-tag`, `qz-time-ago`, `qz-event-item`
Tables: `qz-table`, `qz-table-header`, `qz-table-row`, `qz-table-cell`, `qz-pagination`
Interactive: `qz-btn`, `qz-btn-primary`, `qz-btn-danger`, `qz-btn-ghost`, `qz-dialog`, `qz-dialog-backdrop`
Form: `qz-form-group`, `qz-input`, `qz-select`, `qz-search`
Feedback: `qz-alert`, `qz-alert-error`, `qz-alert-success`, `qz-spinner`, `qz-empty`
Utility: `qz-tooltip`, `qz-mono`, `qz-truncate`, `qz-sr-only`

#### What other agents need to know

1. **Fenster (page components):** Use the CSS classes documented above. Stat cards go in `.qz-stat-grid`. Tables use `.qz-table` with `th.qz-sortable` for sortable columns. Use `.qz-badge-group` + `.qz-badge-name` for key display. State dots: `.qz-state-dot .qz-state-{running|paused|error|complete|standby|shutdown|waiting|blocked|normal}`.

2. **Hockney (API layer):** The `SchedulerSelector.razor` injects `IQuartzApiClient` and `SchedulerState` from `Quartz.Dashboard.Services`. I created minimal stub types — you'll need to expand `IQuartzApiClient` with the full method set from the architecture doc. The `SchedulerState` class is complete per the architecture doc (with one fix: uses `EventHandler<EventArgs>` instead of `Action` to satisfy the Meziantou analyzer).

3. **Verbal (tests):** The CSS file is ~36KB. All interactive elements have `:focus-visible` states. Contrast ratios target WCAG 2.1 AA.

— McManus
