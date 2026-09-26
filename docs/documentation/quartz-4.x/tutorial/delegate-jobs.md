---
title: 'Delegate Jobs'
---

<ApplicableVersion version="4.3" />

A delegate job is a lambda whose parameters are what it needs. `ScheduleJob` adds one with its trigger,
and `AddJob` adds one on its own.

<!-- snippet: sample_delegate_job -->
```csharp
services.AddQuartz(q =>
{
    q.ScheduleJob(
        "session-cleanup",
        static async (ISessionStore sessions, ILogger<SessionCleanup> log, CancellationToken cancellationToken) =>
        {
            int purged = await sessions.PurgeExpired(cancellationToken);
            log.LogInformation("Purged {Count} expired sessions", purged);
        },
        trigger => trigger.WithCronSchedule("0 0 * * * ?"));
});

services.AddQuartzHostedService();
```
<!-- endSnippet -->

## What each parameter is handed

| Parameter type | Handed |
|---|---|
| `IJobExecutionContext` | the firing |
| `CancellationToken` | the firing's token, the same one `context.CancellationToken` carries |
| `IServiceProvider` | the firing's DI scope |
| any other type | `GetRequiredService` from that scope |

* **Write every parameter's type.** A lambda without them has no delegate type and does not compile.
* **Return `Task`, `ValueTask` or nothing.** `AddJob` and `ScheduleJob` throw `ArgumentException` for
  any other return type, `async void`, a `ref`, `out`, `in`, pointer or ref struct parameter, and a
  combined delegate.
* **`async () => await repo.Count()` returns `Task<int>`, and is refused.** A job has no result. Use a
  block body, or set `IJobExecutionContext.Result`.
* **A missing service fails the firing**, not the registration: the run shell reports a
  `JobExecutionException` whose inner exception is the container's.
