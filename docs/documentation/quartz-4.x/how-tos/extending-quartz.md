---
title: 'Extending Quartz: what is open, what is closed, and how to ask'
---

# Extending Quartz: what is open, what is closed, and how to ask

Extend Quartz by implementing an interface or deriving from an open base class, never by reflection
over its internals.

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
| Keep what a scheduler has run and missed | `IExecutionHistoryStore` | [Execution history](../packages/http-api.md#execution-history) |
| Serve the dashboard from somewhere else | `IQuartzApiClient` | [Dashboard](../packages/dashboard.md) |

Register each through a `Use*` or `Add*` method on `IQuartzBuilder` or `IPersistentStoreBuilder`.
Registration is **first-wins** (`TryAdd`): register yours *instead of* the shipped one, and for the
persistent store before `UseSqlServer`, `UsePostgres` or `UseGenericDatabase`, each of which names its own
driver delegate.

`src/Quartz.Documentation.Samples` is not a friend assembly, yet compiles a `StdAdoDelegate` subclass and a
complete `ITriggerPersistenceDelegate`, so the build fails if the public `Quartz.Impl.AdoJobStore` kit loses
a type.

## Two promises that make a seam safe to extend

Both hold across 4.x, so later releases can give a collaborator more without breaking yours:

- **A collaborator is handed a context object, never a parameter list.** `DriverDelegateContext`,
  `LockHandlerContext`, `TriggerPersistenceDelegateContext`, `TriggerAcquisitionRequest`,
  `TriggerAcquisitionCriteria`, `TriggerFiredBundle` and `SchedulerIdentity` have a public parameterless
  constructor and `init` properties. A new datum is a new non-`required` property: source- and
  binary-compatible.
- **A member added to a public interface arrives as a default interface member (DIM).** `IJobStore` has
  nine, `IScheduler` one, `ILockHandler` and `ITriggerPersistenceDelegate` one each. Your implementation
  keeps compiling and gets the default. The public API baselines mark every DIM.

**A forwarder must declare every DIM of its contract.** A DIM is not part of a class's members, so it is
callable only through the interface unless the class declares it. A forwarder that omits one runs the
default body *on the forwarder*, asking the inner instance whatever the default decomposes into rather than
the original question. `DelegatingJobStore` and `DelegatingScheduler` declare every member, checked by a
reflection sweep in the tests; do the same.

## What is closed, and why

- **`RAMJobStore` is sealed; the ADO.NET stores are internal.** They lock across several index mutations in
  a fixed order no override could preserve. Decorate instead.
- **`StdAdoConstants`, the SQL text, is internal.** The schema is the contract, public in `AdoConstants`.
  Override the delegate method that issues a statement; every `IDriverDelegate` member on `StdAdoDelegate`
  is `virtual`.
- **Instant and duration storage is fixed.** `GetDbDateTimeValue`, `GetDateTimeFromDbValue`,
  `GetDbTimeSpanValue` and `GetTimeSpanFromDbValue` are not `virtual`: UTC ticks and whole milliseconds are
  schema contract, and the preferred-node liveness SQL does arithmetic on them. The boolean pair *is*
  virtual, because Oracle has no boolean column type.
- **Read-replica routing is not expressible**: `IDbProvider.CreateConnection()` takes no argument. If it is
  ever opened, it will be a DIM, `DbConnection CreateReadConnection() => CreateConnection();`, never a
  parameter on `CreateConnection`, which would break both public `IDbProvider` implementations.
- **The HTTP wire DTOs, the health-check predicate and the dashboard's default services are internal**,
  each behind a public interface or options object. Write your own endpoint, check or service.
- **The XML and JSON scheduling files know four schedule types.** Schedule a custom trigger in code or
  through the API.

## How to ask for a seam

Open an issue that **describes the integration, not the member**: what you are building, what you tried,
what stopped you. "Make `X` public" cannot be judged; "our store shards `QRTZ_TRIGGERS` by tenant and we need
the acquisition predicate to carry a tenant id" can. Good cases are usually granted, in a shape chosen from
the problem rather than the workaround.

## See also

- [A Job Store of Your Own](custom-job-store.md) — the largest seam, and the one with the most rules
- [Migration Guide](../migration-guide.md) — what moved, was sealed or was internalized in 4.0
