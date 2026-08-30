using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

using Quartz;
using Quartz.Examples.Wolverine;

using Wolverine;
using Wolverine.Postgresql;
using Wolverine.Runtime;

ExampleOptions options = ExampleOptions.FromArguments(args);

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// Wolverine first, so that its runtime is the first hosted service and everything registered below
// starts after it. The lambda is not optional on IHostApplicationBuilder — unlike the IHostBuilder
// overload, this one has no default for it.
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

// AutoStart = false is part 5. The scheduler is built and bound but not running; SchedulerStarter or
// QuartzSchedulerAgent presses start once Wolverine is up.
builder.Services.AddQuartzHostedService(hosted =>
{
    hosted.AutoStart = false;
    hosted.WaitForJobsToComplete = true;
});

Part5StartedByWolverine.Register(builder.Services, options);

using IHost host = builder.Build();
await host.StartAsync();

IMessageBus bus = host.Services.GetRequiredService<IMessageBus>();
IScheduler scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
IWolverineRuntime runtime = host.Services.GetRequiredService<IWolverineRuntime>();

if (options.HasDatabase)
{
    await Refunds.EnsureTable(options.PostgresConnectionString!);
}

Console.WriteLine(options.HasDatabase
    ? $"Quartz.NET + Wolverine, durable mode ({ExampleOptions.PostgresVariable} is set)."
    : $"Quartz.NET + Wolverine, in-memory mode. Set {ExampleOptions.PostgresVariable} for parts 5 and 6 in full.");
Console.WriteLine();

// Part 2: a Wolverine handler schedules a one-off typed job, correlated by trigger group.
await bus.PublishAsync(new OrderPlaced(Smoke.RemindedOrderId, 149.50m));

// Part 2 again, the cancelling half: this order is paid for before its reminder falls due.
// InvokeAsync rather than PublishAsync, because these two must be handled in this order and a local
// queue processes in parallel — a cancellation that arrives before the thing it cancels proves
// nothing. InvokeAsync runs the handler inline on this thread and returns when it is done.
await bus.InvokeAsync(new OrderPlaced(Smoke.PaidOrderId, 20.00m));
await bus.InvokeAsync(new OrderPaid(Smoke.PaidOrderId));

// Part 3: a serialized envelope stored now and sent through Wolverine when its trigger fires.
TriggerKey deferred = await Part3RawMessageData.ScheduleSend(
    scheduler,
    runtime,
    new ArchiveOrders("nightly housekeeping", OlderThanDays: 90),
    options.Smoke ? TimeSpan.FromSeconds(2) : TimeSpan.FromHours(1));

Ledger.Record(Events.RawEnvelopeStored, deferred.ToString());

if (options.HasDatabase)
{
    // Part 6: the application's row, the outgoing envelopes and the trigger, in one transaction.
    await bus.PublishAsync(new ApproveRefund("A-1003", 42.00m));
}

if (options.Smoke)
{
    int exitCode = await Smoke.RunAsync(options, TimeSpan.FromSeconds(20));
    await host.StopAsync();
    return exitCode;
}

Console.WriteLine("Running. Ctrl+C to stop.");
await host.WaitForShutdownAsync();
return 0;
