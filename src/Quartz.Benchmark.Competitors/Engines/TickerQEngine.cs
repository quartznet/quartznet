using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace Quartz.Benchmark.Competitors.Engines;

/// <summary>
/// TickerQ 10.4.0, on the generic host its own documentation puts it on.
/// </summary>
/// <remarks>
/// <para>
/// <b>What one firing does.</b> A background service polls: it asks the persistence provider for the
/// earliest due tickers — which on the in-memory provider is a LINQ scan of every ticker it holds,
/// filtered, ordered and materialised twice — marks the whole second's worth <c>Queued</c>, sleeps until
/// they are due (never less than <c>MinPollingInterval</c>), marks them <c>InProgress</c>, and queues
/// each onto its own task scheduler. Running one costs a
/// <c>CreateAsyncScope</c>, a linked <see cref="CancellationTokenSource" />, a
/// <c>TickerFunctionContext</c>, a <c>Stopwatch</c> pair, a registration in a static cancellation-token
/// manager and a final status write through the provider — in memory as well as on a database.
/// The job instance itself is newed up by source-generated code rather than resolved from the scope.
/// </para>
/// <para>
/// <b>A ticker due within one second never reaches that loop.</b> <c>TickerManager.AddTimeTickerAsync</c>
/// compares <c>ExecutionTime</c> against <c>now.AddSeconds(1)</c> and, when it is inside, acquires and
/// dispatches the ticker on the calling thread. That is why the throughput scenarios schedule two
/// seconds out — measuring a dispatch that bypasses the scheduler against two schedulers that do not
/// bypass theirs would not be a comparison — and why the latency scenario deliberately uses it.
/// </para>
/// <para>
/// <b>Discovery.</b> The source generator emits <c>TickerQInstanceFactoryExtensions.Initialize</c> with
/// a <c>[ModuleInitializer]</c> on it, so the functions in this assembly register themselves when the
/// module loads. No host wiring, no assembly scan, and it works in a console project — verified by
/// reading <c>obj/…/generated/TickerQ.SourceGenerator/…/TickerQInstanceFactory.g.cs</c>.
/// </para>
/// </remarks>
internal sealed class TickerQEngine : IEngine
{
    private readonly Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>>? configureStore;
    private readonly Func<IHost, CancellationToken, ValueTask>? prepareStore;
    private readonly TimeSpan minPollingInterval;
    private readonly List<Guid> timeTickerIds = [];
    private readonly List<Guid> cronTickerIds = [];

    private IHost? host;

    public TickerQEngine(
        string name,
        TimeSpan minPollingInterval,
        Action<TickerOptionsBuilder<TimeTickerEntity, CronTickerEntity>>? configureStore = null,
        Func<IHost, CancellationToken, ValueTask>? prepareStore = null)
    {
        Name = name;
        this.minPollingInterval = minPollingInterval;
        this.configureStore = configureStore;
        this.prepareStore = prepareStore;
    }

    public string Name { get; }

    private IHost Host => host ?? throw new InvalidOperationException("Start has not been called.");

    public ITimeTickerManager<TimeTickerEntity> TimeTickers
        => Host.Services.GetRequiredService<ITimeTickerManager<TimeTickerEntity>>();

    public ICronTickerManager<CronTickerEntity> CronTickers
        => Host.Services.GetRequiredService<ICronTickerManager<CronTickerEntity>>();

    public async ValueTask Start(int maxConcurrency, CancellationToken cancellationToken = default)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        // The generic host's default console logger would write a line per job. Quartz's standalone
        // builder registers no provider either, so both sides log into nothing.
        builder.Logging.ClearProviders();

        builder.Services.AddTickerQ(options =>
        {
            options.MinPollingInterval = minPollingInterval;
            options.ConfigureScheduler(scheduler =>
            {
                // The default is Environment.ProcessorCount, which on this machine is 32. Set to the
                // same worker limit as the other two so the row is about the scheduler.
                scheduler.MaxConcurrency = maxConcurrency;
            });

            configureStore?.Invoke(options);
        });

        host = builder.Build();

        if (prepareStore is not null)
        {
            await prepareStore(host, cancellationToken).ConfigureAwait(false);
        }

        host.UseTickerQ();
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ScheduleOneOff(int count, DateTimeOffset dueAt, CancellationToken cancellationToken = default)
    {
        List<TimeTickerEntity> tickers = new(count);
        for (int i = 0; i < count; i++)
        {
            tickers.Add(new TimeTickerEntity
            {
                Function = CountingTickerFunction.Name,
                ExecutionTime = dueAt.UtcDateTime,
            });
        }

        // The batch API, which is what the other two are given as well.
        await TimeTickers.AddBatchAsync(tickers, cancellationToken).ConfigureAwait(false);

        foreach (TimeTickerEntity ticker in tickers)
        {
            timeTickerIds.Add(ticker.Id);
        }
    }

