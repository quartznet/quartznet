---

title: 'Embedding Quartz in a Library'
---

# Embedding Quartz in a Library

For authors of a **library** that schedules work (a message bus integration, token pruning, a multi-tenant
framework, an outbox) inside an application that owns the scheduler. [Quartz.NET with Wolverine](wolverine.md)
applies the same advice to one real library.

## Who owns the scheduler

| Arrangement | Use it when |
|---|---|
| **Your own named scheduler** — `AddQuartz("mylib", …)` | the work needs its own thread pool, store, start and stop |
| **Contribute to whatever schedulers exist** — `ConfigureAllQuartzSchedulers(…)` | the work is small, periodic housekeeping |
| **An execution group inside a shared scheduler** | you share the application's scheduler but cap your share |

A named scheduler isolates your load from the application's, at the cost of a second thread pool,
acquisition loop and thing to watch. Contributing costs nothing to operate and isolates nothing. Most
libraries want the third: one scheduler, with a named [execution group](../tutorial/execution-groups.md)
and a limit.

::: warning Never create a scheduler the container does not know about
A `QuartzSchedulerBuilder` inside a library creates a scheduler that `ISchedulerRegistry`, the dashboard,
the health check and the host's shutdown cannot see. Two libraries doing it against the same tables each
recover the other's fired triggers at start-up. Register through the application's container.
:::

### A scheduler of your own

<!-- snippet: sample_embedding_named_scheduler -->
```csharp
// Everything this library registers lands under the scheduler's own service key: its thread
// pool, its job store, its listeners. Nothing it does can starve the application's scheduler,
// and nothing the application configures reaches this one.
services.AddQuartz("acme.outbox", q =>
{
    q.UsePersistentStore(store => store.UseSqlServer(connectionString));
    q.UseDefaultThreadPool(maxConcurrency: 4);
});
```
<!-- endSnippet -->

Its thread pool, job store, listeners and middleware are keyed by its name. Resolve it as a keyed service:

<!-- snippet: sample_embedding_named_scheduler_resolve -->
```csharp
IScheduler scheduler = provider.GetRequiredKeyedService<IScheduler>("acme.outbox");
```
<!-- endSnippet -->

Use a name operators will recognise as yours, such as a reverse-DNS prefix. A second scheduler under the
same name (case-insensitive) is refused at registration. See
[Multiple Schedulers](../packages/multiple-schedulers.md).

### An execution group, if you are sharing

Set the limit once by group name; every trigger in that group counts against it:

<!-- snippet: sample_embedding_execution_group -->
```csharp
services.ConfigureAllQuartzSchedulers(q => q.UseExecutionLimits(limits => limits
    .ForGroup("acme.outbox", maxConcurrent: 4)
    .ForOtherGroups(int.MaxValue)));
```
<!-- endSnippet -->

Tag triggers with `WithExecutionGroup("acme.outbox")` or `OneOffJobOptions.ExecutionGroup`. Choose between
a node-scoped and a cluster-scoped limit in [Execution Groups](../tutorial/execution-groups.md).

## Contributing to a scheduler you do not own

`ConfigureAllQuartzSchedulers` applies a delegate to every scheduler already registered, and to each one
registered later as part of its own `AddQuartz`. The library need not know how many schedulers there are,
their names, or whether they were registered first.

<!-- snippet: sample_embedding_contributor -->
```csharp
public static IServiceCollection AddAcmeOutbox(this IServiceCollection services)
{
    // Contributing twice is contributing twice: Quartz will not apply one delegate instance to one
    // scheduler more than once, but a second call here creates a second delegate. Guard the
    // extension method, not the delegate.
    if (services.Any(descriptor => descriptor.ServiceType == typeof(AcmeOutboxMarker)))
    {
        return services;
    }

    services.AddSingleton<AcmeOutboxMarker>();

    // Applied to every scheduler in the container - those registered before this call, and those
    // registered after it. The application decides how many schedulers there are and what they are
    // called; this does not have to know.
    services.ConfigureAllQuartzSchedulers(q =>
    {
        q.AddJob<DrainOutboxJob>(j => j
            .WithIdentity(DrainOutboxJob.Key)
            .StoreDurably());

        q.AddTrigger(t => t
            .WithIdentity("drain", DrainOutboxJob.Key.Group)
            .ForJob(DrainOutboxJob.Key)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(30)).RepeatForever()));
    });

    return services;
}

private sealed class AcmeOutboxMarker;
```
<!-- endSnippet -->

