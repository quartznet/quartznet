using BenchmarkDotNet.Attributes;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Benchmark;

/// <summary>
/// One acquisition round through the ADO driver delegate, against a provider that does nothing but
/// count and delay. This is the other half of the work the trigger-access lock is held across —
/// <see cref="TriggerFirePathBenchmark" /> measures what happens once a trigger has been acquired.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PerCandidate" /> issues exactly the statements the round used to issue: the acquisition
/// read has named the batch, and then each candidate is read back on its own, compare-and-swapped into
/// the acquired state, and written to the fired-triggers table — three round trips per candidate after
/// the read that found them. <see cref="PerRound" /> issues what it makes now: one
/// <c>SelectTriggers</c> for the whole batch, the same compare-and-swap per candidate, and one
/// <c>InsertFiredTriggers</c> that goes out as a single <see cref="System.Data.Common.DbBatch" />.
/// </para>
/// <para>
/// The compare-and-swap stays per candidate on both sides and is the reason the round does not collapse
/// to two statements: its result decides whether that candidate was acquired at all, and a batch reports
/// one total rather than an outcome per statement. See the pull request for #3424 for why that was not
/// traded away.
/// </para>
/// <para>
/// <see cref="PerRoundOnProviderWithoutBatchSupport" /> is the fallback arm: the fired-trigger rows go
/// out as the statements they always were, so a provider without <c>DbBatch</c> is no worse off than
/// before. <see cref="RoundTripMicroseconds" /> is a spin-waited stand-in for network latency, so the
/// numbers at a non-zero value are a model rather than a measurement of any real database; at zero it
/// measures what the client actually spends preparing statements and binding parameters. The round
/// trips themselves are counted exactly by <c>AcquisitionRoundTripTest</c>.
/// </para>
/// <para>
/// A <see cref="BatchSize" /> of one is the scheduler's default, and there is nothing to batch in it:
/// both arms take three round trips. It is a parameter here so that the set-shaped members can be held
/// to never costing more than the single-trigger ones they replace — which is what the one-element
/// paths in <c>SelectTriggers</c> and <c>InsertFiredTriggers</c> exist for.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class TriggerAcquisitionRoundBenchmark
{
    /// <summary>Zero measures client-side cost alone; 250 µs is a modest same-datacentre round trip.</summary>
    [Params(0, 250)]
    public int RoundTripMicroseconds { get; set; }

    /// <summary>What one acquisition asks for: the default of one, and a batch.</summary>
    [Params(1, 10)]
    public int BatchSize { get; set; }

    private static readonly DateTimeOffset NextFireTime = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    private CountingConnection connection = null!;
    private ConnectionAndTransactionHolder holder = null!;
    private StdAdoDelegate driverDelegate = null!;
    private TriggerKey[] keys = null!;
    private IOperableTrigger[] triggers = null!;

    [GlobalSetup]
    public void Setup()
    {
        connection = new CountingConnection
        {
            RoundTripMicroseconds = RoundTripMicroseconds,
            ReaderFactory = () => TriggerRows.Reader(BatchSize, NextFireTime),
        };
        holder = new ConnectionAndTransactionHolder(connection, null);

        driverDelegate = new StdAdoDelegate();
        driverDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            SchedulerName = "BenchmarkScheduler",
            InstanceId = "NODE-01",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = new CountingDbProvider(connection),
            ObjectSerializer = new NoopObjectSerializer(),
            TimeProvider = TimeProvider.System,
        });

        keys = new TriggerKey[BatchSize];
        triggers = new IOperableTrigger[BatchSize];
        for (int i = 0; i < BatchSize; i++)
        {
            keys[i] = new TriggerKey("t" + i, "g1");
            triggers[i] = Trigger("t" + i);
        }
    }

    /// <summary>What the round did: a read, a state update and a fired-trigger row per candidate.</summary>
    [Benchmark(Baseline = true)]
    public async ValueTask<int> PerCandidate()
    {
        connection.CanBatch = true;
        connection.RoundTrips = 0;

        for (int i = 0; i < BatchSize; i++)
        {
            await driverDelegate.SelectTrigger(holder, keys[i]).ConfigureAwait(false);
            await driverDelegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                holder, keys[i], StoredTriggerState.Acquired, StoredTriggerState.Waiting, NextFireTime).ConfigureAwait(false);
            await driverDelegate.InsertFiredTrigger(holder, triggers[i], StoredTriggerState.Acquired, null).ConfigureAwait(false);
        }

        return connection.RoundTrips;
    }

    /// <summary>What it does now, on a provider that can batch.</summary>
    [Benchmark]
    public async ValueTask<int> PerRound()
    {
        connection.CanBatch = true;
        connection.RoundTrips = 0;

        await Round().ConfigureAwait(false);

        return connection.RoundTrips;
    }

    /// <summary>
    /// And on a provider that cannot: the fired-trigger rows go out as the statements they always were,
    /// so this arm exists to show that such a provider is no worse off than before rather than to be
    /// faster than the arm above.
    /// </summary>
    [Benchmark]
    public async ValueTask<int> PerRoundOnProviderWithoutBatchSupport()
    {
        connection.CanBatch = false;
        connection.RoundTrips = 0;

        await Round().ConfigureAwait(false);

        return connection.RoundTrips;
    }

    private async ValueTask Round()
    {
        await driverDelegate.SelectTriggers(holder, keys).ConfigureAwait(false);

        for (int i = 0; i < BatchSize; i++)
        {
            await driverDelegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                holder, keys[i], StoredTriggerState.Acquired, StoredTriggerState.Waiting, NextFireTime).ConfigureAwait(false);
        }

        await driverDelegate.InsertFiredTriggers(holder, triggers, StoredTriggerState.Acquired, null).ConfigureAwait(false);
    }

    private static IOperableTrigger Trigger(string name)
    {
        Quartz.Impl.Triggers.SimpleTriggerImpl trigger = new()
        {
            Key = new TriggerKey(name, "g1"),
            JobKey = new JobKey("j1", "jg1"),
            StartTimeUtc = NextFireTime.AddHours(-1),
            RepeatCount = Quartz.Impl.Triggers.SimpleTriggerImpl.RepeatIndefinitely,
            RepeatInterval = TimeSpan.FromMinutes(1),
            FireInstanceId = name + "-fire",
        };
        trigger.NextFireTimeUtc = NextFireTime;
        return trigger;
    }

    private sealed class NoopObjectSerializer : IObjectSerializer
    {
        public byte[] Serialize<T>(T obj) where T : class => [];

        public T? Deserialize<T>(byte[] data) where T : class => null;
    }
}
