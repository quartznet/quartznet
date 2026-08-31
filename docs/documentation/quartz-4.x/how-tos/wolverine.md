---

title: 'Quartz.NET with Wolverine'
---

# Quartz.NET with Wolverine

[Wolverine](https://wolverinefx.net) can deliver a message later. It cannot say "every weekday at
03:00", and its maintainer has said it never will: the request for cron scheduling,
[JasperFx/wolverine#1403](https://github.com/JasperFx/wolverine/issues/1403), was closed the day it was
opened with "We're not doing this, ever. *Maybe* there'll be a move to integrate Quartz.net or Hangfire
*with* Wolverine, but it's not something I'm interested in having to support." That position has since
turned into a plan rather than a refusal. Wolverine 6's
[master issue](https://github.com/JasperFx/wolverine/issues/2715) lists "Scheduler integrations —
Quartz.Net + TickerQ" among its goals, its
[migration-guide tracker](https://github.com/JasperFx/wolverine/issues/2717) says the integrations get
"opt-in package references and configuration" documented, the
[release punchlist](https://github.com/JasperFx/wolverine/issues/2745) records that the maintainer
"wants involvement before this lands", and the
[Critter Stack roadmap post of 24 July 2026](https://jeremydmiller.com/2026/07/24/critter-stack-roadmap-for-the-rest-of-2026/)
says "It's quite possible that Wolverine gets first class documentation and integration for Quartz.Net
and TickerQ first". Wolverine has already shipped the hook the integration needs:
["Sending Raw Message Data"](https://wolverinefx.net/guide/messaging/message-bus.html) exists, in its
own words, for "integrating scheduling libraries like Quartz.NET or Hangfire where you might be
persisting a `byte[]` for a message to be sent via Wolverine at a certain time".

Nothing has shipped on either side. There is no `WolverineFx.Quartz` package and no `Quartz.Wolverine`
package; this page is a recipe, not an announcement, and it is written against Wolverine 6.30.3.

::: tip A working copy of all of this
`src/Quartz.Examples.Wolverine` in the
[Quartz.NET repository](https://github.com/quartznet/quartznet/tree/main/src/Quartz.Examples.Wolverine)
is this page as one console application that builds and runs. It is in the solution, so a call on this
page that stops compiling fails the build, and every C# block below is checked against it line for
line. `dotnet run --project src/Quartz.Examples.Wolverine -- --smoke` exercises all six parts against
the in-memory store and exits non-zero if any of them stops working; the `WolverineSmoke` build target
runs exactly that on every pull request, on all three operating systems, with no database involved.
:::

## Which library should own the schedule

Before wiring anything together it is worth being clear about what is genuinely missing, because most
of what a bus calls "scheduling" is not what a scheduler does.

A transport's delay is a property of one message. Azure Service Bus says so outright: "Because the
feature is anchored on individual messages and messages can only be enqueued once, Service Bus doesn't
support recurring schedules for messages"
([message sequencing](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-sequencing)).
Amazon SQS caps delayed delivery at
[15 minutes](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-delay-queues.html)
and tells you to reach for EventBridge Scheduler beyond that. RabbitMQ's delayed-message-exchange
plugin, which several buses lean on, keeps its schedule in a single unreplicated table, is documented as
being for "a number of seconds, minutes, or hours — a day or two at most", and
[is no longer maintained](https://github.com/rabbitmq/rabbitmq-delayed-message-exchange): Mnesia was
removed in RabbitMQ 4.3.0 and took the plugin with it.

A recurrence is not a message. It is a rule that outlives every message it produces, and it brings a
tail of decisions with it: which time zone the expression is read in, what happens to a firing the
process was down for, which node in a cluster owns it, and how it is cancelled after the fact. NServiceBus
built a scheduler, ran it for years, and then removed it, publishing an unusually candid list of why:
the schedule was not durable across a restart, tasks "cannot be canceled or modified after creation",
an interval could be specified but not an execution time, and on a scaled-out endpoint a task could be
dequeued by an instance that had never created it, so it was "not executed but also not rescheduled".
Their conclusion was to
[deprecate the API](https://docs.particular.net/nservicebus/upgrades/7to8/) "in favor of options like
sagas and production-grade schedulers such as Hangfire, Quartz, and FluentScheduler". Rebus never
offered recurrence at all, consistent with its self-description as a "message bus without smarts", and
[Brighter](https://brightercommand.gitbook.io/paramore-brighter-documentation/scheduler/brighterschedulersupport)
defines a scheduler abstraction whose whole surface is "at this time" and "after this delay", with cron
left to whichever backend it is pointed at — Quartz being one.

The honest counter-example is MassTransit, which went the other way. Having relied on Quartz for
recurring messages for years — and it still ships `MassTransit.Quartz` — it grew a cron parser of
its own inside its Job Service in 2024, and now
[tells users](https://masstransit.io/documentation/patterns/job-consumers) that "Quartz.NET or Hangfire
are NOT required". So this is a live disagreement, not settled practice.

The axis that decides it, in four questions:

* Does the schedule outlive any individual message? A rule that keeps producing work is not a delayed
  send, however many times you re-arm one.
* Does a missed firing need a defined outcome rather than a silent drop? That decision is a misfire
  policy, and something has to own it.
* Must the schedule be inspectable, re-schedulable and cancellable after the fact, by something other
  than the code that created it?
* Who is on the hook when a cron expression turns out to be wrong across a daylight-saving transition?
  That is the maintenance surface Wolverine's maintainer declined to take on, and it is a real one.

Where the answers are yes, a scheduler earns its place beside the bus. Where they are no — a saga
timeout, a retry in ten minutes, a delayed reply — Wolverine's own `ScheduleAsync` is the right tool
and adding Quartz buys nothing.

## Setting the two up

Both runtimes are ordinary hosted services in one host. Wolverine goes first, so that its runtime is
started before anything that might publish into it:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Program.cs:22 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
builder.UseWolverine(opts =>
{
    // Handlers live in this assembly. Setting it explicitly rather than letting Wolverine walk the
    // stack is what keeps discovery working under a test runner and under a second host in the same
    // process (JasperFx/wolverine#3776, #3778).
    opts.ApplicationAssembly = typeof(OrderPlaced).Assembly;

    // Part 3 sends raw bytes to an endpoint by name, and a name is the whole of what it can address —
    // there is no live message for Wolverine to route on. A local queue keeps that free of a broker.
    opts.PublishMessage<ArchiveOrders>().ToLocalQueue(Part3RawMessageData.EndpointName);

    if (options.HasDatabase)
    {
        // The outbox, the inbox and the node table part 5's agent needs. Quartz's own tables go in the
        // same database, because part 6's single transaction cannot span two servers.
        opts.PersistMessagesWithPostgresql(options.PostgresConnectionString!);
    }
});
```

`WolverineFx.RuntimeCompilation` is not optional in Wolverine 6: the core package no longer ships
Roslyn, and a host left in the default `TypeLoadMode.Dynamic` throws at startup with "no
`IAssemblyGenerator` (Roslyn) is registered" unless either that package is referenced or handlers were
pre-generated with `codegen write`.

Quartz is registered the way it always is. The in-memory store is the fallback, so `UseInMemoryStore()`
would only restate the default; the persistent branch is what the last two sections need:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Program.cs:41 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
builder.Services.AddQuartz(q =>
{
    Part1RecurringPublishing.Register(q, options.ReconciliationCron);
    Part4TunedLatency.Register(q);

    if (options.HasDatabase)
    {
        q.UsePersistentStore(store =>
        {
            store.UsePostgres(options.PostgresConnectionString!);
            store.UseSystemTextJsonSerializer();

            // Development convenience. A production account is usually right not to hold DDL rights;
            // database/migrations/ is what moves a real schema forward.
            store.ProvisionSchema();

            // Part 6 throws without this, rather than silently scheduling outside the caller's
            // transaction.
            store.ConfigureStore(o => o.AcceptEnlistedTransactions = true);
        });
    }

    // Nothing else: the in-memory store is what a scheduler falls back to, so UseInMemoryStore() would
    // only restate the default.
});
```

## Publishing on a cron schedule

This is the capability Wolverine does not have, and it is a job like any other. Take `IMessageBus` in
the constructor — Quartz resolves the job from a fresh scope per firing, so a scoped `IMessageBus` is
exactly right — and publish:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part1RecurringPublishing.cs:34 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
public sealed class ReconciliationJob : IJob<ReconciliationWindow>
{
    private readonly IMessageBus bus;
    private readonly ILogger<ReconciliationJob> logger;

    public ReconciliationJob(IMessageBus bus, ILogger<ReconciliationJob> logger)
    {
        this.bus = bus;
        this.logger = logger;
    }

    public async ValueTask Execute(
        IJobExecutionContext context,
        ReconciliationWindow input,
        CancellationToken cancellationToken = default)
    {
        // The scheduler's own clock, not DateTimeOffset.UtcNow: a trigger that misfired and is firing
        // late still reports the time it was scheduled for, which is the window the run is about.
        DateTimeOffset to = context.ScheduledFireTimeUtc ?? context.FireTimeUtc;

        RunReconciliation message = new(to - input.Length, to);
        await bus.PublishAsync(message);

        logger.LogInformation("Published {Message} for the window ending {To:O}", nameof(RunReconciliation), to);
    }
}
```

`IJob<TInput>` is the typed-input form: the payload arrives as a parameter rather than as a
`JobDataMap` lookup. Register it with a cron trigger and `UsingInput`:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part1RecurringPublishing.cs:84 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
q.ScheduleJob<ReconciliationJob>(trigger => trigger
    .WithIdentity("reconciliation", "recurring")
    .WithCronSchedule(cron, x => x
        // The expression is read in this zone, so a deployment that means "03:00 local" says
        // so here rather than hoping the host agrees.
        .InTimeZone(TimeZoneInfo.Utc)
        // What happens when the process was down at 03:00. DoNothing skips to the next
        // firing; FireAndProceed publishes one catch-up message. Wolverine has no equivalent
        // decision to make, because it has nothing to miss.
        .WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing))
    .UsingInput(new ReconciliationWindow(TimeSpan.FromDays(1))));
```

Read `context.ScheduledFireTimeUtc` rather than the clock. A trigger firing late after a misfire still
reports the time it was scheduled for, and that is the window the run is about.

## Scheduling one firing from a handler

A Wolverine handler can take `IScheduler` as a parameter and arrange a single future firing in one
call. `OneOffJobOptions.Group` is the interesting argument: it sets the trigger's group, and the group
is a correlation axis — everything scheduled for one order, one saga or one tenant shares it.

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part2OneOffFromHandler.cs:41 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
public static class OrderPlacedHandler
{
    public static async Task Handle(OrderPlaced message, IScheduler scheduler, CancellationToken cancellationToken)
    {
        ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<PaymentReminderJob, PaymentReminder>(
            new PaymentReminder(message.OrderId, message.Amount),
            ExampleOptions.Current.ReminderDelay,
            new OneOffJobOptions { Group = OrderGroup.For(message.OrderId) },
            cancellationToken);

        // The call answers with the trigger's key and the time the store says it will first fire, so
        // "scheduled for" is what will happen rather than what was asked for.
        Ledger.Record(Events.ReminderScheduled, $"{scheduled.TriggerKey} at {scheduled.FirstFireTimeUtc:u}");
    }
}
```

The job key is not affected by `Group`. One durable job detail is stored per job type, under the
`QRTZ_SCHEDULED` group, and each call adds a trigger to it; the group you pass names the trigger.

### Cancelling by correlation

Because the group is part of the trigger's identity, withdrawing everything arranged for one order is
a single store operation, with the matcher evaluated where the triggers are:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part2OneOffFromHandler.cs:60 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
public static class OrderPaidHandler
{
    public static async Task Handle(OrderPaid message, IScheduler scheduler, CancellationToken cancellationToken)
    {
        // The whole cancellation, in one store operation. The matcher is evaluated where the triggers
        // are, so no key list round-trips through this process and there is no window in which a
        // trigger listed a moment ago fires before it can be removed. What comes back is the keys that
        // were actually withdrawn, which is how the caller learns whether it beat the firing.
        List<TriggerKey> cancelled = await scheduler.UnscheduleJobs(
            GroupMatcher<TriggerKey>.GroupEquals(OrderGroup.For(message.OrderId)),
            cancellationToken);

        Ledger.Record(Events.RemindersCancelled, $"{cancelled.Count} for {message.OrderId}");
    }
}
```

This is worth comparing honestly with the alternatives, because "Quartz can cancel and buses cannot"
would be wrong. Azure Service Bus hands back a sequence number and takes it back through
`CancelScheduledMessageAsync`; MassTransit gives you a `TokenId` and a `CancelScheduledMessage`
contract; Hangfire returns a job id for `BackgroundJob.Delete`. What none of them offers is a *set*
operation. Each cancels exactly one schedule per call, against a handle the application had to keep.
Quartz's schedule identity is a two-part key, and the scheduler exposes group matchers over it —
`GetTriggerKeys`, `UnscheduleJobs`, `PauseTriggers`, `DeleteJobs` — so "everything this tenant owns"
is a query rather than a list you were responsible for not losing. NServiceBus saga timeouts sit at the
other end: they cannot be cancelled at all, and the documented approach is to let the timeout arrive and
be ignored because the saga is gone.

## Deferring a serialized envelope

The section of Wolverine's documentation that names Quartz teaches `SendRawMessageAsync`, which takes
a `byte[]` rather than a message. What it does not show is how to produce the bytes, or where to keep
them; `WolverineOptions.DefaultSerializer.WriteMessage(message)` is the missing line, and a typed job
input is the place:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part3RawMessageData.cs:59 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
public static async ValueTask<TriggerKey> ScheduleSend<TMessage>(
    IScheduler scheduler,
    IWolverineRuntime runtime,
    TMessage message,
    TimeSpan delay,
    CancellationToken cancellationToken = default) where TMessage : notnull
{
    // Serialized here, at the moment the decision was made, rather than at fire time.
    DeferredEnvelope envelope = new(
        EndpointName,
        typeof(TMessage).ToMessageTypeName(),
        runtime.Options.DefaultSerializer.WriteMessage(message));

    ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<DeferredEnvelopeJob, DeferredEnvelope>(
        envelope,
        delay,
        new OneOffJobOptions { Group = "deferred-envelopes" },
        cancellationToken);

    return scheduled.TriggerKey;
}
```

At fire time the job hands the stored bytes back to Wolverine:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part3RawMessageData.cs:94 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
public async ValueTask Execute(
    IJobExecutionContext context,
    DeferredEnvelope input,
    CancellationToken cancellationToken = default)
{
    IDestinationEndpoint endpoint = bus.EndpointFor(input.EndpointName);

    await endpoint.SendRawMessageAsync(input.Data, configure: envelope =>
    {
        // The stored name rather than SetMessageType<T>(): the type this envelope describes is
        // whatever was serialized, which this job has no static knowledge of.
        envelope.MessageType = input.MessageTypeName;

        // Setting Destination is not optional, and Wolverine 6.30.3 does not do it for you.
        // DestinationEndpoint.SendRawMessageAsync assigns Sender but leaves Destination null, and
        // Executor.ExecuteAsync logs both success and failure through envelope.Destination!, so a
        // raw message that is handled perfectly still ends the pipeline with a
        // NullReferenceException out of the logging call. One line here avoids it.
        envelope.Destination = endpoint.Uri;
    });
}
```

Two things are load-bearing there. `Envelope.MessageType` is set from the stored name rather than from
`SetMessageType<T>()`, because the job has no static knowledge of what was serialized;
`typeof(T).ToMessageTypeName()` on the storing side honours a `[MessageIdentity]` alias where a raw
`FullName` would not. And `Envelope.Destination` has to be set by hand: on Wolverine 6.30.3,
`SendRawMessageAsync` assigns `Sender` but leaves `Destination` null, while `Executor.ExecuteAsync`
logs both success and failure through `envelope.Destination!` — so a raw message that is handled
perfectly still ends its pipeline with a `NullReferenceException` out of the logging call.

Why bother, when the previous section publishes a live object instead? Because the envelope is
serialized at the moment the decision was made. A payload stored as bytes is not re-derived from
application state that has since moved on, and the message contract can change under it without the
stored firing changing meaning. It is the outbox argument, applied to a schedule.

## What the latency settings actually do

Reading Quartz's 30-second `IdleWaitTime` beside Wolverine's 5-second `ScheduledJobPollingTime`
invites the conclusion that Quartz is six times slower to deliver a due message. That is not what the
numbers mean.

`QuartzSchedulerThread` acquires triggers due within the next `IdleWaitTime`, not triggers due now, and
having acquired one it waits out the exact fire time rather than sleeping the interval. More
importantly, every in-process mutation — `ScheduleJob`, `AddTrigger`, `RescheduleJob`, `DeleteJob`
— signals the scheduling loop, which releases the wait immediately. A trigger scheduled from a
Wolverine handler through this process's own `IScheduler` therefore does not wait for a sweep at all.

`IdleWaitTime` bounds the discovery of work this node did not learn about in process: a trigger another
node wrote to the shared database, or one recovered from a node that died. It is a cross-node pickup
bound and the look-ahead horizon of a single acquisition. Lowering it does not make a locally scheduled
job fire sooner, and the one place it genuinely shows is the last section on this page.

With that said, the three settings that are worth touching in front of a message bus:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part4TunedLatency.cs:49 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
q.ConfigureScheduler(options =>
{
    // Default 30 s. Only affects how quickly this node notices triggers it did not schedule
    // itself, so it is a clustering setting, not a latency setting.
    options.IdleWaitTime = TimeSpan.FromSeconds(10);

    // Default 1. Must not exceed ThreadPoolOptions.MaxConcurrency, which defaults to 10.
    options.MaxBatchSize = 10;

    // Default TimeSpan.Zero. Without this, MaxBatchSize above changes nothing for triggers
    // that are due milliseconds apart rather than at the same instant.
    options.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.FromMilliseconds(500);
});
```

`MaxBatchSize` and `BatchTriggerAcquisitionFireAheadTimeWindow` are one setting in two halves. With the
default window of `TimeSpan.Zero` only triggers due at the same instant batch together, so raising
`MaxBatchSize` alone leaves the effective batch at one for any schedule spread over time. Set the
window to the spread you are willing to fire early by. `MaxBatchSize` must not exceed the thread pool's
`MaxConcurrency`, and `IdleWaitTime` has a floor of one second.

## Letting Wolverine start the scheduler

`AutoStart = false` leaves the scheduler built, initialized and bound but in `SchedulerStatus.Created`.
Everything that reads a scheduler still sees it; nothing fires until something calls `Start`. Shutdown
is unaffected — the hosted service stops every scheduler it created, started or not.

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Program.cs:69 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
builder.Services.AddQuartzHostedService(hosted =>
{
    hosted.AutoStart = false;
    hosted.WaitForJobsToComplete = true;
});
```

Which "something" presses start depends on whether Wolverine has a message store, and here the tidy
answer and the true one differ.

**Without persistence**, Wolverine runs no agents at all. `WolverineRuntime.startAgentsAsync` opens with
`if (Storage is NullMessageStore) { ...; return; }`, so the node agent controller is never built and the
`IAgentFamily` registrations in the container are never read. `AddSingularAgent<T>()` would compile,
register, and silently never start. The faithful form is an ordinary `IHostedService` registered after
`UseWolverine`, since hosted services start in registration order:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part5StartedByWolverine.cs:51 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
public sealed class SchedulerStarter : IHostedService
{
    private readonly ISchedulerFactory schedulerFactory;
    private readonly ILogger<SchedulerStarter> logger;

    public SchedulerStarter(ISchedulerFactory schedulerFactory, ILogger<SchedulerStarter> logger)
    {
        this.schedulerFactory = schedulerFactory;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IScheduler scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await scheduler.Start(cancellationToken);

        logger.LogInformation("Scheduler '{Name}' started after the Wolverine runtime", scheduler.SchedulerName);
        Ledger.Record(Events.SchedulerStartedByWolverine, "IHostedService ordered after UseWolverine");
    }

    // Nothing to do: the Quartz hosted service shuts the scheduler down whether or not it started it.
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

**With persistence**, the agent machinery is running and `SingularAgent` is the supported way to say
"one node in the cluster does this":

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part5StartedByWolverine.cs:96 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
protected override async Task startAsync(CancellationToken cancellationToken)
{
    started = await schedulerFactory.GetScheduler(cancellationToken);
    await started.Start(cancellationToken);

    logger.LogInformation("Scheduler '{Name}' started on this node by Wolverine", started.SchedulerName);
    Ledger.Record(Events.SchedulerStartedByWolverine, "Wolverine SingularAgent");
}

protected override async Task stopAsync(CancellationToken cancellationToken)
{
    // The scheduler the agent started, not a fresh ISchedulerFactory.GetScheduler(): on host
    // shutdown Wolverine stops its agents after the Quartz hosted service has already shut the
    // scheduler down, and asking the factory for it again throws rather than handing back the
    // shut-down instance. Holding the reference and checking Status keeps the stop quiet.
    if (started is null || started.Status is SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown)
    {
        return;
    }

    // Standby rather than Shutdown: the agent may be re-assigned to this node later, and a
    // shut-down scheduler cannot be started again in the same container.
    await started.Standby(cancellationToken);
}
```

Be precise about what that buys. `SingularAgent` is once-per-cluster but it is *not* leader-pinned: its
`EvaluateAssignmentsAsync` picks `assignments.Nodes.FirstOrDefault(x => !x.IsLeader) ??
assignments.Nodes.FirstOrDefault()`, so it prefers a non-leader and falls back to the leader only when
there is one node. Wolverine's own leader-pinned family is for transport listeners and is registered
internally; a user cannot add to it. Strictly-leader-only means writing an `IAgentFamily` of your own
and calling `AssignmentGrid.RunOnLeader`.

Note also what is *not* being asked of Wolverine here: not "which node may fire this trigger". A
clustered persistent Quartz store already answers that with its own lock, so a scheduler running on
every node still fires each trigger once. What this arrangement buys is that the scheduler's lifecycle
is subordinate to the messaging runtime's — the bus is up before the first job can publish into it.

## Sharing the outbox's transaction

A handler that writes a row, sends a message and schedules a follow-up has three writes that can
disagree. Wolverine's outbox already ties the first two together. `IScheduler.EnlistTransaction` is how
the third joins them: for the duration of the returned scope, on the current asynchronous flow, the
persistent job store uses the given transaction and its connection instead of opening its own, so the
`INSERT` into `QRTZ_TRIGGERS` is a statement in the caller's transaction and a rollback takes the
trigger with it.

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part6EnlistedTransaction.cs:70 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
public static async Task Handle(
    ApproveRefund message,
    MessageContext context,
    IWolverineRuntime runtime,
    IScheduler scheduler,
    CancellationToken cancellationToken)
{
    IMessageDatabase database = (IMessageDatabase) runtime.Storage;

    await using NpgsqlConnection connection = new(ExampleOptions.Current.PostgresConnectionString!);
    await connection.OpenAsync(cancellationToken);
    await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

    // Wolverine's outgoing envelopes are now written into this transaction rather than sent
    // immediately. This is the manual form of what [Transactional] does for a Marten or EF Core
    // application.
    await context.EnlistInOutboxAsync(new DatabaseEnvelopeTransaction(database, transaction));

    // 1. the application's own state
    await using (NpgsqlCommand command = new(
        "insert into refunds (order_id, amount) values (@order_id, @amount)",
        connection,
        (NpgsqlTransaction) transaction))
    {
        command.Parameters.AddWithValue("order_id", message.OrderId);
        command.Parameters.AddWithValue("amount", message.Amount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // 2. a message that must not be sent unless the row above survives
    await context.PublishAsync(new SendPaymentReminder(message.OrderId, message.Amount));

    // 3. the trigger, in the same transaction as both, with the commit inside the scope so the
    // scheduler is signalled once the trigger is visible to it
    using (scheduler.EnlistTransaction(transaction))
    {
        await scheduler.ScheduleJob<PaymentReminderJob, PaymentReminder>(
            new PaymentReminder(message.OrderId, message.Amount),
            TimeSpan.FromDays(7),
            new OneOffJobOptions { Group = OrderGroup.For(message.OrderId) },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    // Releases the envelopes the outbox held back. Nothing left the process before the commit.
    await context.FlushOutgoingMessagesAsync();

    Ledger.Record(Events.RefundApprovedInTransaction, message.OrderId);
```

The caveats are all in
[`SchedulerEnlistmentExtensions`](https://github.com/quartznet/quartznet/blob/main/src/Quartz/SchedulerEnlistmentExtensions.cs)'
own documentation, and every one of them bites here:

* It must be turned on. `ConfigureStore(o => o.AcceptEnlistedTransactions = true)`, or
  `quartz.jobStore.acceptEnlistedTransactions`. Without it "the job store keeps opening its own
  connection and managing its own transaction, and enlisting throws rather than being ignored".
* An ambient `TransactionScope` on its own is not enough, "because a connection the job store opens for
  itself is deliberately kept out of it". Sharing the one connection is also what keeps the transaction
  from being promoted to a distributed one, which Npgsql does not support at all.
* The enlistment "flows with the current asynchronous context, so it must be established in the same
  scope as the scheduler calls it should cover". Establishing it inside an `async` helper does not carry
  it back out to the caller.
* The commit belongs inside the `using` block. Disposing the scope is what signals the scheduling loop
  that a trigger appeared, so disposing before the commit wakes it to look for a row it cannot yet see
  — and the trigger then waits for the next acquisition sweep, which is one of the few places
  `IdleWaitTime` really does bound latency.
* "While the enlistment is in effect the job store holds its locks in the caller's transaction, so they
  are only released once that transaction completes. Keep enlisted transactions short: a long running
  one blocks trigger acquisition, the misfire handler and cluster check-in." A message handler fits
  that; a batch job that enlists and then works for a minute does not.
* Both stores must be in one database. Different schemas are fine; one `DbTransaction` cannot span two
  servers.

The transaction is opened by hand rather than by Wolverine's `[Transactional]` attribute, and that is
not a stylistic choice. Wolverine's transactional middleware supplies whatever its persistence provider
supplies, and on 6.30.3 the raw-ADO.NET Postgres package supplies nothing: every
`IPersistenceFrameProvider` in the tree belongs to Marten, Entity Framework Core, RavenDB, CosmosDB,
Fisher or Polecat. A handler declaring `[Transactional] Handle(T msg, NpgsqlTransaction tx)` against
plain `PersistMessagesWithPostgresql` compiles and then fails at runtime with "JasperFx was unable to
resolve a variable of type Npgsql.NpgsqlTransaction". An application that already has Marten or EF Core
can use `[Transactional]` and take the provider's own session or `DbContext` — but then the commit
happens in generated code after the handler returns, so the enlistment scope necessarily disposes
first, and the signal is spent on a trigger the loop cannot yet see. Nothing is lost; the trigger simply
waits for the next sweep.

## What this recipe does not do

* **It is not a package.** There is nothing to install beyond `Quartz` and `WolverineFx`, and nothing
  here is covered by Quartz.NET's API compatibility promises. If JasperFx ships a first-party
  integration, prefer it.
* **It does not put Quartz's schedule under Wolverine's leader election.** Trigger ownership is the
  Quartz cluster's business, and a persistent store with `UseClustering()` already handles it. The
  agent in the last-but-one section decides which node *runs a scheduler*, not which node fires a
  trigger.
* **It does not make in-memory scheduling durable.** With the default in-memory store a restart loses
  every pending trigger, exactly as it loses Wolverine's in-memory scheduled envelopes. Use a
  persistent store for anything that must survive.
* **It does not replace Wolverine's own scheduling.** `ScheduleAsync` and `TimeoutMessage` remain the
  right answer for a delayed message or a saga timeout. Reach for Quartz when the schedule is a rule
  rather than a message.

## See also

* [One-Off Job](one-off-job.md) — the `ScheduleJob<TJob, TInput>` one-liner in isolation
* [Rescheduling Jobs](rescheduling-jobs.md) — changing a live schedule, and recovering a failed trigger
* [Job Template](job-template.md) — the recommended skeleton for a job class
* [Running Quartz under Aspire](aspire.md) — telemetry, health and the database, wired to an AppHost
* [Cron Expression Reference](../cron-expressions.md) — the cron field and special-character syntax
* [Configuration Reference](../configuration/reference.md) — every option, typed and legacy
