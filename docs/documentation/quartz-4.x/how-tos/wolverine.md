---

title: 'Quartz.NET with Wolverine'
---

# Quartz.NET with Wolverine

Since 6.34.0 ([JasperFx/wolverine#4307](https://github.com/JasperFx/wolverine/pull/4307)),
[Wolverine](https://wolverinefx.net) can publish a message on a cron expression with
`opts.Schedules.ScheduleRecurring`, parsed by [Cronos](https://github.com/HangfireIO/Cronos). It is a cron
on top of Wolverine's scheduled messages, deliberately not a scheduler. This page covers when to use it,
when to use Quartz, and how to run both.

There is no integration package (no `WolverineFx.Quartz`, no `Quartz.Wolverine`); one was
[planned for Wolverine 6](https://github.com/JasperFx/wolverine/issues/2715) but has not shipped. This is a
recipe, written against Wolverine 6.35.0. It uses Wolverine's hook for scheduling libraries,
["Sending Raw Message Data"](https://wolverinefx.net/guide/messaging/message-bus.html), in
[Deferring a serialized envelope](#deferring-a-serialized-envelope).

::: tip A working copy of all of this
`src/Quartz.Examples.Wolverine` in the
[Quartz.NET repository](https://github.com/quartznet/quartznet/tree/main/src/Quartz.Examples.Wolverine) is
this page as one console application, built with the solution; every C# block below is checked against it
line for line. `dotnet run --project src/Quartz.Examples.Wolverine -- --smoke` runs all seven parts on the
in-memory store and exits non-zero on failure. The `WolverineSmoke` build target runs it on every pull
request, on all three operating systems, with no database.
:::

## Which library should own the schedule

| Use | When |
|---|---|
| Wolverine's `opts.Schedules` | publish a message on a cron, skipping whatever the process was down for |
| a Quartz trigger that publishes | a missed firing needs a *decided* outcome; the schedule changes while the host runs; some dates are excluded; or runs must not overlap |

For the first case Wolverine's schedule is three lines in `UseWolverine`, rides the existing outbox, and
needs no second runtime.

A transport delay applies to one message; a recurrence is a rule that outlives its messages. Other stacks:

| Stack | Recurring schedules |
|---|---|
| Azure Service Bus | [not supported](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-sequencing); scheduling is per message |
| Amazon SQS | delay capped at [15 minutes](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-delay-queues.html); EventBridge Scheduler beyond that |
| RabbitMQ delayed-message-exchange plugin | seconds to a day or two, one unreplicated table; [unmaintained](https://github.com/rabbitmq/rabbitmq-delayed-message-exchange) since Mnesia was removed in RabbitMQ 4.3.0 |
| NServiceBus | scheduler [deprecated](https://docs.particular.net/nservicebus/upgrades/7to8/) in favour of sagas and schedulers such as Hangfire, Quartz and FluentScheduler |
| Rebus | none |
| [Brighter](https://brightercommand.gitbook.io/paramore-brighter-documentation/scheduler/brighterschedulersupport) | "at this time" and "after this delay"; cron left to the backend, Quartz being one |
| MassTransit | a cron parser in its [Job Service](https://masstransit.io/documentation/patterns/job-consumers) since 2024; still ships `MassTransit.Quartz`, which [needs Quartz.NET 3.x](../packages/quartz-3rd-party-plugins.md#message-buses) |
| Wolverine | `opts.Schedules` since 6.34: a cron deciding *when* an occurrence is published |

[Comparison](../comparison.md) has the full side-by-side, including Hangfire, TickerQ and Coravel.

## Setting the two up

Both are hosted services in one host. Register Wolverine first, so it is running before anything publishes:

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

    // Part 7: Wolverine's own recurring schedule, registered here rather than inside AddQuartz because
    // it is Wolverine's. Since 6.34 this is what most of part 1 would otherwise be reaching for.
    Part7WolverineSchedules.Register(opts, options.ExpiryCron);

    if (options.HasDatabase)
    {
        // The outbox, the inbox and the node table part 5's agent needs. Quartz's own tables go in the
        // same database, because part 6's single transaction cannot span two servers.
        opts.PersistMessagesWithPostgresql(options.PostgresConnectionString!);
    }
});
```

Wolverine 6 requires `WolverineFx.RuntimeCompilation`: the core no longer ships Roslyn, and a host in the
default `TypeLoadMode.Dynamic` throws "no `IAssemblyGenerator` (Roslyn) is registered" at startup unless
that package is referenced or handlers were pre-generated with `codegen write`.

Register Quartz as usual; the persistent branch is for the last two sections:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Program.cs:45 — WolverineHowToTest fails when the two stop
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

## Wolverine's own schedules

Register it inside `UseWolverine`; the Quartz scheduler never sees it.

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part7WolverineSchedules.cs:66 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
public static void Register(WolverineOptions opts, string cron)
{
    // Cronos' grammar, not Quartz's. The zone is the schedule's own, so a deployment that means
    // "03:15 local" says so here rather than hoping the host agrees — the same decision part 1
    // makes with InTimeZone, and one of the few this feature and a Quartz trigger both let you make.
    CronSchedule schedule = new(cron, TimeZoneInfo.Utc);

    // The factory is handed the occurrence time, which is this feature's answer to reading
    // context.ScheduledFireTimeUtc rather than the clock: the message describes the window the
    // schedule says it is for. Without a name the schedule is named for its message type, and
    // ScheduleRecurring<T>(cron) is the whole registration for a message with a parameterless
    // constructor.
    opts.Schedules.ScheduleRecurring(ScheduleName, schedule, occurrence => new ExpireUnpaidOrders(occurrence));
}
```

* One `SingularAgent` per cluster keeps the *next* occurrence of each schedule queued as an ordinary
  scheduled message, so delivery, durability and replay are Wolverine's.
* Each occurrence has a deterministic deduplication id, `{name}:{occurrenceUtc:O}`, so a re-publish after
  agent failover collapses at consumption.
* With a relational message store, `wolverine_recurring_messages` records which schedule owns which pending
  envelope; the agent checks it is still in the inbox, and a successor adopts it instead of publishing
  again.

[Documented](https://wolverinefx.net/guide/messaging/recurring.html) limits:

* **Nothing faster than every five seconds**, the default `DurabilitySettings.ScheduledJobPollingTime`; a
  faster cron is refused at registration. So the smoke run uses `*/5 * * * * *` here, while part 1's Quartz
  trigger fires every two seconds under `--smoke`.
* **Without a message store** (a startup warning), occurrences in a restart window are lost and there is no
  store-backed deduplication. `DurabilityMode.Serverless` and `DurabilityMode.MediatorOnly` run no agents,
  so a host in either mode with a schedule refuses to start.
* **Missed occurrences are skipped, never back-filled**, including during a `PauseAsync` / `ResumeAsync`
  window. This is the main difference from a Quartz trigger.

## When the schedule still belongs in Quartz

For the cases in the [table above](#which-library-should-own-the-schedule), use a Quartz job that
publishes. Take `IMessageBus` in the constructor; Quartz resolves the job in a fresh scope per firing, so a
scoped `IMessageBus` is right.

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part1RecurringPublishing.cs:35 — WolverineHowToTest fails when the two stop
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

`IJob<TInput>` receives the payload as a parameter, not a `JobDataMap` lookup. Register it with a cron trigger
and `UsingInput`:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Part1RecurringPublishing.cs:85 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
q.ScheduleJob<ReconciliationJob>(trigger => trigger
    .WithIdentity("reconciliation", "recurring")
    .WithCronSchedule(cron, x => x
        // The expression is read in this zone, so a deployment that means "03:00 local" says
        // so here rather than hoping the host agrees.
        .InTimeZone(TimeZoneInfo.Utc)
        // What happens when the process was down at 03:00. DoNothing skips to the next
        // firing, which is what part 7's schedule does and all it does; FireAndProceed
        // publishes one catch-up message, which is the choice Wolverine's own schedules do
        // not offer.
        .WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing))
    .UsingInput(new ReconciliationWindow(TimeSpan.FromDays(1))));
```

Read `context.ScheduledFireTimeUtc`, not the clock: a late firing after a misfire still reports its
scheduled time, which is the window the run covers.

## Scheduling one firing from a handler

A handler can take `IScheduler` and schedule a firing in one call. `OneOffJobOptions.Group` sets the
trigger's group, the correlation axis for one order, saga or tenant:

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

`Group` names the trigger, not the job: one durable job detail per job type is stored under the
`QRTZ_SCHEDULED` group, and each call adds a trigger.

### Cancelling by correlation

Withdrawing everything for one order is one store operation, with the matcher evaluated in the store:

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

Azure Service Bus (`CancelScheduledMessageAsync` with a sequence number), MassTransit
(`CancelScheduledMessage` with a `TokenId`) and Hangfire (`BackgroundJob.Delete` with a job id) cancel one
schedule per call, by a handle you keep. NServiceBus saga timeouts cannot be cancelled; the saga ignores
them on arrival. Quartz's group matchers (`GetTriggerKeys`, `UnscheduleJobs`, `PauseTriggerGroups`,
`DeleteJobs`) make "everything this tenant owns" a query.

## Deferring a serialized envelope

Wolverine's `SendRawMessageAsync` takes a `byte[]`. Produce it with
`WolverineOptions.DefaultSerializer.WriteMessage(message)` and store it as a typed job input:

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

At fire time the job hands the bytes back to Wolverine:

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

        // Setting Destination is not optional, and Wolverine 6.35.0 does not do it for you.
        // DestinationEndpoint.SendRawMessageAsync assigns Sender but leaves Destination null, and
        // Executor.ExecuteAsync logs both success and failure through envelope.Destination!, so a
        // raw message that is handled perfectly still ends the pipeline with a
        // NullReferenceException out of the logging call. One line here avoids it.
        envelope.Destination = endpoint.Uri;
    });
}
```

* `Envelope.MessageType` comes from the stored name, because the job does not know the type statically.
  `typeof(T).ToMessageTypeName()` honours a `[MessageIdentity]` alias; a raw `FullName` would not.
* **Set `Envelope.Destination` by hand.** In Wolverine 6.35.0, `SendRawMessageAsync` sets `Sender` but not
  `Destination`, and `Executor.ExecuteAsync` logs through `envelope.Destination!`. Without that line the
  message is still delivered, but a `NullReferenceException` from `Executor.ExecuteAsync` is logged.

Use this when the payload must be fixed at the moment of the decision: stored bytes do not change with later
application state or message-contract changes.

## What the latency settings actually do

Quartz's 30-second `IdleWaitTime` beside Wolverine's 5-second `ScheduledJobPollingTime` does not make Quartz
six times slower to deliver a due message:

* `QuartzSchedulerThread` acquires triggers due within the next `IdleWaitTime` and waits for the exact
  fire time.
* Every in-process `ScheduleJob`, `AddTrigger`, `RescheduleJob` or `DeleteJob` wakes the loop, so a trigger
  scheduled from a handler through this process's `IScheduler` never waits for a sweep.
* `IdleWaitTime` bounds only the pickup of work scheduled elsewhere (another node, a recovered trigger) and
  the look-ahead of one acquisition. It matters in the [last section](#sharing-the-outbox-s-transaction).

The three settings worth changing in front of a bus:

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

Set `MaxBatchSize` and `BatchTriggerAcquisitionFireAheadTimeWindow` together: with the default window of
`TimeSpan.Zero` only triggers due at the same instant batch, so set the window to how early a firing may
run. `MaxBatchSize` must not exceed the thread pool's `MaxConcurrency`; `IdleWaitTime` has a floor of one
second.

## Letting Wolverine start the scheduler

With `AutoStart = false` the scheduler is built, initialized and bound, and waits in
`SchedulerStatus.Created` until something calls `Start`. Shutdown is unaffected: the hosted service stops
every scheduler it created.

<!-- Not a compiled sample: `Quartz.Documentation.Samples` may not reference `WolverineFx`.
     Copied from src/Quartz.Examples.Wolverine/Program.cs:73 — WolverineHowToTest fails when the two stop
     matching. -->

```csharp
builder.Services.AddQuartzHostedService(hosted =>
{
    hosted.AutoStart = false;
    hosted.WaitForJobsToComplete = true;
});
```

**Without persistence**, Wolverine runs no agents: `WolverineRuntime.startAgentsAsync` begins with
`if (Storage is NullMessageStore) { ...; return; }`, so `IAgentFamily` registrations are never read and
`AddSingularAgent<T>()` silently never starts. Register an `IHostedService` after `UseWolverine` (hosted
services start in registration order):

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

**With persistence**, use a `SingularAgent`, which runs on one node in the cluster:

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

* `SingularAgent` is *not* leader-pinned: `EvaluateAssignmentsAsync` picks
  `assignments.Nodes.FirstOrDefault(x => !x.IsLeader) ?? assignments.Nodes.FirstOrDefault()`, preferring a
  non-leader. Wolverine's leader-pinned family is internal, for transport listeners. For leader-only, write
  an `IAgentFamily` that calls `AssignmentGrid.RunOnLeader`.
* This does not decide which node fires a trigger; a clustered persistent Quartz store does that with its
  own lock. It only makes the bus start before the first job can publish.

## Sharing the outbox's transaction

To make a row, a message and a scheduled follow-up commit together, add `IScheduler.EnlistTransaction` to
Wolverine's outbox. While the returned scope is open, on the current asynchronous flow, the persistent job
store uses the given transaction and connection, so a rollback removes the `QRTZ_TRIGGERS` row too.

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

From [`SchedulerEnlistmentExtensions`](https://github.com/quartznet/quartznet/blob/main/src/Quartz/SchedulerEnlistmentExtensions.cs):

* **Turn it on** with `ConfigureStore(o => o.AcceptEnlistedTransactions = true)` or
  `quartz.jobStore.acceptEnlistedTransactions`; otherwise enlisting throws.
* **An ambient `TransactionScope` alone is not enough**: the store keeps its own connections out of it.
  Sharing one connection also avoids promotion to a distributed transaction, which Npgsql does not support.
* **Enlist in the same scope as the scheduler calls**; set inside an `async` helper, it does not flow back.
* **Commit inside the `using` block.** Disposing signals the loop; disposed before the commit, the loop
  finds nothing, and the trigger waits for the next sweep (bounded by `IdleWaitTime`).
* **Keep the transaction short.** The store's locks are held until it completes, blocking acquisition, the
  misfire handler and cluster check-in.
* **One database.** Different schemas are fine; one `DbTransaction` cannot span two servers.

The transaction is opened by hand because, as of 6.35.0, Wolverine's `[Transactional]` gets nothing from the
raw-ADO.NET Postgres package: neither `Wolverine.Postgresql` nor `Wolverine.RDBMS` defines an
`IPersistenceFrameProvider`. `[Transactional] Handle(T msg, NpgsqlTransaction tx)` with plain
`PersistMessagesWithPostgresql` compiles, then fails with "JasperFx was unable to resolve a variable of type
Npgsql.NpgsqlTransaction". With Marten or EF Core, `[Transactional]` can supply the provider's session or
`DbContext`, but its generated commit runs after the handler returns, after the enlistment scope has disposed;
the trigger then waits for the next sweep.

## What this recipe does not do

* **It is not a package.** Install `Quartz` and `WolverineFx`. Nothing here is covered by Quartz.NET's API
  compatibility promises; prefer a first-party JasperFx integration if one ships.
* **It does not put Quartz under Wolverine's leader election.** A persistent store with `UseClustering()`
  owns triggers; the agent in [Letting Wolverine start the scheduler](#letting-wolverine-start-the-scheduler)
  only decides which node *runs a scheduler*.
* **It does not make in-memory scheduling durable.** A restart loses every pending trigger, as it loses
  Wolverine's in-memory scheduled envelopes.
* **It does not replace Wolverine's scheduling.** Use `ScheduleAsync` and `TimeoutMessage` for delayed
  messages and saga timeouts, and `opts.Schedules` (6.34+) for recurring publishes that may skip missed
  runs. Use Quartz for a misfire policy, a calendar, changes while running, or operator visibility.

## See also

* [One-Off Job](one-off-job.md) — the `ScheduleJob<TJob, TInput>` one-liner in isolation
* [Rescheduling Jobs](rescheduling-jobs.md) — changing a live schedule, and recovering a failed trigger
* [Job Template](job-template.md) — the recommended skeleton for a job class
* [Running Quartz under Aspire](aspire.md) — telemetry, health and the database, wired to an AppHost
* [Cron Expression Reference](../cron-expressions.md) — the cron field and special-character syntax
* [Configuration Reference](../configuration/reference.md) — every option, typed and legacy
