# How to contribute

The easiest way to contribute is to open an issue and start a discussion. 
Then we can decide if and how a feature or a change could be implemented and if you should submit a pull requests with code changes.

Also read this first: [Being a good open source citizen](https://hackernoon.com/being-a-good-open-source-citizen-9060d0ab9732#.x3hocgw85)

## General feedback and discussions

Please start a discussion on the [core repo issue tracker](https://github.com/quartznet/quartznet/issues).

## Building

Run `build.cmd` or `build.sh` from the command line. The scripts restore the [Fallout](https://fallout.build/) CLI from
`.config/dotnet-tools.json` and hand the arguments to it, so no global tool install is needed.

## Testing

Integration tests provision their database dependencies through Testcontainers for .NET.

* Ensure your Docker daemon is running
* Run the build command with flags: `.\build.cmd Compile UnitTest IntegrationTest`

This builds and runs tests like the CI server does.

## Documentation

The documentation website is built and published from this **`main`** branch. All versioned docs live under `docs/documentation/` (for example `docs/documentation/quartz-3.x/` for the current stable line and `docs/documentation/quartz-4.x/` for the next release). Edit the docs here; the `3.x` maintenance branch no longer carries the docs site.

The published Quartz 3.x package pages under `docs/documentation/quartz-3.x/packages/` are mirrored, in compact NuGet-rendered form, by the per-package `src/<Project>/README.md` files on the `3.x` branch (which are packed into the NuGet packages). When you change one, update the other in a companion PR so the published page and the shipped package README stay consistent.

### Package readmes

Every packable project carries its own `src/<Project>/README.md`. That file is what `dotnet pack` puts in
the `.nupkg` and what nuget.org renders on the package page, and it is deliberately **not** a
documentation page: nuget.org renders CommonMark with none of VuePress's extensions, so frontmatter comes
out as a horizontal rule followed by a literal `title:`, a `::: tip` container comes out as literal text,
and a relative link 404s. `PackageReadmeTest` fails on all three, on a missing or undeclared readme, and
on a csproj that packs anything out of `docs/`.

Keep them short — what the package is, how to install it, the smallest useful example, and absolute links
to the documentation site, which is where longer prose belongs. Their code samples come from the same
compiled-snippet mechanism as the documentation pages, described below, so a readme carries snippet
markers rather than typed C#.

### Building the site

`npm ci` then `npm run docs:build`. `npm ci` runs `postinstall`, which is `patch-package`, which applies
the files in `patches/` — VuePress needs the `gray-matter` patch to parse front matter, so a build from an
unpatched tree fails in a way that does not name the cause.

CI installs with `npm ci --ignore-scripts`, so that no dependency's lifecycle script runs on a machine
holding a deploy secret, and then calls `npm run apply-patches` explicitly. The two entries in
`package.json` are the same command for different callers: `postinstall` is for you, `apply-patches` is for
CI. Keep both.

### Code samples in the documentation

**Do not type C# into a markdown file.** Write it as ordinary code in
[`src/Quartz.Documentation.Samples`](src/Quartz.Documentation.Samples), which is a project in the
solution, and let the build inject it into the page. A sample that stops compiling then fails the
build like any other code, and the page cannot drift from it, because the page is generated from it.

Wrap the lines the page should show in a `#region`, named `sample_` followed by something unique:

```csharp
public static void ShutdownUnderAHost(IServiceCollection services)
{
    #region sample_plugins_shutdown_under_a_host

    services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

    #endregion
}
```

Then put a marker pair where the fenced block would have gone:

```markdown
<!-- snippet: sample_plugins_shutdown_under_a_host -->
<!-- endSnippet -->
```

and run `npm run docs:snippets` (or `dotnet fallout DocsSnippets`) to fill it in. Commit the filled-in
markdown: it is what GitHub and the published site both render, and reviewing the generated code is
half the point. The same markers work in the package readmes under `src/<Project>/README.md`, which are
processed by the same target.

A few things worth knowing:

* **Names must be unique across the whole samples project.** Two regions with one name are not an error
  upstream — both are emitted, one after the other — so `DocsSnippets` treats a duplicate as an error.
* **The region's indentation is removed**, so a fragment can live inside whatever scaffolding makes it
  compile: put the region inside an `AddQuartz(q => { … })` body and the page shows just the `q.…` lines.
* **Two samples can show a type of the same name** by living in different namespaces or in different
  nested classes; the region never includes the enclosing declaration.
* **A page that must show `using` directives** needs its region to start at the top of a file, which
  means that file has no namespace of its own — see `CustomCalendarSample.cs`.
* **Sample code still has to satisfy the compiler.** The samples project turns the *style* analyzers
  down so a sample reads like a documentation page rather than like library code, but it is built with
  the same `TreatWarningsAsErrors` as everything else.
* Some blocks are deliberately left as plain fences: those calling a package this repository does not
  reference, and the `BinaryFormatter` migration sample, which would drag in an unsupported
  compatibility package. Adding a NuGet dependency purely to compile a sample is not worth it.

`dotnet fallout VerifyDocsSnippets` is what CI runs. It fails when a page names a snippet that does not
exist, when a marker was left empty, and when the committed markdown no longer matches the samples.
Nothing regenerates the documentation behind your back — a stale page is your pull request's problem,
not a bot's.

## Bugs and feature requests?

Please log a new issue in the GitHub repo.

## Other discussions

https://gitter.im/quartznet/quartznet and https://groups.google.com/forum/#!forum/quartznet

## Filing issues

Use the issue forms at https://github.com/quartznet/quartznet/issues/new/choose. Each form prompts
for the information we need to investigate, so please fill in every required field.

The fastest path to a fix is a minimal, runnable reproduction in a public GitHub repo we can clone —
isolated repros get triaged first. If your issue is a usage question, please use
[Discussions](https://github.com/quartznet/quartznet/discussions) or the
[`[quartz.net]` tag on Stack Overflow](https://stackoverflow.com/questions/tagged/quartz.net) instead.

Believe you have found a security vulnerability? Do **not** open a public issue. Use
[Security Advisories](https://github.com/quartznet/quartznet/security/advisories/new); see
[`SECURITY.md`](.github/SECURITY.md) for details.

## Contributing code and content

Make sure you can build the code. Familiarize yourself with the project workflow and our coding conventions. If you don't know what a pull request is read this article: https://help.github.com/articles/using-pull-requests.

Before submitting a feature or substantial code contribution please discuss it with the team and ensure it follows the product roadmap. Here's a list of blog posts that are worth reading before doing a pull request:

* [Open Source Contribution Etiquette](http://tirania.org/blog/archive/2010/Dec-31.html) by Miguel de Icaza
* [Don't "Push" Your Pull Requests](http://www.igvita.com/2011/12/19/dont-push-your-pull-requests/) by Ilya Grigorik.
* [10 tips for better Pull Requests](http://blog.ploeh.dk/2015/01/15/10-tips-for-better-pull-requests/) by Mark Seemann
* [How to write the perfect pull request](https://github.com/blog/1943-how-to-write-the-perfect-pull-request) by GitHub

Here's a few things you should always do when making changes to the code base:

**Commit/Pull Request Format**

```
Summary of the changes (Less than 80 chars)
 - Detail 1
 - Detail 2

#bugnumber (in this specific format)
```

**Tests**

-  Tests need to be provided for every bug/feature that is completed.
-  Tests only need to be present for issues that need to be verified by QA (e.g. not tasks).
-  If there is a scenario that is far too hard to test there does not need to be a test for it.
  - "Too hard" is determined by the team as a whole.
