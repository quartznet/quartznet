# Testing

How tests are written, what the coverage gate counts, and which runs a green unit suite does not cover.
The commands are in `AGENTS.md`.

`AGENTS.md` states each rule here in brief and routes to this document, which holds the full text
and wins where the two differ.

## Assertions

**Use AwesomeAssertions (`.Should()`) rather than NUnit's `Assert`.** `AwesomeAssertions` is a
global using in the test projects, so no `using` is needed. It produces far better failure messages,
which is most of the value of a test that has just failed.

```csharp
// Preferred
scheduler.SchedulerName.Should().Be("core");
options.Context.Should().ContainKey("environment").WhoseValue.Should().Be("staging");
threadPool.Should().BeOfType<DefaultThreadPool>();
limits.Should().NotBeNull();
schedulers.Should().NotContain(x => x.IsShutdown);

// Reason strings explain *why*, and show up in the failure message
reporting.Should().NotBeSameAs(defaultStore,
    "each scheduler must own its job store, otherwise they share trigger state");

// Exceptions
var act = async () => await factory.GetScheduler();
await act.Should().ThrowAsync<SchedulerConfigException>()
    .WithMessage("*IdleWaitTime*");

// Avoid
Assert.That(scheduler.SchedulerName, Is.EqualTo("core"));
Assert.Multiple(() => { ... });
Assert.ThrowsAsync<SchedulerConfigException>(async () => await factory.GetScheduler());
```

`Assert.Multiple` is unnecessary — AwesomeAssertions reports each failed assertion, and
`AssertionScope` is available when several assertions really must be evaluated as a group.

NUnit's `Assert` still appears in older tests; leave it alone unless you are already editing that
code, and use AwesomeAssertions for anything new.

## Coverage

SonarCloud's new-code condition — 80 % of new lines — reads only what `dotnet fallout Compile UnitTest
--coverage` collects: `Quartz.Tests.Unit`, `Quartz.Tests.AspNetCore`, `Quartz.Analyzers.Tests`,
`Quartz.Tests.Interceptors` and `Quartz.Tests.Aspire` (`build/Build.cs`, `UnitTest`). The dashboard host is
tested over `WebApplicationFactory` in `Quartz.Tests.AspNetCore`; `Quartz.Aspire.Hosting` only in
`Quartz.Tests.Aspire`, the one project that references `Aspire.Hosting`. `Quartz.Tests.Integration` never feeds it, so
product code only an integration test reaches counts as uncovered.

| Code | Unit-level test |
|---|---|
| An ADO.NET store path | A file SQLite database: `UseSQLite(...)` plus `ProvisionSchema()` creates the tables |
| An `IJobStore` default interface member | A fake that does not override it: `A.CallTo(...).CallsBaseMethod()` |
| A new `IScheduler` member | The forwarding through `DeferredScheduler` and `DelegatingScheduler` |

Per-file figures for a pull request: `https://sonarcloud.io/api/measures/component?component=quartznet_quartznet:<path>&pullRequest=<n>&metricKeys=new_lines_to_cover,new_uncovered_lines`
(read `measures[].periods[0].value`).

## Runs the unit suite does not cover

- **`dotnet fallout BenchmarkSmoke`** runs every benchmark once. Run it when a change touches a type
  `src/Quartz.Benchmark` uses: triggers, `TriggerHeader`, scheduler internals, serialization shapes.
- **`dotnet fallout PublishTrimmed PublishAot`** is where an `IL2xxx` or `IL3xxx` shows up; see
  [architecture.md](architecture.md), "Trimming and AOT".
- **`dotnet fallout ContainerSmoke`** builds the dashboard host's image into a local Linux Docker engine, runs
  it and removes it. Run it when a change touches `src/Quartz.Dashboard.Host` or its csproj; it skips, with a
  warning, where no Linux engine answers.
- **`dotnet fallout WolverineSmoke ExamplesSmoke`** start the Wolverine sample and the examples out of
  process, and fail when an effect they assert does not arrive.
- **`pr-tests-unit-net11`** is the .NET 11 leg (#4211), on ubuntu and only for changes to `src/`, `build/`,
  `database/` and the root build files. `dotnet fallout UnitTest --unit-test-runtime 11` rolls every test host forward
  to the newest runtime installed and fails unless each one reports .NET 11. `dotnet fallout Pack
  Net11ConsumerSmoke` builds `build/net11-consumer` with the .NET 11 SDK outside the repository, from the
  packed packages: a Debug web application with the dashboard at a custom path, then both trim canaries
  trimmed and native. Both need the .NET 11 SDK installed; the native publish on Windows needs `vswhere`
  on `PATH`, as `PublishAot` does.