* The delegate gets a builder **per scheduler**, so what it registers lands under that scheduler's key,
  as if written inside its `AddQuartz(name, q => …)`. A listener or plugin added this way is one instance
  per scheduler.
* It runs after each scheduler's own callback. Registration is first-wins (the application's job store is
  kept); options are last-wins (your values override the application's).
* **Guard the extension method, not the delegate.** Quartz never applies one delegate *instance* to a
  scheduler twice (so a second `AddQuartz()` does not double your registrations), but a capturing lambda
  is a new delegate on every call. Copy the marker-service check.
* Remote schedulers from `AddQuartzHttpClient` are skipped. With no scheduler registered the call is not an
  error, so call it unconditionally.

::: tip This replaces 3.x's `IConfigureOptions<QuartzOptions>`
On 3.x a library registered an `IConfigureOptions<QuartzOptions>` and called `QuartzOptions.AddJob` /
`AddTrigger`, as OpenIddict's
[Quartz integration](https://github.com/openiddict/openiddict-core/blob/dev/src/OpenIddict.Quartz/OpenIddictQuartzConfiguration.cs)
does. `QuartzOptions`
[no longer holds jobs and triggers](../migration-guide.md#quartzoptions-is-no-longer-a-dictionary).
`ConfigureAllQuartzSchedulers` replaces it, and also reaches named schedulers.
:::

For one scheduler only, check `IQuartzBuilder.SchedulerName`: the empty string for the default scheduler,
the registered name otherwise.

<!-- snippet: sample_embedding_contributor_scheduler_name -->
```csharp
public static IServiceCollection AddAcmeOutboxToOneScheduler(this IServiceCollection services, string schedulerName)
{
    services.ConfigureAllQuartzSchedulers(q =>
    {
        // "" is the default scheduler; anything else is the name it was registered under.
        if (!string.Equals(q.SchedulerName, schedulerName, StringComparison.Ordinal))
        {
            return;
        }

        q.AddJob<DrainOutboxJob>(j => j.WithIdentity(DrainOutboxJob.Key).StoreDurably());
    });

    return services;
}
```
<!-- endSnippet -->

## Settings your consumers can set

Use the options pipeline, not a static, a singleton or a second builder. `q.ConfigureOptions<TOptions>(…)`
registers a callback under **this scheduler's** options name, so a job, listener, plugin or job store built
for that scheduler receives *its* scheduler's values:

<!-- snippet: sample_embedding_library_options -->
```csharp
public static IServiceCollection AddAcmeOutboxScheduler(
    this IServiceCollection services,
    Action<AcmeOutboxOptions>? configure = null)
{
    services.AddQuartz("acme.outbox", q =>
    {
        // The library's own settings, named for this scheduler. A component the container
        // builds for "acme.outbox" and taking IOptions<AcmeOutboxOptions> is handed these.
        q.ConfigureOptions<AcmeOutboxOptions>(options =>
        {
            options.DrainInterval = TimeSpan.FromSeconds(30);
            configure?.Invoke(options);
        });

        // AddPlugin<T, TOptions> is the same thing said for a plugin
        q.AddPlugin<AcmeOutboxPlugin, AcmeOutboxOptions>(name: "acmeOutbox");
    });

    return services;
}
```
<!-- endSnippet -->

`AddPlugin<TPlugin, TOptions>(configure, name)` does the same for a plugin. Consumers configure the named
options from `appsettings.json` with `services.Configure<AcmeOutboxOptions>("acme.outbox", section)`, or
from code with another `ConfigureOptions` call, in either order.

## What your scheduler costs an operator

State these in your readme:

* **A schema, if the store is persistent.** Every row carries `SCHED_NAME`, so your scheduler can share
  the consumer's tables and `TablePrefix`; say whether you assume that. Do not default to
  [`ProvisionSchema()`](../tutorial/job-stores.md#creating-the-schema): production databases often deny
  DDL. See [Database Schema](../db/) and [Shared database](../multi-tenancy.md#shared-database).
* **A separate entry** in `ISchedulerRegistry`, the [dashboard's](../packages/dashboard.md) scheduler
  picker and `GET /schedulers`. Name it recognisably: `acme.outbox`, not `default2`.
* **A health check, if wanted.** A named scheduler gets none by default. Add
  `AddQuartz("acme.outbox", q => q.AddQuartzHealthChecks())`; see
  [Health checks](../packages/hosted-services-integration.md#health-checks).

## Deriving keys

* **The job key says what runs.** A job detail is a definition, so one durable job per type is enough. The
  typed `ScheduleJob` overloads store it at `SchedulerConstants.ScheduledJobKey<TJob>()`, which is
  `(typeof(TJob).Name, SchedulerConstants.ScheduledJobGroup)`. Ask for this key rather than re-deriving it;
  the group is reserved.
* **The trigger key says which occurrence.** Its **name** is the firing; its **group is the correlation
  id** (saga, conversation, tenant), so the set can be listed, paused or cancelled together.

<!-- snippet: sample_embedding_typed_job -->
```csharp
public sealed record SendReminder(string ConversationId, string MessageId, string Text);

public sealed class SendReminderJob(IReminderSink sink) : IJob<SendReminder>
{
    public ValueTask Execute(
        IJobExecutionContext context,
        SendReminder input,
        CancellationToken cancellationToken = default)
    {
        return sink.Send(input.ConversationId, input.Text, cancellationToken);
    }
}
```
<!-- endSnippet -->

<!-- snippet: sample_embedding_schedule_correlated -->
```csharp
public sealed class Conversations(IScheduler scheduler)
{
    public async ValueTask<TriggerKey> Remind(SendReminder reminder, TimeSpan delay, CancellationToken cancellationToken)
    {
        ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<SendReminderJob, SendReminder>(
            reminder,
            delay,
            new OneOffJobOptions
            {
                // The name is this one firing; the group is what the firing is about. Both are the
                // library's own identifiers, so nothing has to be looked up to cancel later.
                Name = reminder.MessageId,
                Group = reminder.ConversationId,
                Replace = true
            },
            cancellationToken);

        // The call answers with the key it stored and the time the store will first fire it at.
        return scheduled.TriggerKey;
    }

    public ValueTask<bool> Cancel(TriggerKey firing, CancellationToken cancellationToken)
    {
        return scheduler.UnscheduleJob(firing, cancellationToken);
    }

    public ValueTask<List<TriggerKey>> CancelConversation(string conversationId, CancellationToken cancellationToken)
    {
        // Everything still scheduled for that conversation, in one store operation, answering with
        // the keys it removed.
        return scheduler.UnscheduleJobs(GroupMatcher<TriggerKey>.GroupEquals(conversationId), cancellationToken);
    }
}
```
<!-- endSnippet -->

* `Replace = true` turns a repeat of the same name into an update instead of an
  `ObjectAlreadyExistsException`.
* Derive the name from what the caller already has (message id, saga id plus step), so nothing needs
  storing.
* `UnscheduleJobs` by group is one atomic call. Listing then deleting leaves a window in which another node
  can schedule into the group. `DeleteJobs(GroupMatcher<JobKey>)` is the same for jobs; both return the
  keys removed.

::: warning Do not put the correlation id in the job key
The trigger is the occurrence. A job per correlation id adds a row and a delete per saga for nothing.
:::

### Why this is worth using rather than reinventing

Most systems return one opaque handle per item, and cancelling a set means keeping your own index:

| System | Handle | Cancel a set |
|---|---|---|
| Hangfire | job id | own index |
| Azure Service Bus | sequence number | own index |
| MassTransit one-shot | scheduled-message token | own index |
| MassTransit [recurring](https://masstransit.io/documentation/configuration/scheduling) | `ScheduleId`, `ScheduleGroup` (Quartz's trigger name and group) | by group |
| NServiceBus [saga timeouts](https://docs.particular.net/nservicebus/sagas/timeouts) | none; cannot be rescheduled or revoked | the saga ignores it on arrival |
| Temporal | workflow id | Search Attributes |

* Deciding at the firing whether the work is still wanted is always an option, and cannot race. Cancel by
  group when the work is expensive or the set large.
* Derive ids from the business object, as Temporal recommends for a
  [Workflow ID](https://docs.temporal.io/workflow-execution/workflowid-runid) and as Hangfire's recurring
  jobs do with the id `AddOrUpdate` upserts on.

## A typed input, not a payload bag

`IJob<TInput>` serializes the input into the trigger's data under one reserved key and hands it to the job
typed; `MergedJobDataMap.GetString("CustomerId")` is unchecked. Treat the input like a message:

* **Put ids in it, not entities.** It is read later, possibly by newer code (Hangfire's
  [passing arguments](https://docs.hangfire.io/en/latest/background-methods/passing-arguments.html);
  Temporal's claim check, driven by [payload limits](https://docs.temporal.io/dataconversion)).
* **Evolve it like a message.** Adding an optional member is safe; renaming or removing one breaks every
  stored firing. There is no schema version unless you add one.

See [A typed input](../tutorial/job-data-map.md#a-typed-input-the-third-read-side) and
[What does not belong in job data](../tutorial/job-data-map.md#what-does-not-belong-in-job-data).

## Scheduling over what is already there

Do not check, unschedule, then schedule: three round trips and a race. The `ScheduleJob` overloads taking
`ScheduleJobOptions` replace inside the store's lock:

<!-- snippet: sample_embedding_upsert -->
```csharp
ITrigger trigger = TriggerBuilder.Create<SendReminderJob>(scheduler.TimeProvider)
    .WithIdentity(reminder.MessageId, reminder.ConversationId)
    .ForJob(SchedulerConstants.ScheduledJobKey<SendReminderJob>())
    .StartAt(at)
    .UsingInput(reminder)
    .Build();

await scheduler.ScheduleJob(trigger, ScheduleJobOptions.Replacing, cancellationToken);
```
<!-- endSnippet -->

`ScheduleJobOptions.Replacing` and `AddJobOptions.Replacing` are the common values. The typed one-call
overloads do the same with `OneOffJobOptions.Replace`. A replaced trigger keeps its previous fire time; see
[One-Off Job](one-off-job.md#scheduling-over-a-firing-that-is-already-there).

## Starting at your own moment

When the scheduler must wait for a bus connection, a leader lease or a migration, set `AutoStart = false`.
The host builds, initializes and binds it, and leaves it in `Created` for you to start:

<!-- snippet: sample_embedding_deferred_start -->
```csharp
builder.Services.AddQuartz("acme.outbox", q => q.UseInMemoryStore());

// Built, initialized and bound with the host, and then left in Created. The library starts it
// when whatever it depends on - a bus connection, a leader lease, a migration - is ready.
builder.Services.AddQuartzHostedService("acme.outbox", options => options.AutoStart = false);
```
<!-- endSnippet -->

Do not omit the hosted service instead: you would lose shutdown handling. The scheduler stays in
`ISchedulerRegistry` and on the dashboard.

Health:

* While waiting it reports **degraded**, not unhealthy, so the probe does not remove a correctly
  configured node. The check reads that scheduler's own `QuartzHostedServiceOptions`, so a `Created`
  scheduler that did not opt out of `AutoStart` still reports unhealthy.
* This is Quartz's choice: Microsoft's readiness sample and MassTransit report unhealthy while starting. By
  default *degraded* answers HTTP 200 and *unhealthy* 503.
* `QuartzHealthCheckOptions.StandbyStatus` sets the verdict for **standby** only; the `Created` window stays
  degraded. `FailureStatus` sets what the *registration* reports on failure.
* Over HTTP, remap at the probe with `HealthCheckOptions.ResultStatusCodes` (not possible in a worker
  project with no endpoint).

See [Health checks](../packages/hosted-services-integration.md#health-checks), and
[Running under an External Leader Election](external-leader.md) when an election decides the moment.

## Cross-cutting concerns are middleware

Use `IJobExecutionMiddleware` to wrap a firing in a log scope, tenant context, unit of work, consume
context, or exception translation. On 3.x libraries (ABP, Elsa, Brighter) shipped adapter jobs instead,
because `IJobListener` only notifies before and after. Adapter jobs:

* **record the wrapper** in `JOB_CLASS_NAME`, so listings name the adapter; ABP's generic
  `QuartzPeriodicBackgroundWorkerAdapter<T>` overflowed the column's 250 characters
  ([abp#4609](https://github.com/abpframework/abp/issues/4609));
* **lose the inner job's `[DisallowConcurrentExecution]` and `[PersistJobDataAfterExecution]`**, which
  Quartz reads from the type it was given, its base types and interfaces;
* **bypass the job factory and DI scope**, and break interruption unless they forward the token;
* **do not compose**: two libraries cannot both wrap one firing.

<!-- snippet: sample_embedding_middleware -->
```csharp
public sealed class OutboxScopeMiddleware(IOutboxContext outbox) : IJobExecutionMiddleware
{
    public async ValueTask Invoke(
        IJobExecutionContext context,
        JobExecutionDelegate next,
        CancellationToken cancellationToken = default)
    {
        // Ambient state the library's own services read, established around the job rather than
        // inside a wrapper job that has to know how to construct the real one.
        using (outbox.Begin(context.FireInstanceId))
        {
            await next(context, cancellationToken);
        }
    }
}
```
<!-- endSnippet -->

<!-- snippet: sample_embedding_middleware_registration -->
```csharp
services.ConfigureAllQuartzSchedulers(q => q.AddJobMiddleware<OutboxScopeMiddleware>());
```
<!-- endSnippet -->

* Keyed per scheduler; runs in registration order, outermost first; composed once when the scheduler is
  built. Keep per-firing state in an `AsyncLocal<T>` or the job's scope, not a field.
* Built from the *root* container, so dependencies must be singletons. For scoped services, take an
  `IServiceScopeFactory` and open a scope in `Invoke`.
* Not calling `next` short-circuits the firing.
* Runs inside the execution span and duration measurement, outside the run shell's exception handling, so
  a `JobExecutionException` it throws is honoured.
* **Always inner to the application's middleware**: a scheduler's own `AddQuartz` callback runs before
  `ConfigureAllQuartzSchedulers` is applied, whatever the source order.

See [Job Execution Middleware](../tutorial/job-execution-middleware.md).

## Retry

Give the trigger a retry policy instead of writing a loop:

<!-- snippet: sample_embedding_retry_policy -->
```csharp
services.AddQuartz(q =>
{
    q.AddJob<DrainOutboxJob>(j => j.WithIdentity(DrainOutboxJob.Key).StoreDurably());
    q.AddTrigger<DrainOutboxJob>(t => t
        .ForJob(DrainOutboxJob.Key)
        .WithIdentity("drain-outbox", "acme.outbox")
        .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever())
        // Four attempts, backing off 10s, 20s, 40s, 80s - but never past the next minute's
        // occurrence, which supersedes a retry that would collide with it.
        .WithRetryPolicy(RetryPolicy.Exponential(4, TimeSpan.FromSeconds(10))));
});
```
<!-- endSnippet -->

Throwing asks for a retry. Waits persist with the trigger (surviving restart and failover), and
`context.RetryAttempt` gives the attempt number. See [Retrying Failed Jobs](retrying-failed-jobs.md).

```csharp
throw new JobExecutionException(ex) { RefireImmediately = true };
```

**`RefireImmediately` is not a retry.** It re-runs on the same thread with no delay, ceiling or backoff until
the job stops throwing; against a down database that is a tight loop. Use it only when the cause has already
gone. It outranks the trigger's policy.

For faults that clear in milliseconds, use a resilience pipeline inside the job
([Polly](https://www.pollydocs.org/) or `Microsoft.Extensions.Http.Resilience`). Use the trigger's policy for
faults that outlive the firing.

## Trace context across the scheduled gap

Quartz stores the W3C trace context current when the trigger was stored, under
`SchedulerConstants.TraceParent` and `SchedulerConstants.TraceState`. The firing's `Quartz.Job.Execute`
span links to it with an `ActivityLink`, not as a parent, per OpenTelemetry's
[messaging conventions](https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/). Nothing
needs configuring.

It costs two string entries per trigger's data map. To turn it off:

<!-- snippet: sample_embedding_trace_context_off -->
```csharp
services.ConfigureAllQuartzSchedulers(q =>
    q.ConfigureScheduler(options => options.PropagateTraceContext = false));
```
<!-- endSnippet -->

Use these keys for the trace part of any correlation you carry; do not add your own. See
[Linking a firing to what scheduled it](../packages/opentelemetry-integration.md#linking-a-firing-to-what-scheduled-it).

## Running inside somebody else's transaction

To commit your rows and the scheduling together, let the persistent store use the application's
connection and transaction:

<!-- snippet: sample_embedding_accept_enlisted -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);
    store.ConfigureStore(options => options.AcceptEnlistedTransactions = true);
}));
```
<!-- endSnippet -->

<!-- snippet: sample_embedding_enlist -->
```csharp
public sealed class Outbox(IScheduler scheduler)
{
    /// <summary>
    /// Schedules inside a transaction the caller owns, so the scheduling and whatever else that
    /// transaction did commit together or not at all.
    /// </summary>
    public async ValueTask Enqueue(
        DbTransaction transaction,
        SendReminder reminder,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        // The enlistment flows with the asynchronous context, so it has to be established in the
        // same scope as the calls it covers - which is why this takes the transaction rather than
        // establishing one and handing it back.
        using (scheduler.EnlistTransaction(transaction))
        {
            await scheduler.ScheduleJob<SendReminderJob, SendReminder>(
                reminder,
                at,
                new OneOffJobOptions { Name = reminder.MessageId, Group = reminder.ConversationId, Replace = true },
                cancellationToken);
        }
    }
}
```
<!-- endSnippet -->

Take the transaction as a parameter. The enlistment flows with the asynchronous context, so it must be in
the same scope as the calls it covers; set inside an `async` helper it does not flow back (the reason
`TransactionScope` needs `TransactionScopeAsyncFlowOption.Enabled`).

* **Same database.** The connection's provider must match the store's; otherwise the enlistment is refused.
  For different databases, use a transactional outbox in your own tables.
* **The application must opt in** with `AcceptEnlistedTransactions` (off by default; enlisting without it
  throws). A library cannot enable it without changing how the store locks, so document it and fail with a
  clear message.
* **Locks are held in your transaction** until commit, blocking acquisition, the misfire handler and
  cluster check-in on every node.
* **Persistent ADO.NET store only**; against `RAMJobStore` the call throws.

See [Joining an existing transaction](../tutorial/job-stores.md#joining-an-existing-transaction).

## Shipping a package that multi-targets

Quartz 4 targets only `net10.0`. To serve `net8.0` and `net9.0` consumers, use a conditional
`PackageReference`
([NuGet: target frameworks](https://learn.microsoft.com/nuget/create-packages/multiple-target-frameworks-project-file)):

<!-- Not a compiled sample: it is csproj rather than C#, and the point of it is the condition, which no
     sample project in this repository can carry twice. -->

```xml
<PropertyGroup>
  <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
</PropertyGroup>

<ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
  <PackageReference Include="Quartz" Version="4.0.0" />
</ItemGroup>

<ItemGroup Condition="'$(TargetFramework)' != 'net10.0'">
  <PackageReference Include="Quartz" Version="3.20.0" />
  <PackageReference Include="Quartz.Extensions.DependencyInjection" Version="3.20.0" />
  <PackageReference Include="Quartz.Extensions.Hosting" Version="3.20.0" />
</ItemGroup>
```

**This works only while your public surface is the same on every target.** Exposing an `IJobFactory`, a
clock, a scheduler you construct, or a `Task` that Quartz now wants as `ValueTask` makes the package two
libraries. Then ship a separate `net10.0`-only package or drop the old targets; do not add deeper `#if`s.

### What actually differs

The renames an integration package meets, checked against both branches' public API baselines. The
[migration guide's appendix](../migration-guide.md#appendix-what-happened-to-a-name) has the complete list.

| 3.x | 4.x | Note |
|---|---|---|
| `Task` / `Task<T>` on every asynchronous member | `ValueTask` / `ValueTask<T>` | only `QuartzHostedService`'s members still return `Task`, because `IHostedService` does |
| `ValueTask Execute(IJobExecutionContext context)` | `ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)` | the token is a parameter as well as a context member |
| `IScheduler.CheckExists(JobKey \| TriggerKey)` | `IScheduler.Exists(JobKey \| TriggerKey)` | |
| `MisfireInstruction.SimpleTrigger.FireNow` and the other nested `const int`s | `SimpleTriggerMisfireInstruction`, `CronTriggerMisfireInstruction`, `CalendarIntervalTriggerMisfireInstruction`, `DailyTimeIntervalTriggerMisfireInstruction`, `RecurrenceTriggerMisfireInstruction` | values unchanged; `IgnoreMisfirePolicy` is `IgnoreMisfires`, and cron's, calendar's and daily's `FireOnceNow` is `FireAndProceed` |
| `ITrigger.MisfireInstruction` (`int`) | `ITrigger.MisfireInstructionCode` (`int`) | plus a typed `MisfireInstruction` on each family interface |
| `trigger.GetNextFireTimeUtc()`, `GetPreviousFireTimeUtc()`, `GetMayFireAgain()` | the properties `NextFireTimeUtc`, `PreviousFireTimeUtc`, `MayFireAgain` | `FinalFireTimeUtc` was already a property; `GetFireTimeAfter(…)` is still a method |
| `new JobExecutionException(ex, refireImmediately: true)` | `new JobExecutionException(ex) { RefireImmediately = true }` | flags are `init`, the `bool` constructor overloads are gone, the type is sealed |
| `Quartz.Util.TimeZoneUtil.FindTimeZoneById(id)` | `Quartz.TimeZones.FindById(id)` | |
| `TimeZoneUtil.CustomResolver = resolver` | `TimeZones.AddResolver(resolver)` | returns an `IDisposable` that removes that one resolver |
| your own `TryGetValue<T>` extension on `JobDataMap` | **unchanged — keep it** | see below |
| `Quartz.Extensions.DependencyInjection`, `Quartz.Extensions.Hosting` (packages) | merged into `Quartz` | namespace was `Quartz` on both sides; only the `PackageReference` changes |
| `IServiceCollectionQuartzConfigurator` | `IQuartzBuilder` | |
| `scheduler.JobFactory = factory` (setter-only, on `IScheduler`) | `q.UseJobFactory(factory)` / `UseJobFactory<T>()` on the builder | or `ConfigureJobScope(…)` to seed the DI scope |
| `IJobFactory.NewJob(bundle, scheduler)` returning `IJob`; `ReturnJob(IJob)` | `CreateJob(bundle, scheduler, ct)` returning `ValueTask<JobScope>`; `ReturnJob(JobScope, ct)` | |
| `SystemTime.UtcNow = () => …` (a public mutable field) | `q.UseTimeProvider(provider)`, read back as `IScheduler.TimeProvider` | per scheduler, not process-wide |
| `StdSchedulerFactory` / `SchedulerBuilder` | `QuartzSchedulerBuilder.Create(q => …)` | its `Build()` gives a `StandaloneSchedulerFactory` |
| `TriggerBuilder.ModifiedByCalendar(name)` | `WithCalendarName(name)` | |
| `SchedulerMetaData` / `GetMetaData()` | `SchedulerMetadata` / `GetMetadata()` | lower-case `d` in both |
| `GetCurrentlyExecutingJobs()` | `QueryFireInstances(new FireInstanceQuery())` | returns `PagedResult<FireInstance>` |
| `IsStarted`, `InStandbyMode`, `IsShutdown` | one `SchedulerStatus Status` | |
| `Interrupt(fireInstanceId)` | `InterruptFireInstance(fireInstanceId)` | |
| `Quartz.Spi`, `Quartz.Simpl`, `Quartz.Impl.Matchers`, `Quartz.Util`, `Quartz.Listener` | `Quartz.Extensibility`, `Quartz.Impl`, `Quartz` (matchers and `Key<T>` are top-level now), dissolved, `Quartz.Listeners` | |
| `Quartz.Logging` — `ILogProvider`, `LogContext`, `LogLevel` | gone with LibLog; **no** replacement for the abstraction | `LogProvider` moved to `Quartz.Diagnostics` and takes an `ILoggerFactory` |

* **Keep your own `TryGetValue<T>` extension.** `JobDataMap` still implements
  `IDictionary<string, object?>`, so it still binds. 4.x's `TryGet<T>` is a pure `is T` test with no
  conversion and returns `false` for a value stored as a string; `TryGetInt` and its siblings convert.
* **A listener with 3.x signatures compiles.** Every 4.0 listener member has a default implementation, so a
  `Task`-returning `JobToBeExecuted` overrides nothing. Quartz rejects such a listener at registration and
  names the member; the compiler does not.
* **The `Quartz` namespace gained about ninety types.** `QuartzSchedulerOptions` shadowed MassTransit's type
  of that name under `using Quartz;`. Also check `RetryPolicy`, `TimeRange`, `PagedResult<T>`, `Key<T>`,
  `Matchers`, `ThreadPoolOptions`, `DataSourceOptions`, `JobType`, `SchedulerStatus`, `TimeZones` and
  `JsonSerializationException` (also in Newtonsoft.Json). Fix with a using-alias, which wins over the
  namespace import: `using QuartzSchedulerOptions = Acme.Bus.QuartzSchedulerOptions;`. Full list:
  [types 4.0 added to the `Quartz` namespace](../migration-guide.md#types-4-0-added-to-the-quartz-namespace).

## See also

* [Multiple Schedulers](../packages/multiple-schedulers.md) — named schedulers in full
* [Execution Groups](../tutorial/execution-groups.md) — limiting what a share of a scheduler costs
* [Job Execution Middleware](../tutorial/job-execution-middleware.md) — the seam, and middleware against
  listeners
* [Quartz.NET with Wolverine](wolverine.md) — this page's advice as one worked integration
* [One-Off Job](one-off-job.md) — the typed one-call scheduling this page derives keys for
* [Running under an External Leader Election](external-leader.md) — when the moment to start is somebody
  else's decision
* [Migration Guide](../migration-guide.md) — the exhaustive 3.x to 4.x delta
* [Health checks in ASP.NET Core](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks) —
  `HealthStatus`, readiness probes, and `ResultStatusCodes`
