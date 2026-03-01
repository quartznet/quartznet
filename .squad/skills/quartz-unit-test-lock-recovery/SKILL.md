---
name: "quartz-unit-test-lock-recovery"
description: "Recover from CS2012 DLL lock failures when running Quartz unit tests"
domain: "testing"
confidence: "high"
source: "manual"
---

## Context
On Windows, `dotnet test src\Quartz.Tests.Unit\Quartz.Tests.Unit.csproj` may fail with CS2012 before tests run because `artifacts\obj\Quartz.Tests.Unit\debug\Quartz.Tests.Unit.dll` is locked by a lingering `VBCSCompiler` process.

## Pattern
1. Capture locking PID from CS2012 output.
2. Stop only that PID (`Stop-Process -Id <PID> -Force`).
3. Rerun tests with shared compilation disabled and single-node execution:
   `dotnet test src\Quartz.Tests.Unit\Quartz.Tests.Unit.csproj -m:1 /p:UseSharedCompilation=false`

## Expected Outcome
Build succeeds and tests execute normally; if failures remain, then they are likely real test/product failures.
