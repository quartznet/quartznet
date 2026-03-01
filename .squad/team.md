# Team Roster

> Blazor dashboard for Quartz.NET — web UI consuming HTTP APIs for job scheduling management.

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. Does not generate domain artifacts. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Keaton | Lead | `.squad/agents/keaton/charter.md` | ✅ Active |
| McManus | UI/UX Designer | `.squad/agents/mcmanus/charter.md` | ✅ Active |
| Fenster | Blazor Dev | `.squad/agents/fenster/charter.md` | ✅ Active |
| Hockney | Backend Dev | `.squad/agents/hockney/charter.md` | ✅ Active |
| Verbal | Tester | `.squad/agents/verbal/charter.md` | ✅ Active |
| Scribe | Session Logger | `.squad/agents/scribe/charter.md` | 📋 Silent |
| Ralph | Work Monitor | — | 🔄 Monitor |

## Model Preferences

| Context | Model | Rationale |
|---------|-------|-----------|
| Complex work, architecture, large designs | `claude-opus-4.6` | User directive — premium tier for high-stakes decisions |
| General implementation | `gpt-5.3-codex` | User directive — code specialist for implementation work |
| Non-code tasks (docs, logs, planning) | `claude-haiku-4.5` | Cost first for mechanical ops |

## Project Context

- **Owner:** Marko Lahma
- **Stack:** .NET 10, C#, Blazor Web, HTTP APIs, Quartz.NET scheduling library
- **Description:** Blazor dashboard for monitoring and managing Quartz.NET scheduled jobs via HTTP web APIs
- **Created:** 2026-02-16
