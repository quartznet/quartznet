# Hockney Decision: Dashboard DTO boundary for HTTP API contracts

**Date:** 2026-02-16  
**Requested by:** Marko Lahma

## Decision
`Quartz.Dashboard` introduces its own public DTOs for dashboard-facing API client contracts instead of directly using `Quartz.HttpClient.HttpApiContract` types.

## Why
The `Quartz.HttpClient.HttpApiContract` DTOs are currently `internal` and not visible to `Quartz.Dashboard`. Keeping dashboard DTOs local allows scaffolding to compile immediately without changing visibility in `Quartz.HttpClient`.

## Impact
- Dashboard service interfaces are unblocked and can evolve independently.
- If shared contract types are desired later, we should either make selected DTOs public in `Quartz.HttpClient` or add `InternalsVisibleTo` for `Quartz.Dashboard`.