* **A scheduler's own parts fail validation at startup**: `IScheduler`, `ISchedulerFactory`, a
  scheduler's options. Take `IJobExecutionContext` and read `context.Scheduler`. It is the rule for
  [a registered job's constructor](../multi-tenancy.md#which-scheduler-s-parts-a-job-is-built-from).
* **Job data** is `context.MergedJobDataMap`. It is not applied to properties, since a lambda has none.

The parameters are read once, when the job is added, and the handler runs through reflection. It works
trimmed and under native AOT: the repository's trimming canary runs a delegate job in its native
binary. [#3882](https://github.com/quartznet/quartznet/issues/3882) replaces the reflection with
generated code.

## Adding one

| Call | The job | Its trigger |
|---|---|---|
| `ScheduleJob(name, handler, trigger)` | takes its trigger's identity; not durable | named `name` unless `trigger` renames it |
| `AddJob(name, handler, configure)` | `name` in the default group, unless `configure` sets an identity; durable | none: add one with `AddTrigger`, or fire it with `TriggerJob` |

Each has a twin whose callback also takes the `IServiceProvider`, for configuration read from services.

<!-- snippet: sample_delegate_job_add_job -->
```csharp
services.AddQuartz(q =>
{
    // Durable, and fired only by a trigger of its own or by TriggerJob.
    q.AddJob("send-digest", static (IEmailSender email, CancellationToken cancellationToken) =>
        email.SendDigest(cancellationToken));

    q.AddTrigger(trigger => trigger
        .ForJob("send-digest")
        .WithCronSchedule("0 0 7 ? * MON-FRI"));
});
```
<!-- endSnippet -->

A one-off firing is the named job fired with data of its own:

<!-- snippet: sample_delegate_job_one_off -->
```csharp
// A one-off firing of the named job, carrying data of its own.
await scheduler.TriggerJob(
    new JobKey("send-digest"),
    new JobDataMap { ["recipient"] = "ada@example.com" },
    cancellationToken);
```
<!-- endSnippet -->

<!-- snippet: sample_delegate_job_context -->
```csharp
services.AddQuartz(q =>
{
    q.AddJob("send-digest", static (IJobExecutionContext context, ILogger<SessionCleanup> log) =>
    {
        string? recipient = context.MergedJobDataMap.GetString("recipient");
        log.LogInformation("Digest for {Recipient}, fired by {Trigger}", recipient, context.Trigger.Key);
    });
});
```
<!-- endSnippet -->

## Persistence and clusters

* **Every delegate job is stored as `Quartz.Impl.DelegateJob, Quartz`.** The name resolves on every
  node, so the job persists and clusters like any other. Its `Description` defaults to
  `Delegate job '<name>'`.
* **The job key is the identity.** The handler is code and is not stored. A firing finds it by the job's
  key, on the scheduler that fires it.
* **Register the job on every node** that runs the scheduler. A node without it fails the firing with a
  `JobExecutionException` naming the key and the scheduler, so the trigger's retry policy and
  `SchedulerError` apply.
* **Two handlers under one key on one scheduler** are refused when the scheduler starts.
* **Named schedulers keep their own handlers.** The same key on two schedulers is two jobs.
* **An `IsJobTypeAllowed` allow-list** on the [HTTP API](../packages/http-api.md#narrowing-which-job-types-may-be-named)
  or the [dashboard](../packages/dashboard.md#narrowing-which-job-types-may-be-named) must allow
  `Quartz.Impl.DelegateJob, Quartz` for a caller to add a delegate job by type name. Such a job runs
  only if a handler is registered under its key.

## What composes

| Feature | With a delegate job |
|---|---|
| `[DisallowConcurrentExecution]` | `.DisallowConcurrentExecution()` in `AddJob`'s `configure`; both stores enforce it |
| `[PersistJobDataAfterExecution]` | `.PersistJobDataAfterExecution()` in `AddJob`'s `configure` |
| `[JobTimeout]` | [`AddJobTimeout(defaultTimeout)`](job-execution-middleware.md#timing-a-job-out); there is no attribute to put on a lambda |
| Retry policy, execution group, preferred node, calendar | on the trigger, as for any job |
| Middleware, listeners, execution history, metrics | unchanged; the job type they report is `DelegateJob` |
| `ConfigureJobScope` | runs before the handler's services are resolved |

<!-- snippet: sample_delegate_job_composition -->
```csharp
services.AddQuartz(q =>
{
    // DelegateJob carries no [JobTimeout], so the scheduler-wide default bounds it.
    q.AddJobTimeout(TimeSpan.FromMinutes(5));

    q.AddJob(
        "reindex",
        static async (ISessionStore sessions, CancellationToken cancellationToken) =>
        {
            await sessions.PurgeExpired(cancellationToken);
        },
        job => job
            .WithIdentity("reindex", "maintenance")
            .WithDescription("Rebuilds the session index")
            // The attribute's builder form: every delegate job shares one type.
            .DisallowConcurrentExecution());

    q.AddTrigger(trigger => trigger
        .ForJob("reindex", "maintenance")
        .WithCronSchedule("0 0/15 * * * ?")
        .WithRetryPolicy(RetryPolicy.Exponential(3, TimeSpan.FromSeconds(30))));
});
```
<!-- endSnippet -->

## When to write an `IJob` class instead

* The job needs a typed input: `IJob<TInput>` and `ScheduleJob<TJob, TInput>` take a class.
* It needs a `[JobTimeout]` of its own, rather than the scheduler-wide default.
* It should be tested, reused or found by name in the code base.
* Job data should arrive as properties.
* Not every node that runs the scheduler registers the same jobs. A class resolves by its type name; a
  delegate job needs its handler registered.

## Related

* [Using Quartz](using-quartz.md): `AddJob<T>`, `AddTrigger<T>` and `ScheduleJob<T>`
* [Declaring Jobs with Attributes](declaring-jobs-with-attributes.md): a class declared with
  `[QuartzJob]` and `[CronTrigger]`
* [One-Off Job](../how-tos/one-off-job.md): many short-lived firings of one job
