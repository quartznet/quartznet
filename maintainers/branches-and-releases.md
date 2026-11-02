# Branches and releases

Moving a change between `3.x` and `main`, and how a release is recorded. Patch-line rules are
[api-decisions.md](api-decisions.md), "Promises 4.0 makes"; cross-branch database scripts are
[persistence.md](persistence.md).

`AGENTS.md` states each rule here in brief and routes to this document, which holds the full text
and wins where the two differ.

## Porting changes between 3.x and main

`3.x` is the maintenance branch and `main` is 4.x, and a change written against one usually needs
relocating for the other. **The map is `docs/documentation/quartz-4.x/migration-guide.md`** — every
namespace move, every contract that changed shape, and an appendix indexed by the name you would have
typed. Ported code that fails to build is usually a rename, not a missing feature, so look there
before assuming the feature is missing.

## What a port to 3.x meets

| On `3.x` | So |
|---|---|
| Its own `AGENTS.md` (shorter, no size test) governs | Read it after switching; this tree's rules are `main`'s |
| `IJob.Execute` and the other async members return `Task`, with no `CancellationToken` parameter on `IJob` | Adjust signatures; keep the bodies |
| `Quartz` targets `net462;net472;net8.0;net9.0;net10.0;netstandard2.0`, and `Quartz.Tests.Unit` targets `net10.0;net472` | Run the unit tests on both frameworks; `net472` runs on Windows only |
| No `Directory.Packages.props`: versions are inline in each project | Add a package version in the project, not centrally |
| `Assert` is a global alias of NUnit's `ClassicAssert` | Use `.Should()` only, so a test file can be the same on both branches |
| `BinaryObjectSerializer` still ships, and `BinaryFormatter` matches fields by name | A `[Serializable]` auto-property that gains logic uses the `field` keyword, never a renamed backing field |
| `Quartz.Extensions.DependencyInjection` and `Quartz.Extensions.Hosting` are separate packages | `migration-guide.md`'s appendix maps each type back |
| No `docs/` tree | A 3.x documentation change is a `main` pull request under `docs/documentation/quartz-3.x/` |
| Pull request titles end in `(3.x)` | Keep the `main` title and add the suffix |

`main`'s analyzers are stricter. Write a change both branches carry so it passes `main`'s, and the shared
source stays identical.

## Release notes

- **Release notes live in GitHub releases, not in the repository.** There is no changelog file on
  either branch; the tag's release is the record. Pushing a `v*` tag runs `publish.yml`, whose last
  target, `DraftRelease` (`build/Build.Release.cs`, byte-identical on `3.x`), opens that release as a
  **draft** with notes generated from `.github/release.yml` and attaches `Quartz.NET-<version>.zip`.
  A draft that already carries notes keeps them and only gains the zip, so notes are written ahead
  of the tag; re-running the workflow is the recovery path (`Publish` skips duplicates, the asset is
  replaced). A human publishes the draft and decides "latest" — unticked for a 3.x release. To
  rehearse: `GITHUB_ACTIONS=true GITHUB_REF_TYPE=tag GITHUB_REF_NAME=v0.0.0-rehearsal
  GITHUB_REPOSITORY=quartznet/quartznet GITHUB_TOKEN=$(gh auth token) dotnet fallout Pack
  DraftRelease`, then delete the draft; a local tag is stamped `-dev` instead and `--skip` skips a
  target's dependencies, not the target.
