# Documentation and generated artifacts

Rules for the documentation site, package readmes, generated pages and the public API baselines.
Prose style is `CONTRIBUTING.md` → "Writing style".

`AGENTS.md` states each rule here in brief and routes to this document, which holds the full text
and wins where the two differ.

- **`docs/documentation/quartz-3.x/` must keep the old names.** Only update `quartz-4.x/`.
- **Pages are reference, not essays.** Follow `CONTRIBUTING.md`'s "Writing style": answer first, short
  sentences, tables over paragraphs, no quotations. Cut words, never facts.
- **Heading fragments are checked with VuePress's slugger, not GitHub's.** `npm run docs:check-links`
  checks them on every docs pull request: `Quartz.Core` is `#quartz-core`, not `#quartzcore`.
  markdownlint's MD051 is off, and its fragment suggestions 404. `npm run docs:check-links-test` is
  the checker's control.
- **C# in a documentation page is generated, not typed.** Samples live as `#region sample_*` blocks in
  `src/Quartz.Documentation.Samples` — an ordinary project in the solution, so a rotted sample fails
  `Compile` — and a page carries `<!-- snippet: name -->` / `<!-- endSnippet -->` markers that
  `dotnet fallout DocsSnippets` fills in. `VerifyDocsSnippets` fails a pull request whose markdown is
  stale, whose marker names nothing, or whose marker came out empty. The convention, and the handful of
  blocks deliberately left as plain fences, are written up in `CONTRIBUTING.md` under "Code samples in
  the documentation". Pages outside `docs/documentation/quartz-4.x/packages/` still carry hand-written
  fences; converting one is welcome, converting it halfway is not.
- **A package's readme is `src/<Project>/README.md`, never a documentation page.** That file is what
  nuget.org renders, and nuget.org renders CommonMark with none of VuePress's extensions — frontmatter
  becomes a horizontal rule and a literal `title:`, a `::: tip` becomes literal text, a relative link
  404s. `PackageReadmeTest` fails on each of those, on a packable project with no readme, and on a csproj
  that packs anything out of `docs/`. The readmes carry the same `<!-- snippet: … -->` markers as the
  documentation, so keep them short and let the site hold the prose.
- **`src/Quartz.Tests.Unit/Verify/PublicApiTest_*.verified.txt` are the public API baselines**, and
  `src/Quartz.Tests.AspNetCore/Verify/PublicApiTest_*.verified.txt` are the same thing for
  `Quartz.AspNetCore` and `Quartz.Dashboard`, whose dependencies only that project has.
  Any change to public API fails those tests; review the diff, and if the change is intended,
  accept the new baseline and carry the same diff into
  `docs/documentation/quartz-4.x/migration-guide.md`. Never hand-edit them.
- **`3.x` carries the same baselines, so the 4.0 API delta is a `git diff`.** It is exhaustive and
  always current, which no prose summary of the delta can be:

  ```shell
  git fetch origin

  # one assembly
  git diff origin/3.x:src/Quartz.Tests.Unit/Verify/PublicApiTest_Quartz.verified.txt \
           origin/main:src/Quartz.Tests.Unit/Verify/PublicApiTest_Quartz.verified.txt

  # everything both branches snapshot
  git diff origin/3.x origin/main -- src/Quartz.Tests.Unit/Verify src/Quartz.Tests.AspNetCore/Verify
  ```

  Package boundaries moved between the two, so match the files up first with the migration guide's
  appendix; `Task` → `ValueTask` and the namespace moves are systematic and are **not** deltas worth
  reporting.
- **`docs/documentation/quartz-4.x/log-events.md` is generated** from the `LogEventCatalogTest_*` snapshots by `dotnet fallout DocsLogEvents`; `VerifyDocsLogEvents` runs on every docs pull request, and a rebase that merges two catalogues leaves the page stale until it is regenerated.
