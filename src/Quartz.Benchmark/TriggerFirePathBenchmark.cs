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
/// Both arms run real, shipped delegate members. <see cref="Sequential" /> issues exactly the calls the
/// fire path used to make — <c>SelectTriggerState</c>, <c>TriggerExists</c>, <c>UpdateFiredTrigger</c>,
/// the sibling-state updates for a serial job, and <c>UpdateTrigger</c>, which in turn selects the
/// trigger's type and writes both of its rows. <see cref="Batched" /> issues what it makes now:
/// <c>SelectTriggerHeader</c>, which carries the state, the existence and the type in one read, and
/// <c>ApplyTriggerFired</c>, which is every write in one <see cref="DbBatch" />. The job read is the
/// same on both sides and is left out; it is not what changed.
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

    [GlobalSetup]
    public void Setup()
    {
        connection = new CountingConnection { RoundTripMicroseconds = RoundTripMicroseconds };
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
    }

    /// <summary>What the fire path did: one statement per round trip.</summary>
    [Benchmark(Baseline = true)]
    public async ValueTask<int> Sequential()
    {
        connection.CanBatch = true;
        connection.RoundTrips = 0;

        await driverDelegate.SelectTriggerState(holder, trigger.Key).ConfigureAwait(false);
        await driverDelegate.TriggerExists(holder, trigger.Key).ConfigureAwait(false);
        await driverDelegate.UpdateFiredTrigger(holder, trigger, StoredTriggerState.Executing, job).ConfigureAwait(false);

        if (SerialJob)
        {
            await driverDelegate.UpdateTriggerStatesForJobFromOtherState(holder, job.Key, StoredTriggerState.Blocked, StoredTriggerState.Waiting).ConfigureAwait(false);
            await driverDelegate.UpdateTriggerStatesForJobFromOtherState(holder, job.Key, StoredTriggerState.Blocked, StoredTriggerState.Acquired).ConfigureAwait(false);
            await driverDelegate.UpdateTriggerStatesForJobFromOtherState(holder, job.Key, StoredTriggerState.PausedBlocked, StoredTriggerState.Paused).ConfigureAwait(false);
        }

        await driverDelegate.UpdateTrigger(holder, trigger, StoredTriggerState.Waiting, job).ConfigureAwait(false);

        return connection.RoundTrips;
    }

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

    private sealed class CountingDbProvider : IDbProvider
    {
        private readonly CountingConnection connection;

        public CountingDbProvider(CountingConnection connection) => this.connection = connection;

        public DbCommand CreateCommand() => new CountingCommand(connection);

        public DbConnection CreateConnection() => connection;

        public string ConnectionString { get; set; } = "";

        public DbMetadata Metadata { get; } = new() { ParameterNamePrefix = "@", BindByName = true };

        public void Shutdown()
        {
        }
    }

    /// <summary>
    /// Counts round trips and, when asked, spends the time one would cost. The spin is deliberate: a
    /// timer-based delay on Windows quantises to milliseconds, which is four times the interval being
    /// modelled.
    /// </summary>
    private sealed class CountingConnection : DbConnection
    {
        private static readonly double ticksPerMicrosecond = Stopwatch.Frequency / 1_000_000.0;

        public int RoundTripMicroseconds { get; init; }

        public bool CanBatch { get; set; } = true;

        public int RoundTrips { get; set; }

        public override bool CanCreateBatch => CanBatch;

        public void RecordRoundTrip()
        {
            RoundTrips++;

            if (RoundTripMicroseconds == 0)
            {
                return;
            }

            long until = Stopwatch.GetTimestamp() + (long) (RoundTripMicroseconds * ticksPerMicrosecond);
            while (Stopwatch.GetTimestamp() < until)
            {
                Thread.SpinWait(10);
            }
        }

        protected override DbBatch CreateDbBatch() => new CountingBatch(this);

        protected override DbCommand CreateDbCommand() => new CountingCommand(this);

        [AllowNull]
        public override string ConnectionString { get; set; } = "";
        public override string Database => "";
        public override string DataSource => "";
        public override string ServerVersion => "";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    }

    private sealed class CountingBatch : DbBatch
    {
        private readonly CountingConnection owner;
        private readonly CountingBatchCommandCollection commands = [];

        public CountingBatch(CountingConnection owner) => this.owner = owner;

        protected override DbBatchCommandCollection DbBatchCommands => commands;

        protected override DbBatchCommand CreateDbBatchCommand() => new CountingBatchCommand();

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
        {
            owner.RecordRoundTrip();
            return Task.FromResult(commands.Count);
        }

        public override int ExecuteNonQuery() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) => throw new NotSupportedException();
        public override object ExecuteScalar() => throw new NotSupportedException();
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override int Timeout { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Prepare() { }
        public override Task PrepareAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override void Cancel() { }
        public override void Dispose() { }
    }

    private sealed class CountingBatchCommandCollection : DbBatchCommandCollection
    {
        private readonly List<DbBatchCommand> items = [];

        public override int Count => items.Count;
        public override bool IsReadOnly => false;
        public override void Add(DbBatchCommand item) => items.Add(item);
        public override void Clear() => items.Clear();
        public override bool Contains(DbBatchCommand item) => items.Contains(item);
        public override void CopyTo(DbBatchCommand[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);
        public override IEnumerator<DbBatchCommand> GetEnumerator() => items.GetEnumerator();
        public override int IndexOf(DbBatchCommand item) => items.IndexOf(item);
        public override void Insert(int index, DbBatchCommand item) => items.Insert(index, item);
        public override bool Remove(DbBatchCommand item) => items.Remove(item);
        public override void RemoveAt(int index) => items.RemoveAt(index);
        protected override DbBatchCommand GetBatchCommand(int index) => items[index];
        protected override void SetBatchCommand(int index, DbBatchCommand batchCommand) => items[index] = batchCommand;
    }

    private sealed class CountingBatchCommand : DbBatchCommand
    {
        private readonly StubParameterCollection parameters = new();

        [AllowNull]
        public override string CommandText { get; set; } = "";
        public override CommandType CommandType { get; set; }
        public override int RecordsAffected => 1;
        protected override DbParameterCollection DbParameterCollection => parameters;
        // False, like several shipped providers: the delegate then mints parameters from a throwaway
        // command instead, which is the path those providers actually take.
        public override bool CanCreateParameter => false;
    }

    private sealed class CountingCommand : DbCommand
    {
        private readonly CountingConnection owner;
        private readonly StubParameterCollection parameters = new();

        public CountingCommand(CountingConnection owner) => this.owner = owner;

        [AllowNull]
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new StubParameter();

        public override int ExecuteNonQuery()
        {
            owner.RecordRoundTrip();
            return 1;
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            owner.RecordRoundTrip();
            return Task.FromResult(1);
        }

        public override object ExecuteScalar()
        {
            owner.RecordRoundTrip();
            return AdoConstants.StateAcquired;
        }

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            owner.RecordRoundTrip();
            return Task.FromResult<object?>(AdoConstants.StateAcquired);
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            owner.RecordRoundTrip();
            return new TriggerRowReader();
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            owner.RecordRoundTrip();
            return Task.FromResult<DbDataReader>(new TriggerRowReader());
        }
    }

    /// <summary>
    /// One trigger row, with the columns the reads under benchmark ask for by name.
    /// </summary>
    private sealed class TriggerRowReader : DbDataReader
    {
        private static readonly string[] columns =
        [
            AdoConstants.ColumnTriggerState,
            AdoConstants.ColumnNextFireTime,
            AdoConstants.ColumnJobName,
            AdoConstants.ColumnJobGroup,
            AdoConstants.ColumnTriggerType
        ];

        private static readonly object[] values =
        [
            AdoConstants.StateAcquired,
            new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero).UtcTicks,
            "j1",
            "jg1",
            AdoConstants.TriggerTypeSimple
        ];

        private int read;

        public override bool Read() => read++ == 0;

        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

        public override int GetOrdinal(string name) => Array.IndexOf(columns, name);

        public override object GetValue(int ordinal) => values[ordinal];

        public override string GetString(int ordinal) => (string) values[ordinal];

        public override object this[int ordinal] => values[ordinal];

        public override object this[string name] => values[GetOrdinal(name)];

        public override int FieldCount => columns.Length;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override int Depth => 0;
        public override bool NextResult() => false;
        public override bool IsDBNull(int ordinal) => false;
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => Task.FromResult(false);
        public override bool GetBoolean(int ordinal) => false;
        public override byte GetByte(int ordinal) => 0;
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => '\0';
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override string GetDataTypeName(int ordinal) => "";
        public override DateTime GetDateTime(int ordinal) => default;
        public override decimal GetDecimal(int ordinal) => 0;
        public override double GetDouble(int ordinal) => 0;
        public override Type GetFieldType(int ordinal) => values[ordinal].GetType();
        public override float GetFloat(int ordinal) => 0;
        public override Guid GetGuid(int ordinal) => Guid.Empty;
        public override short GetInt16(int ordinal) => 0;
        public override int GetInt32(int ordinal) => 0;
        public override long GetInt64(int ordinal) => (long) values[ordinal];
        public override string GetName(int ordinal) => columns[ordinal];
        public override int GetValues(object[] values) => 0;
        public override System.Collections.IEnumerator GetEnumerator() => throw new NotSupportedException();
    }

    private sealed class StubParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> items = [];

        public override int Count => items.Count;
        public override object SyncRoot => items;
        public override int Add(object value)
        {
            items.Add((DbParameter) value);
            return items.Count - 1;
        }
        public override void AddRange(Array values)
        {
            foreach (object? value in values)
            {
                Add(value!);
            }
        }
        public override void Clear() => items.Clear();
        public override bool Contains(object value) => items.Contains((DbParameter) value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection) items).CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => items.GetEnumerator();
        public override int IndexOf(object value) => items.IndexOf((DbParameter) value);
        public override int IndexOf(string parameterName) => items.FindIndex(x => x.ParameterName == parameterName);
        public override void Insert(int index, object value) => items.Insert(index, (DbParameter) value);
        public override void Remove(object value) => items.Remove((DbParameter) value);
        public override void RemoveAt(int index) => items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => items.RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => items[index];
        protected override DbParameter GetParameter(string parameterName) => items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) => items[IndexOf(parameterName)] = value;
    }

    private sealed class StubParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = "";
        [AllowNull]
        public override string SourceColumn { get; set; } = "";
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }
}
