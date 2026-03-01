# Decision: Tolerant DTO rendering in dashboard pages

**By:** Fenster  
**Date:** 2026-02-16

## What
Dashboard page components render API DTOs using `DisplayValueHelper` path accessors (including nested paths like `Key.Group`) instead of hard-coding a single DTO shape.

## Why
The dashboard consumes HTTP API contracts that can differ slightly between endpoints and may evolve while the UI is being developed in parallel. Tolerant access keeps pages resilient to minor DTO shape differences without blocking UI feature delivery.

## Impact
- List/detail pages can display both flat (`Group`/`Name`) and nested (`Key.Group`/`Key.Name`) key structures.
- Trigger/job execution pages remain functional if API payloads are represented as concrete DTOs or `JsonElement`-backed objects.
- If DTO contracts are finalized later, the helper can be simplified centrally without rewriting each page.
