---

title: 'Job Execution Middleware'
---

# Job Execution Middleware

Middleware wraps every job a scheduler executes. It is where a cross-cutting concern lives — a log
scope, a tenant context, a metric, a translation of what a third-party library throws — when that
concern has to *surround* the call to the job rather than merely hear about it.

Listeners cannot do this. An `IJobListener` is notified before the job runs and again after it has
run, but the execution happens *between* the two notifications rather than *inside* them, so a
listener cannot open an `await using` around it, cannot decline to run it, and cannot catch what it
threw. Before 4.0 the only place left for such code was a job that wrapped another job, which is why
several frameworks built on Quartz ship exactly that adapter.

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

`next` is the rest of the chain, ending in the job. Await it to run the job; do not, and the job does
not run. Awaiting it twice is legal and runs the job twice inside the one firing — see
[Translating exceptions](#translating-exceptions) for why that is not how you retry.

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

Middleware belongs to a scheduler, so it is registered where the scheduler is configured. The same
three shapes listeners have: the container builds it, you build it from the container, or you hand
over one you already have.

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

The standalone builder takes the same calls, because it *is* an `IQuartzBuilder`:

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
Registering a middleware for `AddQuartz("reporting", …)` puts it in that scheduler's pipeline alone.
A named scheduler's middleware is its own, the way its listeners and its job store are.
:::

::: warning
A middleware is built **once, from the container's root**, when the scheduler's resources are. Its
constructor dependencies must therefore be singletons: a scoped one throws
`Cannot resolve scoped service … from root provider` where scope validation is on — the Host's default
in Development — and becomes a captive dependency living as long as the scheduler where it is not. The
name is ASP.NET Core's, but the lifetime is a listener's.

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

The firing's own scope — the one the job was resolved from — is reachable through
`IJobExecutionContextAccessor`; see [Per-firing state](#per-firing-state).
:::

## Order

Middleware runs in registration order, **outermost first**. The first registered sees the firing
before the second does and sees its result after it, which is the ordering a log scope or a
transaction has to be planned around:

```text
q.AddJobMiddleware<A>();     A ─┐
q.AddJobMiddleware<B>();        B ─┐
                                   job
                                B ─┘
                             A ─┘
```

Each call adds a stage, so registering the same type twice puts it in the chain twice.

The chain is composed **once**, when the scheduler is built, and one instance of each middleware
serves every firing that scheduler performs. A middleware must therefore keep no per-firing state in
a field — see [Per-firing state](#per-firing-state) below.

A middleware registered through `ConfigureAllQuartzSchedulers` always composes **inside** one
registered in a scheduler's own `AddQuartz` callback, whichever of the two calls was written first: a
scheduler's own configuration runs before what every scheduler was told. That is deliberate, and it is
what a library embedding Quartz relies on — what the library wraps, such as an outbox or a unit of
work, belongs inside what the application wraps, such as its tenant scope. Because the ordering does not
depend on the order of the calls, a library cannot change it by being registered earlier.

## Where it runs

| | |
| -- | -- |
| after the trigger and job listeners have been notified | a fire a listener vetoed never reaches the pipeline |
| inside the execution span and the duration measurement | what a middleware costs is part of what the firing cost, and anything it traces is a child of `Quartz.Job.Execute` |
| outside the run shell's exception handling | what a middleware throws is classified exactly as though the job had thrown it |
| inside the store's concurrency handling | `[DisallowConcurrentExecution]` is enforced above the pipeline, so a middleware never sees two firings of one job overlapping |

## Short-circuiting

A middleware that does not call `next` keeps the call to itself. The job does not run; everything
else about the firing is unchanged — the listeners are notified as usual, and the trigger is left
where a successful execution leaves it.

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
`JobExecutionVetoed`, and the firing ends there. A middleware that declines is invisible from
outside.

## Translating exceptions

A middleware runs outside the run shell's exception classification, so a `JobExecutionException` it
throws is honoured exactly like one the job raised — including `RefireImmediately` and the unschedule
flags — and a plain exception is wrapped the same way. That makes middleware the place to teach
Quartz what a library's own failures mean:

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
thread-pool slot for the whole wait, and the attempt is lost if the process stops. A trigger's retry
policy is the tool for that.
:::

## Per-firing state

One middleware instance serves every firing, so a field is the wrong place to keep anything about the
firing in hand. Two things that are the right place:

**An `AsyncLocal<T>`.** The value travels with the execution context, so the job and everything it
calls read the one their own firing set:

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

**The job's dependency-injection scope.** `ConfigureJobScope` runs once per firing, before anything
in the scope is resolved, and is handed the `TriggerFiredBundle`:

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

No `IServiceScope` is threaded through `Invoke`, deliberately: the scope belongs to the firing rather
than to any one middleware, and code that needs the firing itself can read it from
`IJobExecutionContextAccessor.Current`, which is set for the whole execution — including inside the
pipeline, on the way in and on the way out.

**`context.MergedJobDataMap`,** to hand something to a *listener*. An `AsyncLocal` does not reach one:
an async method restores its caller's execution context, so what a middleware sets inside `Invoke` is
gone by the time the run shell notifies listeners. The merged map is not — it is this firing's own copy
of the job's and the trigger's data, built once and shared by everything that holds the context, so a
value put into it is visible to the job, to the rest of the pipeline and to the listeners for as long
as the firing lasts. Writing to it is safe and persists nothing: neither the job's nor the trigger's
stored map is touched. Data that has to outlive the firing goes into `context.JobDetail.JobDataMap` on
a job marked `[PersistJobDataAfterExecution]`, which is what a job store writes back.

## The cancellation token

Forward the token you were given. Passing a different one to `next` changes what the job's `Execute`
parameter is without changing `IJobExecutionContext.CancellationToken`, so the two stop being the
same token and a job that reads the context sees the wrong one. That is the trap in writing a timeout
as a middleware — and it is why the built-in one interrupts the firing instead.

## Timing a job out

`AddJobTimeout` registers the middleware Quartz ships for exactly this. It replaces
`JobInterruptMonitorPlugin`, which is gone in 4.0 along with its `"AutoInterruptable"` and
`"MaxRunTime"` job-data-map keys.

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

A job varies the budget by declaring one, the way it declares `[DisallowConcurrentExecution]`. The
attribute is inherited from a base class or from an interface the job implements, so a contract can set
the budget for everything that fulfils it:

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

**Precedence.** The job type's `[JobTimeout]` decides whenever there is one — including when it says
zero, which means the job has no timeout and is exempt from the scheduler-wide default rather than
overruled by it. Without the attribute, `AddJobTimeout`'s argument decides; without an argument, and
on a scheduler that never called `AddJobTimeout` at all, nothing is bounded.

**What a timeout does.** When the budget is spent, the firing is interrupted through
`IScheduler.InterruptFireInstance` — the same path an operator's interrupt takes, so
`IJobExecutionContext.CancellationToken` (the very token the job holds) is cancelled and
`ISchedulerListener.JobInterrupted` is raised. Only the firing that overran is interrupted, because it
is named by its fire instance id: two concurrent executions of one job are timed separately. The
middleware then raises a `JobExecutionException` naming the budget. That second step is the point: an
interrupt on its own is *success-shaped*, because the run shell treats a cancellation of the context's
token as a completed firing, so without it a timeout would reach no listener, produce no error, and
never be retried.

**A timeout is a retryable failure.** Because it arrives as a `JobExecutionException`, the trigger's
[`RetryPolicy`](../how-tos/retrying-failed-jobs.md) decides what happens next, exactly as it would for any other
failure. A retry is an ordinary re-acquisition with a new fire instance, so the pipeline runs again and
each attempt is handed the whole budget afresh.

::: warning
**A job that ignores its `CancellationToken` cannot be stopped.** Cancellation is cooperative and
nothing in .NET aborts running code. Such a job runs to completion, holding its thread-pool slot, and is
reported as timed out only when it finally returns — which is worth knowing before a budget is relied on
to free capacity. `CA2016` is the analyzer that flags a job failing to forward the token it was handed;
turn it on.

An exception the job threw that is *not* a cancellation is left alone even when the budget had expired:
it says more about what went wrong than the timeout does. The overrun is logged either way.
:::

## Middleware or a listener?

| Use middleware when you need to | Use a listener when you need to |
| -- | -- |
| wrap the execution — a scope, a stopwatch, a transaction | be told that something happened |
| decide whether the job runs at all | veto a fire (`ITriggerListener.VetoJobExecution`) |
| catch or translate what the job threw | react to the failure the run shell reports |
| set ambient state the job will read | react to scheduling events that are not executions at all — a trigger paused, the scheduler shutting down |
| act only on this scheduler's job executions | select which jobs or triggers you hear about, with a matcher |

Listeners stay notification-only, and none of this changes them. The two compose: a middleware can do
its work and a listener can still record what happened.

## See also

* [Trigger and Job Listeners](trigger-and-job-listeners.md)
* [More About Jobs](more-about-jobs.md) — job scopes and `ConfigureJobScope`
* [Retrying failed jobs](../how-tos/retrying-failed-jobs.md) — what a trigger does with the failure a
  timeout raises
