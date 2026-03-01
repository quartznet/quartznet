### 2026-02-16: UI framework decision
**By:** Keaton
**What:** Use vanilla CSS with hand-built components. Do not adopt MudBlazor or any third-party component library.
**Why:**

I gave MudBlazor a fair hearing. Here's the honest breakdown.

---

#### What MudBlazor would give us

Real things, not theoretical:

- **Data tables** with sorting, filtering, pagination — out of the box. We need these on Jobs, Triggers, History, and Currently Executing pages. Building these from scratch is the single biggest chunk of UI work.
- **Form components** with validation — useful for the JobDataMap editor and trigger configuration forms.
- **Dialogs/modals** — we need confirm dialogs for destructive actions (pause, resume, delete, trigger now).
- **Theming** — light/dark mode via MudThemeProvider, no custom CSS properties needed.
- **Developer velocity** — contributors can read MudBlazor docs and know what `<MudTable>` does. With custom components, they need to learn our API.
- **Community familiarity** — MudBlazor is the most popular Blazor component library. Most contributors will know it.

If this were a standalone app, MudBlazor would be the obvious choice. It isn't.

#### Why MudBlazor is wrong for an embeddable library

This is the part Marko's question forces us to confront: **we're not building an app, we're building middleware that lives inside someone else's app.** That changes everything.

1. **Global CSS pollution.** MudBlazor injects global styles via `<link href="_content/MudBlazor/MudBlazor.min.css">`. These styles target base HTML elements (`body`, `h1`-`h6`, `p`, `a`, `table`, etc.). If the host app has its own styles — and it will — MudBlazor's globals will fight them. There is no "scoped MudBlazor" option. You can't isolate it. This is a dealbreaker for middleware.

2. **Version coupling.** If the host app already uses MudBlazor 6, and we ship with MudBlazor 7, the host app breaks or we break. NuGet can't resolve two versions of the same package in one process. The host developer is forced to upgrade or downgrade — either way, we've created a dependency conflict in their project. For a library that promises "two lines of code to add," this is unacceptable.

3. **Bundle size.** MudBlazor's CSS is ~300KB minified. Its JS interop is another ~100KB. For a dashboard that most users visit occasionally, we'd be forcing every page load of the host app to carry this weight (since static assets are registered globally). Our custom CSS will be under 20KB.

4. **Transitive dependency chain.** MudBlazor depends on `MudBlazor.ThemeManager`, `Microsoft.AspNetCore.Components.Web`, and pulls in specific versions. Every transitive dependency is a potential conflict with the host app's dependency graph. Quartz.NET's core library is famously dependency-light — the dashboard should maintain that discipline.

5. **Loss of visual control.** MudBlazor components have opinions about spacing, typography, elevation, and animation. We can theme the colors, but not the fundamental visual language. An ops dashboard should be dense and fast-loading. MudBlazor's Material Design aesthetic is designed for consumer apps — generous whitespace, ripple effects, elevation shadows. That's the opposite of what we want.

#### What custom CSS actually costs

Let's be honest about the work:

- **Data table with sort/filter/pagination**: ~2 days of work. It's a `<table>`, column headers that toggle sort direction, a text input for filtering, and a page size selector. This isn't Excel — it's showing 20-50 rows of jobs/triggers. We don't need virtualized scrolling, column reordering, or inline editing.
- **Confirmation dialog**: Half a day. A `<dialog>` element with two buttons. HTML has native modal dialog support.
- **Form inputs**: We barely have forms. The JobDataMap editor is a key-value list with add/remove. Trigger configuration uses dropdowns and text inputs. There's no complex form wizard.
- **Theming**: CSS custom properties give us light/dark for free. `prefers-color-scheme` media query + a toggle. One afternoon.
- **Pagination**: A `<nav>` with page number buttons. Trivial.

Total estimated UI component work: **3-5 days**. That's the "weeks of work" concern addressed. It's not weeks. These are simple, data-display components for a known, bounded set of pages.

#### Why not a middle ground?

- **CSS-only frameworks (Pico, Simple.css)**: Same global CSS problem as MudBlazor, just smaller. Any framework that styles base elements is incompatible with embedding.
- **Headless component libraries**: Interesting in theory (provide behavior, bring your own styles). But Blazor's headless library ecosystem is immature. The only notable option is Blazorise in headless mode, and it still has global CSS requirements.
- **Scoped MudBlazor**: Not possible. MudBlazor requires its global stylesheet to function. You can't cherry-pick components without the base styles.

#### Decision

**Stay with vanilla CSS.** The original architecture got this right.

The constraints haven't changed: we're embeddable middleware. Any third-party UI framework that injects global styles, creates version coupling, or bloats the host app's bundle is incompatible with that fundamental requirement.

The "developer velocity" argument for MudBlazor is real but overstated for this project. We have ~10 pages and ~10 shared components. The components are simple data displays, not complex interactive widgets. The cost of building them from scratch is days, not months.

What we *should* do to address the legitimate concerns behind Marko's question:

1. **Document our component API clearly** — every shared component gets a usage example in its doc comment.
2. **Keep components simple** — if a component needs more than 100 lines of markup, it's doing too much.
3. **Establish visual patterns early** — build the data table and stat card first. Everything else follows their patterns.
4. **Use CSS custom properties religiously** — contributors shouldn't be writing raw color values or magic numbers.

The dashboard that ships with zero third-party UI dependencies, loads in under 50KB of CSS/JS, and never conflicts with host app styles — that's the dashboard that earns trust. The one that makes users say "I forgot it was even there until I needed it."

That's the goal. Vanilla CSS gets us there. MudBlazor doesn't.

— Keaton
