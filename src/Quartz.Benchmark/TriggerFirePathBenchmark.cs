using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using BenchmarkDotNet.Attributes;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Triggers;

namespace Quartz.Benchmark;

/// <summary>
/// One trigger's fire, through the ADO driver delegate, against a provider that does nothing but count
/// and delay. This is the work the trigger-access lock is held across, so the round trips it takes are
/// the window every other node in a cluster waits on.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Sequential" /> issues exactly the statements the fire path used to issue — the trigger's
/// state, its existence, the fired-trigger row, the sibling-state updates for a serial job, and
/// <c>UpdateTrigger</c>, which in turn selects the trigger's type and writes both of its rows.
/// <see cref="Batched" /> issues what it makes now: <c>SelectTriggerHeader</c>, which carries the state,
/// the existence and the type in one read, and <c>ApplyTriggerFired</c>, which is every write in one
/// <see cref="DbBatch" />. The job read is the same on both sides and is left out; it is not what changed.
/// </para>
/// <para>
/// Every call but one goes through a shipped delegate member. The exception is the fired-trigger row:
/// <c>IDriverDelegate.UpdateFiredTrigger</c> was deleted along with the last caller of it, so
/// <see cref="UpdateFiredTriggerRow" /> reproduces what it did — the same statement, the same eleven
/// parameters in the same order, through the same <c>PrepareCommand</c> and <c>AddCommandParameter</c>,
/// against a table prefix substituted once rather than per call, and without disposing the command,
/// because that is what the deleted member did. A baseline that quietly improves on the code it stands
/// in for understates what replacing it was worth.
/// </para>
/// <para>
/// <see cref="RoundTripMicroseconds" /> is a spin-waited stand-in for network latency, so the numbers at
/// a non-zero value are a model rather than a measurement of any real database. At zero it measures what
/// the client actually spends preparing statements and binding parameters, which is not a model at all.
/// The round trips themselves are counted exactly by <c>ApplyTriggerFiredBatchTest</c>.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class TriggerFirePathBenchmark
{
    /// <summary>Zero measures client-side cost alone; 250 µs is a modest same-datacentre round trip.</summary>
    [Params(0, 250)]
    public int RoundTripMicroseconds { get; set; }

    /// <summary>Whether the job disallows concurrent execution, which is what adds the sibling updates.</summary>
    [Params(false, true)]
    public bool SerialJob { get; set; }

    private CountingConnection connection = null!;
    private ConnectionAndTransactionHolder holder = null!;
    private StdAdoDelegate driverDelegate = null!;
    private SimpleTriggerImpl trigger = null!;
    private IJobDetail job = null!;

    /// <summary>
    /// Substituted once, which is what the delegate's own cache did for the member this replaces.
    /// </summary>
    private string updateFiredTriggerSql = null!;

    [GlobalSetup]
    public void Setup()
    {
        connection = new CountingConnection
        {
            RoundTripMicroseconds = RoundTripMicroseconds,
            ScalarResult = AdoConstants.StateAcquired,
            ReaderFactory = static () => new TableReader(
                [
                    AdoConstants.ColumnTriggerState,
                    AdoConstants.ColumnNextFireTime,
                    AdoConstants.ColumnJobName,
                    AdoConstants.ColumnJobGroup,
                    AdoConstants.ColumnTriggerType
                ],
                [
                    [
                        AdoConstants.StateAcquired,
                        new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero).UtcTicks,
                        "j1",
                        "jg1",
                        AdoConstants.TriggerTypeSimple
                    ]
                ]),
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

        trigger = new SimpleTriggerImpl
        {
            Key = new TriggerKey("t1", "g1"),
            JobKey = new JobKey("j1", "jg1"),
            StartTimeUtc = new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            RepeatInterval = TimeSpan.FromMinutes(1),
            FireInstanceId = "fire-1",
        };
        trigger.NextFireTimeUtc = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

        job = JobBuilder.Create<BenchmarkFireJob>().WithIdentity(trigger.JobKey).Build();

        updateFiredTriggerSql = AdoJobStoreUtil.ReplaceTablePrefixCached(StdAdoConstants.SqlUpdateFiredTrigger, "QRTZ_");
    }

    /// <summary>What the fire path did: one statement per round trip.</summary>
    [Benchmark(Baseline = true)]
    public async ValueTask<int> Sequential()
    {
        connection.CanBatch = true;
        connection.RoundTrips = 0;

        await driverDelegate.SelectTriggerState(holder, trigger.Key).ConfigureAwait(false);
        await driverDelegate.TriggerExists(holder, trigger.Key).ConfigureAwait(false);
        await UpdateFiredTriggerRow().ConfigureAwait(false);

        if (SerialJob)
        {
            await driverDelegate.UpdateTriggerStatesForJobFromOtherState(holder, job.Key, StoredTriggerState.Blocked, StoredTriggerState.Waiting).ConfigureAwait(false);
            await driverDelegate.UpdateTriggerStatesForJobFromOtherState(holder, job.Key, StoredTriggerState.Blocked, StoredTriggerState.Acquired).ConfigureAwait(false);
            await driverDelegate.UpdateTriggerStatesForJobFromOtherState(holder, job.Key, StoredTriggerState.PausedBlocked, StoredTriggerState.Paused).ConfigureAwait(false);
        }

        await driverDelegate.UpdateTrigger(holder, trigger, StoredTriggerState.Waiting, job).ConfigureAwait(false);

        return connection.RoundTrips;
    }

    /// <summary>
    /// The fired-trigger write the fire path used to make, as its own round trip. See the remarks on the
    /// class for why this one statement is bound here rather than through a delegate member — including
    /// why the command is deliberately not disposed.
    /// </summary>
#pragma warning disable CA2000
    private async ValueTask UpdateFiredTriggerRow()
    {
        DbCommand cmd = driverDelegate.PrepareCommand(holder, updateFiredTriggerSql);
        driverDelegate.AddCommandParameter(cmd, "schedulerName", "BenchmarkScheduler");
        driverDelegate.AddCommandParameter(cmd, "instanceName", "NODE-01");
        driverDelegate.AddCommandParameter(cmd, "firedTime", driverDelegate.GetDbDateTimeValue(TimeProvider.System.GetUtcNow()));
        driverDelegate.AddCommandParameter(cmd, "scheduledTime", driverDelegate.GetDbDateTimeValue(trigger.NextFireTimeUtc));
        driverDelegate.AddCommandParameter(cmd, "entryState", StoredTriggerStates.ToStoredValue(StoredTriggerState.Executing));
        driverDelegate.AddCommandParameter(cmd, "jobName", trigger.JobKey.Name);
        driverDelegate.AddCommandParameter(cmd, "jobGroup", trigger.JobKey.Group);
        driverDelegate.AddCommandParameter(cmd, "isNonConcurrent", driverDelegate.GetDbBooleanValue(job.ConcurrentExecutionDisallowed));
        driverDelegate.AddCommandParameter(cmd, "requestsRecover", driverDelegate.GetDbBooleanValue(job.RequestsRecovery));
        driverDelegate.AddCommandParameter(cmd, "executionGroup", (object?) trigger.ExecutionGroup ?? DBNull.Value);
        driverDelegate.AddCommandParameter(cmd, "entryId", trigger.FireInstanceId);

        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
#pragma warning restore CA2000

    /// <summary>What it does now, on a provider that can batch.</summary>
    [Benchmark]
    public async ValueTask<int> Batched()
    {
        connection.CanBatch = true;
        connection.RoundTrips = 0;

        await driverDelegate.SelectTriggerHeader(holder, trigger.Key).ConfigureAwait(false);
        await driverDelegate.ApplyTriggerFired(holder, CreateUpdate()).ConfigureAwait(false);

        return connection.RoundTrips;
    }

    /// <summary>
    /// And on a provider that cannot. The statements are the same ones, issued the way they always were,
    /// so this arm exists to show that such a provider is no worse off than before rather than to be
    /// faster than the arm above.
    /// </summary>
    [Benchmark]
    public async ValueTask<int> BatchedOnProviderWithoutBatchSupport()
    {
        connection.CanBatch = false;
        connection.RoundTrips = 0;

        await driverDelegate.SelectTriggerHeader(holder, trigger.Key).ConfigureAwait(false);
        await driverDelegate.ApplyTriggerFired(holder, CreateUpdate()).ConfigureAwait(false);

        return connection.RoundTrips;
    }

    private TriggerFiredUpdate CreateUpdate()
    {
        return new TriggerFiredUpdate
        {
            Trigger = trigger,
            JobDetail = job,
            NewState = SerialJob ? StoredTriggerState.Blocked : StoredTriggerState.Waiting,
            StoredTriggerType = AdoConstants.TriggerTypeSimple,
            ScheduledFireTimeUtc = trigger.NextFireTimeUtc,
            ClearMisfireOriginalFireTime = false,
            BlockJobTriggers = SerialJob,
        };
    }

    private sealed class BenchmarkFireJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class NoopObjectSerializer : IObjectSerializer
    {
        public byte[] Serialize<T>(T obj) where T : class => [];

        public T? Deserialize<T>(byte[] data) where T : class => null;
    }
}
