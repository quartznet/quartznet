---

title: 'Job Execution Middleware'
---

# Job Execution Middleware

Middleware wraps every job a scheduler executes. Use it for a cross-cutting concern that must *surround*
the call to the job: a log scope, a tenant context, a metric, translating a third-party library's
exceptions.

A [listener](trigger-and-job-listeners.md) cannot do this: the job runs *between* its two notifications,
so it cannot open an `await using` around the job, skip it, or catch its exception. Before 4.0 such code
had to be a job that wrapped another job.

## The interface

<!-- Quartz's own declaration, so it is written out here rather than compiled from the samples
     project: a second `Quartz.IJobExecutionMiddleware` in that project would shadow the real one. -->

```csharp
public delegate ValueTask JobExecutionDelegate(IJobExecutionContext context, CancellationToken cancellationToken);

public interface IJobExecutionMiddleware
{
    ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default);
}
```

`next` is the rest of the chain, ending in the job. Await it to run the job; if you do not, the job does
not run. Awaiting it twice runs the job twice in one firing; that is not a retry (see
[Translating exceptions](#translating-exceptions)).

## Writing one

<!-- snippet: sample_job_middleware_log_scope -->
```csharp
public sealed class LogScopeMiddleware(ILogger<LogScopeMiddleware> logger) : IJobExecutionMiddleware
{
    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["JobKey"] = context.JobDetail.Key,
            ["FireInstanceId"] = context.FireInstanceId,
        });

        await next(context, cancellationToken);
    }
}
```
<!-- endSnippet -->

## Registering

Register middleware where the scheduler is configured, in the same three shapes as listeners: built by
the container, built by you from the container, or an instance you already have.

<!-- snippet: sample_job_middleware_register -->
```csharp
builder.AddQuartz(q =>
{
    // built by the container, so it can take dependencies of its own
    q.AddJobMiddleware<LogScopeMiddleware>();

    // built by you, from this scheduler's services
    q.AddJobMiddleware(provider => new MeteredMiddleware(provider.GetRequiredService<IMeterFactory>()));

    // one you already have
    q.AddJobMiddleware(new TenantScopeMiddleware());
});
```
<!-- endSnippet -->

The standalone builder takes the same calls, because it is an `IQuartzBuilder`:

<!-- snippet: sample_job_middleware_standalone -->
```csharp
IScheduler scheduler = await QuartzSchedulerBuilder
    .Create(q => q
        .UseInMemoryStore()
        .AddJobMiddleware<LogScopeMiddleware>())
    .BuildScheduler();
```
<!-- endSnippet -->

::: tip
Registering a middleware for `AddQuartz("reporting", …)` puts it in that scheduler's pipeline only,
like its listeners and job store.
:::

::: warning
A middleware is built **once, from the container's root**, with the scheduler's other resources. Its
constructor dependencies must be singletons. A scoped one throws
`Cannot resolve scoped service … from root provider` where scope validation is on (the Host's default in
Development), and elsewhere becomes a captive dependency that lives as long as the scheduler.

Take an `IServiceScopeFactory` and open a scope inside `Invoke` instead:

<!-- snippet: sample_job_middleware_scoped -->
```csharp
public sealed class AuditMiddleware(IServiceScopeFactory scopeFactory) : IJobExecutionMiddleware
{
    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        // A middleware is built once, from the container's root, so a scoped service cannot be a
        // constructor parameter. Resolve it per firing instead.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        AuditLog audit = scope.ServiceProvider.GetRequiredService<AuditLog>();

        await audit.Starting(context.JobDetail.Key, cancellationToken);
        await next(context, cancellationToken);
    }
}
```
<!-- endSnippet -->

The firing's own scope, the one the job was resolved from, is reachable through
`IJobExecutionContextAccessor`; see [Per-firing state](#per-firing-state).
:::

## Order

Middleware runs in registration order, **outermost first**. The first registered sees the firing before
the second and sees its result after it. Plan a log scope or a transaction around this:

```text
q.AddJobMiddleware<A>();     A ─┐
q.AddJobMiddleware<B>();        B ─┐
                                   job
                                B ─┘
                             A ─┘
```

* Each call adds a stage; registering a type twice puts it in the chain twice.
* The chain is composed **once**, when the scheduler is built, and one instance of each middleware serves
  every firing. Keep no per-firing state in a field; see [Per-firing state](#per-firing-state).
* A middleware registered through `ConfigureAllQuartzSchedulers` always runs **inside** one registered in
  a scheduler's own `AddQuartz` callback, whichever call comes first. So what a library embedding Quartz
  wraps (an outbox, a unit of work) runs inside what the application wraps (its tenant scope), and a
  library cannot change that by registering earlier.

## Where it runs

| Middleware runs | So |
| -- | -- |
| after the trigger and job listeners have been notified | a fire a listener vetoed never reaches the pipeline |
| inside the execution span and the duration measurement | its cost counts as the firing's, and anything it traces is a child of `Quartz.Job.Execute` |
| outside the run shell's exception handling | what it throws is classified as though the job threw it |
| inside the store's concurrency handling | `[DisallowConcurrentExecution]` is enforced above the pipeline, so it never sees two firings of one job overlap |

## Short-circuiting

A middleware that does not call `next` stops the job from running. Everything else about the firing is
unchanged: listeners are notified as usual, and the trigger is left where a successful execution leaves
it.

<!-- snippet: sample_job_middleware_short_circuit -->
```csharp
public sealed class FeatureFlagMiddleware(FeatureFlags flags) : IJobExecutionMiddleware
{
    public ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        // Not calling next means the job does not run. The firing still completes, the listeners are
        // still notified, and the trigger is left where a successful execution leaves it.
        return flags.IsEnabled(context.JobDetail.Key) ? next(context, cancellationToken) : default;
    }
}
```
<!-- endSnippet -->

This is not a veto. A trigger listener's `VetoJobExecution` is the *scheduler's* refusal: it raises
`JobExecutionVetoed` and ends the firing. A middleware that skips the job is invisible from outside.

## Translating exceptions

A middleware runs outside the run shell's exception classification. A `JobExecutionException` it throws
is honoured like one the job raised, including `RefireImmediately` and the unschedule flags, and a plain
exception is wrapped the same way. Use middleware to tell Quartz what a library's failures mean:

<!-- snippet: sample_job_middleware_translate -->
```csharp
public sealed class TransientFailureMiddleware : IJobExecutionMiddleware
{
    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        try
        {
            await next(context, cancellationToken);
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            // Quartz understands this; it does not understand HttpRequestException.
            throw new JobExecutionException(e) { RefireImmediately = true };
        }
    }
}
```
<!-- endSnippet -->

::: warning
Catching a failure and awaiting a delay before calling `next` again is not a retry. It holds a
thread-pool slot for the whole wait, and the attempt is lost if the process stops. Use a trigger's
[retry policy](../how-tos/retrying-failed-jobs.md).
:::

## Per-firing state

One middleware instance serves every firing, so do not keep firing state in a field. Use one of these:

**An `AsyncLocal<T>`.** The value travels with the execution context, so the job and everything it calls
read the value their own firing set:

<!-- snippet: sample_job_middleware_ambient -->
```csharp
public sealed class TenantScopeMiddleware : IJobExecutionMiddleware
{
    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        // An AsyncLocal, not a field: one instance of this middleware serves every firing the scheduler
        // performs, and several of them can be in flight at once.
        TenantScope.Current.Value = context.Trigger.Key.Group;
        try
        {
            await next(context, cancellationToken);
        }
        finally
        {
            TenantScope.Current.Value = null;
        }
    }
}
```
<!-- endSnippet -->

**The job's dependency-injection scope.** `ConfigureJobScope` runs once per firing, before anything in
the scope is resolved, and receives the `TriggerFiredBundle` (see also
[More About Jobs](more-about-jobs.md#jobfactory)):

<!-- snippet: sample_job_middleware_job_scope -->
```csharp
builder.AddQuartz(q =>
{
    // Populated once per firing, before anything in the job's scope is resolved.
    q.ConfigureJobScope((scope, bundle, scheduler) =>
        scope.ServiceProvider.GetRequiredService<TenantHolder>().Tenant = bundle.Trigger.Key.Group);
});
```
<!-- endSnippet -->

`Invoke` is not passed an `IServiceScope`, because the scope belongs to the firing, not to a middleware.
Code that needs the firing reads `IJobExecutionContextAccessor.Current`, which is set for the whole
execution, including inside the pipeline on the way in and out.

**`context.MergedJobDataMap`,** to pass something to a *listener*. An `AsyncLocal` does not reach one:
an async method restores its caller's execution context, so a value set in `Invoke` is gone when the run
shell notifies listeners.

* The merged map is this firing's own copy of the job's and trigger's data, shared by everything that
  holds the context. A value put in it is visible to the job, the rest of the pipeline and the listeners
  for the whole firing.
* Writing to it persists nothing; neither stored map is touched.
* Data that must outlive the firing goes in `context.JobDetail.JobDataMap` on a job marked
  `[PersistJobDataAfterExecution]`, which the job store writes back.

## The cancellation token

Forward the token you were given. Passing a different one to `next` changes the job's `Execute`
parameter but not `IJobExecutionContext.CancellationToken`, so a job that reads the context sees the
wrong token. For that reason the built-in timeout interrupts the firing instead of passing a new token.

## Timing a job out

`AddJobTimeout` registers the timeout middleware Quartz ships. It replaces `JobInterruptMonitorPlugin`,
which is gone in 4.0 along with its `"AutoInterruptable"` and `"MaxRunTime"` job-data-map keys.

<!-- snippet: sample_job_timeout_register -->
```csharp
builder.AddQuartz(q =>
{
    // every job gets five minutes, unless it says otherwise
    q.AddJobTimeout(TimeSpan.FromMinutes(5));

    // or: no scheduler-wide budget, and only the jobs carrying [JobTimeout] are bounded
    q.AddJobTimeout();
});
```
<!-- endSnippet -->

A job sets its own budget with `[JobTimeout]`, as it declares `[DisallowConcurrentExecution]`. The
attribute is inherited from a base class or from an interface the job implements, so a contract can set
the budget for everything that implements it:

<!-- snippet: sample_job_timeout_attribute -->
```csharp
// Thirty seconds for this job, whatever the scheduler's default is.
[JobTimeout("00:00:30")]
public sealed class ReportJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // Forward the token: a job that never looks at it cannot be stopped by anything, and is simply
        // reported as having timed out once it finally returns.
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }
}

// No timeout at all, whatever the scheduler's default is.
[JobTimeout("00:00:00")]
public sealed class NightlyRebuildJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}
```
<!-- endSnippet -->

**Precedence:**

1. The job type's `[JobTimeout]`, whenever there is one. Zero means no timeout: the job is exempt from
   the scheduler-wide default.
2. Otherwise, `AddJobTimeout`'s argument.
3. Without an argument, or when `AddJobTimeout` was never called, nothing is bounded.

**What a timeout does:**

1. The firing is interrupted through `IScheduler.InterruptFireInstance`, the same path as an operator's
   interrupt. `IJobExecutionContext.CancellationToken`, the token the job holds, is cancelled and
   `ISchedulerListener.JobInterrupted` is raised.
2. Only the firing that overran is interrupted, by its fire instance id; two concurrent executions of one
   job are timed separately.
3. The middleware raises a `JobExecutionException` naming the budget. Without it the timeout would look
   like success, because the run shell treats a cancelled context token as a completed firing: no
   listener would hear of it, no error would be produced, and nothing would retry.

**A timeout is a retryable failure.** It arrives as a `JobExecutionException`, so the trigger's
[`RetryPolicy`](../how-tos/retrying-failed-jobs.md) decides what happens next, as for any failure. A
retry is a new acquisition with a new fire instance, so the pipeline runs again with the full budget.

::: warning
**A job that ignores its `CancellationToken` cannot be stopped.** Cancellation is cooperative and
nothing in .NET aborts running code. Such a job runs to completion, holding its thread-pool slot, and is
reported as timed out only when it returns, so a budget does not free capacity for it. Turn on
`CA2016`, the analyzer that flags a job not forwarding its token.

An exception the job threw that is *not* a cancellation is kept even when the budget had expired,
because it says more about what went wrong. The overrun is logged either way.
:::

## Middleware or a listener?

| Use middleware when you need to | Use a listener when you need to |
| -- | -- |
| wrap the execution — a scope, a stopwatch, a transaction | be told that something happened |
| decide whether the job runs at all | veto a fire (`ITriggerListener.VetoJobExecution`) |
| catch or translate what the job threw | react to the failure the run shell reports |
| set ambient state the job will read | react to scheduling events that are not executions at all — a trigger paused, the scheduler shutting down |
| act only on this scheduler's job executions | select which jobs or triggers you hear about, with a matcher |

Listeners stay notification-only. The two compose: a middleware does its work and a listener records
what happened.