    public async ValueTask ScheduleRecurring(int count, CancellationToken cancellationToken = default)
    {
        List<CronTickerEntity> tickers = new(count);
        for (int i = 0; i < count; i++)
        {
            tickers.Add(new CronTickerEntity
            {
                Function = CountingTickerFunction.Name,

                // Six fields, seconds first. TickerQ.Utilities.CronExpression.NormalizeToSixPart treats
                // a five-field expression as a six-field one with a leading "0", so the six-field form
                // is the one that means "every second".
                Expression = "* * * * * *",
            });
        }

        await CronTickers.AddBatchAsync(tickers, cancellationToken).ConfigureAwait(false);

        foreach (CronTickerEntity ticker in tickers)
        {
            cronTickerIds.Add(ticker.Id);
        }
    }

    /// <summary>
    /// One schedule through <c>ITimeTickerManager.AddAsync</c> or <c>ICronTickerManager.AddAsync</c>,
    /// which is what the per-schedule cost scenario measures.
    /// </summary>
    public async ValueTask ScheduleOne(int index, bool cron, DateTimeOffset dueAt, CancellationToken cancellationToken = default)
    {
        if (cron)
        {
            CronTickerEntity ticker = new()
            {
                Function = CountingTickerFunction.Name,
                Expression = "0 0 12 * * *",
                Description = index.ToString(CultureInfo.InvariantCulture),
            };

            await CronTickers.AddAsync(ticker, cancellationToken).ConfigureAwait(false);
            cronTickerIds.Add(ticker.Id);
        }
        else
        {
            TimeTickerEntity ticker = new()
            {
                Function = CountingTickerFunction.Name,
                ExecutionTime = dueAt.UtcDateTime,
                Description = index.ToString(CultureInfo.InvariantCulture),
            };

            await TimeTickers.AddAsync(ticker, cancellationToken).ConfigureAwait(false);
            timeTickerIds.Add(ticker.Id);
        }
    }

    /// <summary>
    /// One ticker with no execution time, which is TickerQ's "run this now".
    /// </summary>
    /// <remarks>
    /// <c>AddTimeTickerAsync</c> reads a null <c>ExecutionTime</c> as the clock's current instant, which
    /// is inside its one-second immediate window, so the ticker is acquired and dispatched by this call
    /// rather than by the scheduler loop. That is the fastest path TickerQ has and it is deliberately
    /// what the latency row measures — against two schedulers that wake a loop instead, which the
    /// README says in as many words.
    /// </remarks>
    public async ValueTask ScheduleNow(int index, CancellationToken cancellationToken = default)
    {
        TimeTickerEntity ticker = new()
        {
            Function = CountingTickerFunction.Name,
            ExecutionTime = null,
            Description = index.ToString(CultureInfo.InvariantCulture),
        };

        await TimeTickers.AddAsync(ticker, cancellationToken).ConfigureAwait(false);
        timeTickerIds.Add(ticker.Id);
    }

    /// <summary>
    /// Deletes every ticker this engine created, in batches.
    /// </summary>
    /// <remarks>
    /// There is no delete-all on either manager, and the in-memory provider's dictionaries are
    /// <see langword="static" /> — so they survive a host being torn down and rebuilt inside one
    /// process, and a clear that rebuilt the host would clear nothing. The ids are tracked on the way
    /// in for exactly this.
    /// </remarks>
    public async ValueTask Clear(CancellationToken cancellationToken = default)
    {
        const int chunk = 5_000;

        for (int offset = 0; offset < timeTickerIds.Count; offset += chunk)
        {
            List<Guid> slice = timeTickerIds.GetRange(offset, Math.Min(chunk, timeTickerIds.Count - offset));
            await TimeTickers.DeleteBatchAsync(slice, cancellationToken).ConfigureAwait(false);
        }

        for (int offset = 0; offset < cronTickerIds.Count; offset += chunk)
        {
            List<Guid> slice = cronTickerIds.GetRange(offset, Math.Min(chunk, cronTickerIds.Count - offset));
            await CronTickers.DeleteBatchAsync(slice, cancellationToken).ConfigureAwait(false);
        }

        timeTickerIds.Clear();
        cronTickerIds.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (host is null)
        {
            return;
        }

        try
        {
            await Clear().ConfigureAwait(false);
            await host.StopAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }
        finally
        {
            host.Dispose();
            host = null;
        }
    }
}

/// <summary>
/// The TickerQ side of the counting job.
/// </summary>
/// <remarks>
/// The class is newed up by the generated factory on every execution because it has a parameterless
/// constructor; a constructor parameter would be resolved from the per-execution scope instead. The
/// method returns a <see cref="Task" /> rather than <see langword="void" /> because the 10.4.0 generator
/// emits an <c>async</c> lambda body around a <see langword="void" /> ticker function and the result
/// does not compile (CS1643, "not all code paths return a value") — which is a fact about the generator
/// rather than a choice made here, and is recorded in README.md.
/// </remarks>
public sealed class CountingTickerFunction
{
    /// <summary>The name the ticker rows carry, so the string is written once.</summary>
    public const string Name = "count";

    [TickerFunction(Name)]
    public Task Execute(TickerFunctionContext context)
    {
        Completion.Record();
        Recurring.Record(new DateTimeOffset(DateTime.SpecifyKind(context.ScheduledFor, DateTimeKind.Utc)));
        return Task.CompletedTask;
    }
}
