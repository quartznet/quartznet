---

title: 'Embedding Quartz in a Library'
---

# Embedding Quartz in a Library

This page is for the author of a **library** — a message bus integration, an identity server's token
pruning, a multi-tenant framework, an outbox — rather than the author of an application. The difference is
ownership: an application decides what the scheduler is and when it starts, and a library has to fit into
whatever the application decided.

Everything here is one of a handful of questions every embedder answers, usually by inventing something.
The answers Quartz has are collected here so that you do not have to.

This page is deliberately generic, because the questions are. For the same material worked through
against one real library — a message bus, with its own scheduling, its own transaction and its own
lifecycle — read [Quartz.NET with Wolverine](wolverine.md) alongside it.

## Who owns the scheduler

Three arrangements, in the order to consider them:

| Arrangement | Use it when |
|---|---|
| **Your own named scheduler** — `AddQuartz("mylib", …)` | The work has a resource profile of its own: its own thread pool, its own store, its own start and stop. Nothing you do can starve the application's jobs, and nothing it does can starve yours. |
| **Contribute to whatever schedulers exist** — `ConfigureAllQuartzSchedulers(…)` | The work is small, periodic housekeeping that belongs wherever the application already schedules. One scheduler, one thread pool, one set of tables to operate. |
| **An execution group inside a shared scheduler** | You want the application's scheduler but need a ceiling on how much of it you take. |

The axis is **resource isolation against operational surface**. A second scheduler is a second thread pool,
a second acquisition loop and a second thing to watch; contributing to the application's is none of those
and gives you no protection from it. The middle position — one scheduler, a named
[execution group](../tutorial/execution-groups.md) with a limit — is the one most libraries want and the
one most libraries do not know exists.

::: warning Never create a scheduler the container does not know about
Building your own `QuartzSchedulerBuilder` inside a library that is being used from a host means a
scheduler nothing else can see: not `ISchedulerRegistry`, not the dashboard, not the health check, not the
host's shutdown. Two libraries doing it against the same tables is worse — each recovers the other's fired
triggers at start-up. Register through the container the application already has.
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

A named scheduler's whole object graph is keyed by its name, so its thread pool, job store, listeners and
middleware are its own. Resolve it the way any other keyed service is resolved:

<!-- snippet: sample_embedding_named_scheduler_resolve -->
```csharp
IScheduler scheduler = provider.GetRequiredKeyedService<IScheduler>("acme.outbox");
```
<!-- endSnippet -->

Name it something an operator will recognise as yours and that nobody else will pick — a reverse-DNS-ish
prefix works. Registering two schedulers under one name is refused, case-insensitively, at the registration
site, which is a better failure than two libraries silently sharing one.

[Multiple Schedulers](../packages/multiple-schedulers.md) has the rest: configuration sections, per-scheduler
listeners, and how the default and named schedulers mix.

### An execution group, if you are sharing

If you use the application's scheduler, an execution group is how you promise not to take all of it. The
limit is set once, by group name, and every trigger tagged with that group counts against it:

<!-- snippet: sample_embedding_execution_group -->
```csharp
services.ConfigureAllQuartzSchedulers(q => q.UseExecutionLimits(limits => limits
    .ForGroup("acme.outbox", maxConcurrent: 4)
    .ForOtherGroups(int.MaxValue)));
```
<!-- endSnippet -->

Tag your triggers with `WithExecutionGroup("acme.outbox")`, or `OneOffJobOptions.ExecutionGroup`. Read
[Execution Groups](../tutorial/execution-groups.md) before choosing a number — in particular the difference
between a node-scoped and a cluster-scoped limit.

## Contributing to a scheduler you do not own

A library cannot know whether the application registers its schedulers before or after calling the
library's `Add…`, how many there are, or what they are called. `ConfigureAllQuartzSchedulers` is the answer
to all three: the delegate is recorded, applied to every scheduler already registered, and applied by each
scheduler registered afterwards as part of its own `AddQuartz`.

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

The delegate is handed a builder **per scheduler**, so everything it registers lands under that scheduler's
own service key — exactly as if it had been written inside that scheduler's `AddQuartz(name, q => …)`
callback. A listener or plugin added this way is one instance per scheduler, not one shared between them.
It runs after each scheduler's own configuration callback, which is what makes the call order immaterial,
and the usual precedence follows: registration is first-wins, so a job store the application chose is not
replaced by one you choose; options are last-wins, so a value you set here overrides the application's.

