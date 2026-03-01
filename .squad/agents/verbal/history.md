# Project Context

- **Owner:** Marko Lahma
- **Project:** Blazor dashboard for Quartz.NET — web UI consuming HTTP APIs for job scheduling management
- **Stack:** .NET 10, C#, Blazor Web, HTTP APIs, Quartz.NET, NUnit, FluentAssertions, FakeItEasy
- **Created:** 2026-02-16

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->
- 2026-03-01: `dotnet test src\Quartz.Tests.Unit\Quartz.Tests.Unit.csproj` can fail before test execution with CS2012 when `artifacts\obj\Quartz.Tests.Unit\debug\Quartz.Tests.Unit.dll` is locked by `VBCSCompiler`; stopping that PID and rerunning with `/p:UseSharedCompilation=false -m:1` restored a clean pass (1280 total, 0 failed). ✅ ENVIRONMENTAL ISSUE RESOLVED
- 2026-03-01: `dotnet test src\Quartz.Tests.AspNetCore\Quartz.Tests.AspNetCore.csproj` fails all 51 tests with `OneTimeSetUp` `InvalidOperationException` from `WebApplicationFactory` ("Solution root could not be located") after `.sln`→`.slnx`; candidate fix with `UseSolutionRelativeContentRoot()` insufficient; instead, setting `ASPNETCORE_TEST_CONTENTROOT_QUARTZ_TESTS_ASPNETCORE` environment variable before `WebApplicationFactory<Program>` instantiation resolves all 51 tests. ✅ FIXED
