# Project Context

- **Owner:** Marko Lahma
- **Project:** Blazor dashboard for Quartz.NET — web UI consuming HTTP APIs for job scheduling management
- **Stack:** .NET 10, C#, Blazor Web, HTTP APIs, Quartz.NET, NUnit, FluentAssertions, FakeItEasy
- **Created:** 2026-02-16

## Learnings
- 2026-03-01: `Quartz.Tests.AspNetCore` functional tests failed after `.slnx` migration because `WebApplicationFactory` defaults to finding `*.sln`; setting `ASPNETCORE_TEST_CONTENTROOT_QUARTZ_TESTS_ASPNETCORE` to a resolved project path restores reliable content-root resolution. ✅ FIXED
- 2026-03-01: `dotnet test src\Quartz.Tests.Unit\Quartz.Tests.Unit.csproj` passes locally (1280 total, 0 failed; 5 skipped including known WIP/flaky markers), so no backend/core fix was required for this run.