::: tip This replaces 3.x's `IConfigureOptions<QuartzOptions>`
On 3.x the way to contribute jobs to somebody else's scheduler was to register an
`IConfigureOptions<QuartzOptions>` and call `QuartzOptions.AddJob` / `AddTrigger` from it — the shape
OpenIddict's Quartz integration uses. `QuartzOptions`
[is no longer a dictionary and no longer holds jobs and triggers](../migration-guide.md#quartzoptions-is-no-longer-a-dictionary),
so that route is gone. `ConfigureAllQuartzSchedulers` is its replacement, and it is strictly better at the
job: it reaches named schedulers, which the options route never did, and it hands you a builder rather than
an options bag.
:::

**Guard the extension method, not the delegate.** Quartz will not apply one delegate *instance* to one
scheduler twice, which is what keeps a second `AddQuartz()` for the default scheduler from doubling your
registrations. It does not make your `AddAcmeOutbox()` idempotent: a lambda that captures anything is a new
delegate on every call, and two delegates are two contributions. The marker-service check in the sample
above is the ordinary .NET answer and is what to copy.

If your work belongs to one scheduler rather than all of them, ask which one you are configuring.
`IQuartzBuilder.SchedulerName` is the empty string for the default scheduler and the registered name
otherwise:

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

Remote schedulers registered with `AddQuartzHttpClient` are not built by a builder and are skipped. Calling
it when no scheduler is registered at all is not an error, so a library may call it unconditionally.

## Deriving keys

A library schedules on behalf of something that has its own identity — a saga, a message, a tenant, an
import run — and needs to find that work again later. Two keys carry that, and they carry different halves
of it:

* **The job key says what runs.** One durable job per job type is enough, because a job detail is a
  definition rather than an occurrence. That is what the typed `ScheduleJob` overloads store, at
  `(typeof(TJob).Name, SchedulerConstants.ScheduledJobGroup)` — one row per job type, whatever the traffic.
* **The trigger key says which occurrence.** Its **name** is this one firing and its **group is the
  correlation id**: the saga, the conversation, the tenant. Everything scheduled for one of those shares a
  group and can be listed, paused or cancelled together.

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
    public ValueTask<TriggerKey> Remind(SendReminder reminder, TimeSpan delay, CancellationToken cancellationToken)
    {
        return scheduler.ScheduleJob<SendReminderJob, SendReminder>(
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

`Replace = true` is what makes scheduling the same name twice an update rather than an
`ObjectAlreadyExistsException`, which is what a deterministic name is for. Derive the name from something
the caller already has — the message id, the saga id plus a step — rather than generating one you then have
to store somewhere to find it again. The whole point of a derived key is that there is nothing to store.

Cancelling by correlation is one call, and it is atomic: listing the group and then deleting the keys is
two, with a window in which another node can schedule into the group and survive a delete the caller
believes emptied it.

`DeleteJobs(GroupMatcher<JobKey>)` is the same operation for jobs. Both answer with the keys they actually
removed, so a caller can act on what went rather than on what it asked for.

::: warning Do not put the correlation id in the job key
It is the trigger that is the occurrence. A job key per correlation id gives you a job row per saga, a
delete per saga, and nothing to show for it — the durable job is a definition and there is only one
definition.
:::

### Why this is worth using rather than reinventing

A two-level key whose second level is addressable as a *set* is unusual. Most schedulers hand back one
opaque identifier per scheduled item and leave the caller to keep an index if it ever wants to find a group
of them again: a Hangfire job id, an Azure Service Bus scheduled-message sequence number, or MassTransit's
one-shot scheduled-message token, which the sender must have kept in order to cancel by it later.
NServiceBus goes further and declines the question: a requested saga timeout "cannot be changed (i.e.
rescheduled) or revoked (i.e. deleted or cancelled)", and its documented answer is to let the timeout
arrive and have the saga decide, from flags in its own state, whether to act on it. Temporal reaches a set
through Search Attributes rather than through the workflow id. The nearest thing to Quartz's shape
elsewhere is MassTransit's *recurring* scheduler, whose `ScheduleId` and `ScheduleGroup` are Quartz's
trigger name and group surfacing through its API.

That last option — schedule it anyway, decide at the firing — is always available here too, and is worth
weighing: a cancellation you never have to issue is a cancellation that cannot race. Cancelling by group
is what you want when the work is expensive or the set is large; a firing that checks whether it is still
wanted is what you want when it is neither.

Where a platform does let the caller choose the identifier, its documentation says to derive it from the
business object rather than generate one — Temporal recommends a business-process identifier such as a
customer or order id, and Hangfire's recurring jobs are keyed by a caller-chosen id that `AddOrUpdate`
upserts on. No primary source argues for a random id where a stable business key exists.

## A typed input, not a payload bag

A job that reads `context.MergedJobDataMap.GetString("CustomerId")` has a payload contract nothing checks.
`IJob<TInput>` is the checked version: the input is serialized into the trigger's data under one reserved
key, and the job is handed it already typed, as the sample above shows.

Two things follow from it being serialized, and they are the same two that apply to any message:

* **Put ids in the payload, not entities.** The payload is read back minutes, hours or days later, by a
  process that may be running different code. A customer id still means what it meant; a serialized
  customer may not. Hangfire says the same thing about its argument serialization, and Temporal makes it a
  named pattern — the claim check, driven there by hard payload size limits.
* **Evolve it the way you would evolve a message.** Adding an optional member is safe; renaming or removing
  one breaks every firing already in the store. There is no schema version in the payload unless you put
  one there.

[A typed input](../tutorial/job-data-map.md#a-typed-input-the-third-read-side) has the mechanism, and
[What does not belong in job data](../tutorial/job-data-map.md#what-does-not-belong-in-job-data) has the
rest of the hazards.

## Scheduling over what is already there

The three-call form — check whether it exists, unschedule it, schedule the new one — is the single most
common thing an embedder writes, and it is wrong twice over: three round trips, and a window between the
check and the write in which another node does the same thing. Both `ScheduleJob` overloads that take a
`ScheduleJobOptions` do the whole thing inside the store's own lock:

<!-- snippet: sample_embedding_upsert -->
```csharp
ITrigger trigger = TriggerBuilder.Create<SendReminderJob>(scheduler.TimeProvider)
    .WithIdentity(reminder.MessageId, reminder.ConversationId)
    .ForJob(new JobKey(nameof(SendReminderJob), SchedulerConstants.ScheduledJobGroup))
    .StartAt(at)
    .UsingInput(reminder)
    .Build();

await scheduler.ScheduleJob(trigger, ScheduleJobOptions.Replacing, cancellationToken);
```
<!-- endSnippet -->

`ScheduleJobOptions.Replacing` is the well-known value for the common case; `AddJobOptions.Replacing` is its
counterpart for `AddJob`. A replaced trigger keeps the previous fire time it had, so a job reading
`context.PreviousFireTimeUtc` is not told the schedule has never fired merely because its trigger was
rewritten.

The typed overloads take the same option as `OneOffJobOptions.Replace`, so the one-call path already does
this. Reach for a hand-built trigger only when you need a schedule the one-call path cannot express.

## Starting at your own moment

A library often cannot let its scheduler start with the host: the bus is not connected, the leader lease is
not held, the schema is not migrated. `AutoStart = false` builds, initializes and binds the scheduler with
the host and leaves it in `Created` for you to start:

<!-- snippet: sample_embedding_deferred_start -->
```csharp
builder.Services.AddQuartz("acme.outbox", q => q.UseInMemoryStore());

// Built, initialized and bound with the host, and then left in Created. The library starts it
// when whatever it depends on - a bus connection, a leader lease, a migration - is ready.
builder.Services.AddQuartzHostedService("acme.outbox", options => options.AutoStart = false);
```
<!-- endSnippet -->

Not registering the hosted service at all would also not start it, and would lose the shutdown handling with
it, which is why this is a setting rather than an omission. The scheduler is still in `ISchedulerRegistry`,
still on the dashboard, still shut down with the host.

A scheduler waiting like this reports **degraded**, not unhealthy. It is doing what it was configured to
do, and failing the probe would take a correctly configured node out of rotation for the whole window
before you press start. The check reads that scheduler's own `QuartzHostedServiceOptions`, so a `Created`
scheduler that nobody opted out of is still unhealthy, as it should be. See
[Health checks](../packages/hosted-services-integration.md#health-checks).

This is Quartz's judgement rather than an industry convention, and it is worth knowing which way the rest
of the ecosystem leans: Microsoft's own readiness sample reports *unhealthy* while start-up work is still
running, and MassTransit reports unhealthy for a bus that has not connected. The reasoning for the other
choice is the HTTP mapping — *degraded* answers 200 by default and *unhealthy* answers 503 — so a probe
that cannot tell "deliberately waiting" from "broken" removes the node either way. The verdict itself is
not configurable: `QuartzHealthCheckOptions.FailureStatus` sets what the *registration* reports when the
check fails, and does not turn a deliberate *degraded* into *unhealthy*. An application that wants the
stricter reading maps it at the probe, with `HealthCheckOptions.ResultStatusCodes`.

Where the moment is decided by an election outside the process rather than by your own readiness, see
[Running under an External Leader Election](external-leader.md).

## Cross-cutting concerns are middleware

Every embedder needs to surround a firing with something: a log scope, a tenant context, a unit of work, a
consume context, a translation of what the library throws into a `JobExecutionException`. On 3.x the only
place to put it was a job that constructed and called the real job — the adapter ABP, Elsa and Brighter
each ship — because `IJobListener` is notification-only: it is told a job is about to run and told what it
did, with the execution happening between the two notifications rather than inside them.

An adapter job costs more than it looks, and every cost is structural rather than stylistic:

* **The store records the wrapper.** `JOB_CLASS_NAME` holds the type Quartz was given, so a listing, the
  dashboard and every diagnostic say the adapter's name rather than the job's. A generic adapter writes the
  inner type in as a type argument, which is how ABP's `QuartzPeriodicBackgroundWorkerAdapter<T>` overflowed
  the column's 250 characters and broke start-up
  ([abp#4609](https://github.com/abpframework/abp/issues/4609)).
* **`[DisallowConcurrentExecution]` and `[PersistJobDataAfterExecution]` are read from the wrapper.**
  Quartz asks the type it was handed, its base types and its interfaces — not the object the wrapper holds.
  An inner job that declares either attribute silently loses it.
* **It takes over construction**, so the application's job factory and DI scope stop applying to the real
  job, and interruption stops working unless the wrapper forwards the cancellation token by hand.
* **It does not compose.** Two libraries each shipping an adapter cannot both wrap one firing.

4.0 has the seam:

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

Middleware is keyed per scheduler, runs in registration order outermost first, and is composed once when the
scheduler is built — so one instance serves every firing, and per-firing state belongs in an `AsyncLocal<T>`
or in the job's scope rather than in a field. Not calling `next` short-circuits the firing. It runs inside
the execution span and the duration measurement, and outside the run shell's exception handling, so a
`JobExecutionException` a middleware throws is honoured exactly as one the job raised.

[Job Execution Middleware](../tutorial/job-execution-middleware.md) has the whole of it, including which
concerns belong in a listener instead.

## Retry

There is no retry policy attached to a trigger on 4.0. `Quartz.RetryPolicy` exists as a value — `Fixed`,
`Exponential`, `Explicit`, with the storage columns reserved — but nothing on a trigger carries one and
nothing acts on one yet. It is [#3520](https://github.com/quartznet/quartznet/issues/3520).

Until then, be clear about what the one existing mechanism is:

```csharp
throw new JobExecutionException(ex) { RefireImmediately = true };
```

**`RefireImmediately` is not a retry.** It re-executes the job on the same thread, with no delay, no
ceiling and no backoff, until it stops throwing — which against a database that is down is a tight loop
that holds a worker thread and hammers the dependency. It is the right answer to "this failed for a reason
that has already gone away", and the wrong answer to everything else.

A library that needs real retry today has two honest options: put a resilience pipeline
([Polly](https://www.pollydocs.org/) or `Microsoft.Extensions.Http.Resilience`) inside the job, where the
retries are yours and bounded; or make the retry a schedule, by having the failing job schedule its own
next attempt with a computed delay and an attempt count in the payload. The first is right for a fault that
will clear in seconds, the second for one that will not — and only the second survives a process restart.

## Trace context across the scheduled gap

A scheduled job runs long after the call that asked for it, so the trace that wanted the work and the trace
that did it are not the same trace, and cannot be. Quartz records the W3C trace context of whatever
activity was current when the trigger was stored — under `SchedulerConstants.TraceParent` and
`SchedulerConstants.TraceState` — and the firing's `Quartz.Job.Execute` span carries an `ActivityLink` back
to it.

Nothing needs configuring for this, and a library gets it for free: schedule from inside a request or a
message handler and the link is written. It is a link rather than a parent deliberately, which is the shape
OpenTelemetry gives a producer and a consumer separated by a store-and-forward gap — the firing stays its
own trace root, and the link is how a backend walks back.

The cost is two string entries on each trigger's data map, visible wherever trigger data is. A library
whose own rows are read by something that does not expect them can turn it off:

<!-- snippet: sample_embedding_trace_context_off -->
```csharp
services.ConfigureAllQuartzSchedulers(q =>
    q.ConfigureScheduler(options => options.PropagateTraceContext = false));
```
<!-- endSnippet -->

Do not invent a second key for the same job. If you were carrying your own correlation headers in job data
across the gap, these two keys are the standard spelling of the trace half of that; see
[Linking a firing to what scheduled it](../packages/opentelemetry-integration.md#linking-a-firing-to-what-scheduled-it).

## Running inside somebody else's transaction

A library that writes its own rows and then schedules a job to act on them has two transactions where it
wants one: the rows can commit while the scheduling fails, or the reverse. The persistent store can use a
connection the application already owns, so the two commit together:

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

Take the transaction as a parameter, as the sample does, rather than opening one inside the library. The
enlistment flows with the asynchronous context, so it has to be established in the same scope as the calls
it covers — establishing it inside an `async` helper does not carry it back out to the caller, which is the
same rule that makes `TransactionScope` need `TransactionScopeAsyncFlowOption.Enabled`.

Four caveats, and they matter more for a library than for an application, because a library does not
control any of the surrounding conditions:

* **It has to be the same database.** The enlisted connection's provider must match the one the store was
  configured with, and Quartz refuses the enlistment rather than failing deep inside the first statement.
  If your rows and the scheduler's tables live in different databases, this is not available and a
  transactional outbox in your own tables is the answer.
* **The application must have opted in.** `AcceptEnlistedTransactions` is off by default, and enlisting
  against a store that has not enabled it throws. A library cannot enable it on the application's behalf
  without also changing how the store locks, so document it as a prerequisite and fail with a message that
  says so.
* **The store holds its locks in your transaction.** They are released when you commit, so a long
  transaction blocks trigger acquisition, the misfire handler and cluster check-in for every node.
* **It only works on a persistent ADO.NET store.** Against `RAMJobStore` the call throws rather than
  silently ignoring the enlistment.

[Joining an existing transaction](../tutorial/job-stores.md#joining-an-existing-transaction) has the
`TransactionScope` variant and the full list.

## Shipping a package that multi-targets

Quartz 4 targets `net10.0` and nothing else. An application can be told to upgrade; an integration package
serving `net8.0` and `net9.0` consumers cannot. The mechanical part is a conditional `PackageReference`:

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

**Read the limit before you write the `#if`s.** This works when the difference is *inside* your library. It
stops working the moment your own public surface would differ per target framework — and it will, if you
expose an `IJobFactory` of your own, a clock, a scheduler you construct, or anything returning `Task` that
Quartz now wants as `ValueTask`. A type that exists on one target and not another is not a shim, it is two
libraries in one package, and consumers discover which one they got at compile time. When you reach that
point the answers are a separate `net10.0`-only package, or dropping the old targets — not a deeper `#if`.

### What actually differs

These are the renames a real port hit, verified against the public API baselines both branches keep. The
[migration guide's appendix](../migration-guide.md#appendix-what-happened-to-a-name) is the exhaustive
version, indexed by the name you would have typed; this is the subset an integration package meets.

| 3.x | 4.x |
|---|---|
| `Task` / `Task<T>` on every asynchronous member | `ValueTask` / `ValueTask<T>`. The only public members still returning `Task` are `QuartzHostedService`'s, because `IHostedService` says so |
| `ValueTask Execute(IJobExecutionContext context)` | `ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)` — the token is a parameter as well as a context member |
| `IScheduler.CheckExists(JobKey \| TriggerKey)` | `IScheduler.Exists(JobKey \| TriggerKey)` |
| `MisfireInstruction.SimpleTrigger.FireNow` and the other nested `const int`s | five enums — `SimpleTriggerMisfireInstruction`, `CronTriggerMisfireInstruction`, `CalendarIntervalTriggerMisfireInstruction`, `DailyTimeIntervalTriggerMisfireInstruction`, `RecurrenceTriggerMisfireInstruction`. Values are unchanged; several names are not: `IgnoreMisfirePolicy` is `IgnoreMisfires`, and cron's, calendar's and daily's `FireOnceNow` is `FireAndProceed` |
| `ITrigger.MisfireInstruction` (`int`) | `ITrigger.MisfireInstructionCode` (`int`), plus a typed `MisfireInstruction` on each family interface |
| `trigger.GetNextFireTimeUtc()`, `GetPreviousFireTimeUtc()`, `GetMayFireAgain()` | the properties `NextFireTimeUtc`, `PreviousFireTimeUtc`, `MayFireAgain`. `FinalFireTimeUtc` was already a property and `GetFireTimeAfter(…)` is still a method |
| `new JobExecutionException(ex, refireImmediately: true)` | `new JobExecutionException(ex) { RefireImmediately = true }` — the flags are `init`, the `bool` constructor overloads are gone, and the type is sealed |
| `Quartz.Util.TimeZoneUtil.FindTimeZoneById(id)` | `Quartz.TimeZones.FindById(id)` |
| `TimeZoneUtil.CustomResolver = resolver` | `TimeZones.AddResolver(resolver)`, which returns an `IDisposable` that removes that one resolver |
| your own `TryGetValue<T>` extension on `JobDataMap` | **unchanged — keep it.** `JobDataMap` still implements `IDictionary<string, object?>`, so the extension still binds. Do *not* substitute 4.x's `TryGet<T>`: that is a pure `is T` type test with no conversion, so it returns `false` for a value stored as a string. `TryGetInt` and its siblings are the converting readers |
| `Quartz.Extensions.DependencyInjection`, `Quartz.Extensions.Hosting` (packages) | merged into `Quartz`. The namespace was `Quartz` on both sides, so only the `PackageReference` changes |
| `IServiceCollectionQuartzConfigurator` | `IQuartzBuilder` |
| `scheduler.JobFactory = factory` (setter-only, on `IScheduler`) | `q.UseJobFactory(factory)` / `UseJobFactory<T>()` on the builder, or `ConfigureJobScope(…)` when all you wanted was to seed the DI scope |
| `IJobFactory.NewJob(bundle, scheduler)` returning `IJob`; `ReturnJob(IJob)` | `CreateJob(bundle, scheduler, ct)` returning `ValueTask<JobScope>`; `ReturnJob(JobScope, ct)` |
| `SystemTime.UtcNow = () => …` (a public mutable field) | `q.UseTimeProvider(provider)`, read back as `IScheduler.TimeProvider`. Per scheduler, not process-wide |
| `StdSchedulerFactory` / `SchedulerBuilder` | `QuartzSchedulerBuilder.Create()`, whose `Build()` gives a `StandaloneSchedulerFactory` |
| `TriggerBuilder.ModifiedByCalendar(name)` | `WithCalendarName(name)` |
| `SchedulerMetaData` / `GetMetaData()` | `SchedulerMetadata` / `GetMetadata()` — note the lower-case `d` in both |
| `GetCurrentlyExecutingJobs()` | `QueryFireInstances(new FireInstanceQuery())`, returning `PagedResult<FireInstance>` |
| `IsStarted`, `InStandbyMode`, `IsShutdown` | one `SchedulerStatus Status` |
| `Interrupt(fireInstanceId)` | `InterruptFireInstance(fireInstanceId)` |
| `Quartz.Spi`, `Quartz.Simpl`, `Quartz.Impl.Matchers`, `Quartz.Util`, `Quartz.Listener` | `Quartz.Extensibility`, `Quartz.Impl`, `Quartz` (matchers and `Key<T>` are top-level now), dissolved, `Quartz.Listeners` |
| `Quartz.Logging` — `ILogProvider`, `LogContext`, `LogLevel` | gone with LibLog, and there is **no** replacement for the abstraction. `LogProvider` moved to `Quartz.Diagnostics` and takes an `ILoggerFactory` |

Two of those are traps rather than renames, and both bite a library harder than an application:

* **A listener that still has 3.x signatures compiles.** Every listener member on 4.0 has a default
  implementation, so a `Task`-returning `JobToBeExecuted` is not an error — it is simply a method that
  overrides nothing, and your listener silently does nothing. Quartz rejects such a listener at
  registration and names the member, so the failure is loud, but the compiler will not point at it.
* **The `Quartz` namespace gained about ninety types**, several with names a host library plausibly
  declares itself. `QuartzSchedulerOptions` is the known case — it shadowed MassTransit's own type of that
  name under `using Quartz;`, producing compile errors that named the right members on the wrong type.
  `RetryPolicy`, `TimeRange`, `PagedResult<T>`, `Key<T>`, `Matchers`, `ThreadPoolOptions`,
  `DataSourceOptions`, `JobType`, `SchedulerStatus`, `TimeZones` and `JsonSerializationException` (which
  Newtonsoft.Json also declares) are the others worth checking for. The fix is a using-alias in the
  affected file, which wins over the namespace import:
  `using QuartzSchedulerOptions = Acme.Bus.QuartzSchedulerOptions;`. The full list is in the migration
  guide's [types 4.0 added to the `Quartz` namespace](../migration-guide.md#types-4-0-added-to-the-quartz-namespace).

## See also

* [Multiple Schedulers](../packages/multiple-schedulers.md) — named schedulers in full
* [Execution Groups](../tutorial/execution-groups.md) — limiting what a share of a scheduler costs
* [Job Execution Middleware](../tutorial/job-execution-middleware.md) — the seam, and middleware against
  listeners
* [Quartz.NET with Wolverine](wolverine.md) — this page's advice as one worked integration: correlation
  keys, the latency triple, a bus-owned start and a shared outbox transaction, all against a real library
* [One-Off Job](one-off-job.md) — the typed one-call scheduling this page derives keys for
* [Running under an External Leader Election](external-leader.md) — when the moment to start is somebody
  else's decision
* [Migration Guide](../migration-guide.md) — the exhaustive 3.x to 4.x delta

## Sources

Prior art surveyed in August 2026. Quartz.NET's own behaviour is stated from the source in this
repository rather than from any of these.

* OpenIddict,
  [`OpenIddictQuartzConfiguration`](https://github.com/openiddict/openiddict-core/blob/dev/src/OpenIddict.Quartz/OpenIddictQuartzConfiguration.cs)
  — the `IConfigureOptions<QuartzOptions>` contributor this page's recipe replaces
* ABP, [abp#4609](https://github.com/abpframework/abp/issues/4609) — a generic adapter job overflowing
  `JOB_CLASS_NAME`
* OpenTelemetry,
  [Semantic conventions for messaging spans](https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/)
  — span links as the default correlation between a producer and a later consumer
* MassTransit, [Scheduling](https://masstransit.io/documentation/configuration/scheduling) — scheduled-message
  tokens, and the recurring scheduler whose `ScheduleId`/`ScheduleGroup` are Quartz's own key
* Particular Software, [Saga timeouts](https://docs.particular.net/nservicebus/sagas/timeouts) — a
  requested timeout that cannot be revoked, and deciding at the firing instead
* Hangfire, [Best Practices](https://docs.hangfire.io/en/latest/best-practices.html) and
  [Passing arguments](https://docs.hangfire.io/en/latest/background-methods/passing-arguments.html) —
  pass identifiers rather than objects, and keep the payload small
* Temporal, [Workflow ID](https://docs.temporal.io/workflow-execution/workflowid-runid) and
  [Data conversion](https://docs.temporal.io/dataconversion) — a business identifier as the workflow id,
  and payload limits as the reason to pass a reference rather than the thing
* Microsoft, [Health checks in ASP.NET Core](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks)
  — `HealthStatus`, readiness probes, and `ResultStatusCodes`
* NuGet, [Target frameworks](https://learn.microsoft.com/nuget/create-packages/multiple-target-frameworks-project-file)
  — conditional `PackageReference` per target framework
