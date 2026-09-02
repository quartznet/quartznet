---
title: 'Extending Quartz: what is open, what is closed, and how to ask'
---

# Extending Quartz: what is open, what is closed, and how to ask

Quartz is extended by implementing an interface or deriving from an open base class, never by
reflection over its internals. This page is the index of the seams, and the policy for the rest.

## The open seams

| You want to | Seam | Page |
|---|---|---|
| Keep scheduling data somewhere new | `IJobStore` | [A Job Store of Your Own](custom-job-store.md) |
| Add behaviour around an existing store | `DelegatingJobStore` — every member `virtual` | [Decorating a store](custom-job-store.md#decorating-a-store) |
| Support a database with no shipped dialect | subclass `StdAdoDelegate` | [A Driver Delegate for a New Database](dialect-delegate.md) |
| Store a trigger family of your own | `ITriggerPersistenceDelegate` | [Persisting a Custom Trigger Type](trigger-persistence-delegate.md) |
| Replace the `QRTZ_LOCKS` row | `ILockHandler` | [A Lock Handler of Your Own](lock-handler.md) |
| Add a trigger or calendar type | `TriggerBase` / `BaseCalendar`, plus a serializer in each JSON package | [System.Text.Json Serialization](../packages/system-text-json.md) |
| Wrap or proxy a scheduler | `DelegatingScheduler` — every member `virtual` | [Testing](../tutorial/testing.md) |
| Run code around every job | `IJobExecutionMiddleware` | [Job Execution Middleware](../tutorial/job-execution-middleware.md) |
| React to scheduler events | `IJobListener`, `ITriggerListener`, `ISchedulerListener`, `ISchedulerPlugin` | [Trigger and Job Listeners](../tutorial/trigger-and-job-listeners.md) |
| Change how jobs are constructed, types are loaded, work is scheduled | `IJobFactory`, `ITypeLoader`, `IThreadPool` | [Configuration Reference](../configuration/reference.md) |
| Serve the dashboard from somewhere else, or keep its history | `IQuartzApiClient`, `IDashboardHistoryStore` | [Dashboard](../packages/dashboard.md) |

Every one of these is registered through a `Use*` or `Add*` method on `IQuartzBuilder` or
`IPersistentStoreBuilder`. Registration is **first-wins** (`TryAdd`): register yours *instead of* the
shipped one, not after it — which for the persistent store means before the `UseSqlServer`,
`UsePostgres` or `UseGenericDatabase` call, because each of those names a driver delegate of its own.

`src/Quartz.Documentation.Samples` is not a friend assembly, and it compiles a `StdAdoDelegate`
subclass and a whole `ITriggerPersistenceDelegate` written from scratch. That is the compile-time
proof behind "the `Quartz.Impl.AdoJobStore` namespace is an authoring kit rather than the leftovers of
an implementation": a sample that stops compiling means the public kit lost a type.

## Two promises that make a seam safe to extend

Both hold across 4.x, and both are what let a future release give a collaborator something more to
work with without breaking the ones that already exist.

**A collaborator is handed a context object, never a parameter list.** `DriverDelegateContext`,
`LockHandlerContext`, `TriggerPersistenceDelegateContext`, `TriggerAcquisitionRequest`,
`TriggerAcquisitionCriteria`, `TriggerFiredBundle` and `SchedulerIdentity` all have a public
parameterless constructor and `init` properties. A new datum is a new non-`required` property, which
is source- and binary-compatible — so "the scheduler needs one more thing from a store" is an
afternoon rather than a major version.

**A member added to a public interface arrives as a default interface member.** `IJobStore` carries
nine of them, `IScheduler` one, and `ILockHandler` and `ITriggerPersistenceDelegate` one each; an
implementation of yours goes on compiling and gets the default. The public API baselines mark them,
so the promise is checkable rather than asserted.

Two things follow for a decorator. A default interface member is not inherited into a class's member
set, so it is callable only through an interface-typed reference unless the class declares it — and a
forwarding type that does not declare one lets the default body run *on the forwarder*, asking the
inner instance whatever that default decomposes into rather than the question that was put to it.
`DelegatingJobStore` and `DelegatingScheduler` therefore declare every member of their contract, and a
reflection sweep in the test suite holds them to it. Do the same in a forwarder of your own.

## What is closed, and why

- **`RAMJobStore` is sealed and the two ADO.NET stores are internal.** They hold locks across several
  index mutations in a fixed order; no override can be asked to preserve that. Decorate instead.
- **The SQL statement text is internal (`StdAdoConstants`).** The schema is the contract, and it is
  public in `AdoConstants`. Override the delegate method that issues a statement, not the string —
  every `IDriverDelegate` member on `StdAdoDelegate` is `virtual`.
- **How an instant and a duration are stored is not a delegate's choice.** `GetDbDateTimeValue`,
  `GetDateTimeFromDbValue`, `GetDbTimeSpanValue` and `GetTimeSpanFromDbValue` are deliberately not
  `virtual`: UTC ticks and whole milliseconds are part of the schema contract, and the preferred-node
  liveness SQL does raw arithmetic on them. The boolean pair *is* a seam, because Oracle has no
  boolean column type.
- **Read-replica routing is not expressible.** `IDbProvider.CreateConnection()` takes no argument, so
  the store cannot say whether the coming statement reads or writes. If it is ever opened, the move is
  a default interface member — `DbConnection CreateReadConnection() => CreateConnection();` — and not
  a parameter on `CreateConnection`, which would break both public `IDbProvider` implementations.
- **The HTTP wire DTOs, the health-check predicate and the dashboard's default services are
  internal.** Each has a public interface or an options object in front of it; write your own
  endpoint, check or service rather than editing ours.
- **The scheduling file formats (XML, JSON) know four schedule types.** A custom trigger is scheduled
  in code or through the API.

## How to ask for a seam

Open an issue **describing the integration, not the member**. "Make `X` public" cannot be judged;
"our store shards `QRTZ_TRIGGERS` by tenant and we need the acquisition predicate to carry a tenant
id" can. Say what you are building, what you tried, and what stopped you. Opening a seam is
deliberately easier than closing one, so a good case is usually granted — but the shape it is granted
in is chosen from the problem, not from the workaround.

## See also

- [A Job Store of Your Own](custom-job-store.md) — the largest seam, and the one with the most rules
- [Migration Guide](../migration-guide.md) — what moved, was sealed or was internalized in 4.0
