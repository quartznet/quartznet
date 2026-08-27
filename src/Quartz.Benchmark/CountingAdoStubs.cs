using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Benchmark;

/// <summary>
/// A provider that counts round trips and, when asked, spends the time one would cost.
/// </summary>
/// <remarks>
/// Shared by the benchmarks that measure how many round trips a path takes rather than what any one of
/// them costs against a real database. What each read answers is the benchmark's own business, so the
/// connection is handed a reader factory; everything else about the family is the same wherever it is
/// used, and duplicating it once per benchmark would let the two copies drift.
/// </remarks>
internal sealed class CountingConnection : DbConnection
{
    private static readonly double ticksPerMicrosecond = Stopwatch.Frequency / 1_000_000.0;

    /// <summary>A spin-waited stand-in for network latency; zero measures client-side cost alone.</summary>
    public int RoundTripMicroseconds { get; init; }

    /// <summary>What a read answers. Defaults to a reader with no rows.</summary>
    public Func<DbDataReader> ReaderFactory { get; init; } = static () => new EmptyReader();

    /// <summary>What a scalar read answers.</summary>
    public object? ScalarResult { get; init; }

    public bool CanBatch { get; set; } = true;

    public int RoundTrips { get; set; }

    public override bool CanCreateBatch => CanBatch;

    /// <summary>
    /// Counts one round trip and, at a non-zero <see cref="RoundTripMicroseconds" />, spends what one
    /// would cost. The spin is deliberate: a timer-based delay on Windows quantises to milliseconds,
    /// which is four times the interval usually being modelled.
    /// </summary>
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

/// <summary>
/// A provider over one <see cref="CountingConnection" />, for the delegate's own initialization.
/// </summary>
internal sealed class CountingDbProvider : IDbProvider
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

/// <inheritdoc cref="CountingConnection" />
internal sealed class CountingBatch : DbBatch
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

/// <inheritdoc cref="CountingConnection" />
internal sealed class CountingBatchCommandCollection : DbBatchCommandCollection
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

/// <inheritdoc cref="CountingConnection" />
internal sealed class CountingBatchCommand : DbBatchCommand
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

/// <inheritdoc cref="CountingConnection" />
internal sealed class CountingCommand : DbCommand
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
    protected override DbParameter CreateDbParameter() => new StubDbParameter();

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

    public override object? ExecuteScalar()
    {
        owner.RecordRoundTrip();
        return owner.ScalarResult;
    }

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        owner.RecordRoundTrip();
        return Task.FromResult(owner.ScalarResult);
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        owner.RecordRoundTrip();
        return owner.ReaderFactory();
    }

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        owner.RecordRoundTrip();
        return Task.FromResult(owner.ReaderFactory());
    }
}

/// <summary>
/// A reader over a fixed column list and a fixed set of rows, answering by name or by ordinal.
/// </summary>
internal class TableReader : DbDataReader
{
    private readonly string[] columns;
    private readonly object[][] rows;
    private int next;

    public TableReader(string[] columns, object[][] rows)
    {
        this.columns = columns;
        this.rows = rows;
    }

    private object[] Current => rows[next - 1];

    public override bool Read() => next++ < rows.Length;

    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

    public override int GetOrdinal(string name) => Array.IndexOf(columns, name);

    public override object GetValue(int ordinal) => Current[ordinal];

    public override string GetString(int ordinal) => (string) Current[ordinal];

    public override object this[int ordinal] => Current[ordinal];

    public override object this[string name] => Current[GetOrdinal(name)];

    public override int FieldCount => columns.Length;
    public override bool HasRows => rows.Length > 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => 0;
    public override int Depth => 0;
    public override bool NextResult() => false;
    public override bool IsDBNull(int ordinal) => Current[ordinal] is DBNull;
    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => Task.FromResult(IsDBNull(ordinal));
    public override bool GetBoolean(int ordinal) => (bool) Current[ordinal];
    public override byte GetByte(int ordinal) => 0;
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => '\0';
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override string GetDataTypeName(int ordinal) => "";
    public override DateTime GetDateTime(int ordinal) => (DateTime) Current[ordinal];
    public override decimal GetDecimal(int ordinal) => 0;
    public override double GetDouble(int ordinal) => 0;
    public override Type GetFieldType(int ordinal) => Current[ordinal].GetType();
    public override float GetFloat(int ordinal) => 0;
    public override Guid GetGuid(int ordinal) => Guid.Empty;
    public override short GetInt16(int ordinal) => 0;
    public override int GetInt32(int ordinal) => Convert.ToInt32(Current[ordinal]);
    public override long GetInt64(int ordinal) => Convert.ToInt64(Current[ordinal]);
    public override string GetName(int ordinal) => columns[ordinal];
    public override int GetValues(object[] values) => 0;
    public override System.Collections.IEnumerator GetEnumerator() => throw new NotSupportedException();
}

/// <inheritdoc cref="CountingConnection" />
internal sealed class EmptyReader : TableReader
{
    public EmptyReader()
        : base([], [])
    {
    }
}

/// <summary>
/// The rows <c>SelectTrigger</c> and <c>SelectTriggers</c> read: the trigger table's columns, the
/// simple-trigger columns the fast path joins in, and the key columns the set read projects.
/// </summary>
/// <remarks>
/// The column order is <c>StdAdoConstants.TriggerSelectColumns</c>'s, because JOB_DATA is read at
/// ordinal 11 positionally rather than by name.
/// </remarks>
internal static class TriggerRows
{
    private static readonly string[] columns =
    [
        AdoConstants.ColumnJobName,
        AdoConstants.ColumnJobGroup,
        AdoConstants.ColumnDescription,
        AdoConstants.ColumnNextFireTime,
        AdoConstants.ColumnPreviousFireTime,
        AdoConstants.ColumnTriggerType,
        AdoConstants.ColumnStartTime,
        AdoConstants.ColumnEndTime,
        AdoConstants.ColumnCalendarName,
        AdoConstants.ColumnMisfireInstruction,
        AdoConstants.ColumnPriority,
        AdoConstants.ColumnJobDataMap,
        AdoConstants.ColumnCronExpression,
        AdoConstants.ColumnTimeZoneId,
        AdoConstants.ColumnRepeatCount,
        AdoConstants.ColumnRepeatInterval,
        AdoConstants.ColumnTimesTriggered,
        AdoConstants.ColumnMisfireOriginalFireTime,
        AdoConstants.ColumnExecutionGroup,
        AdoConstants.ColumnPreferredNode,
        AdoConstants.ColumnPreferredNodeAuto,
        AdoConstants.ColumnTriggerName,
        AdoConstants.ColumnTriggerGroup
    ];

    /// <summary>Builds a reader over <paramref name="count" /> simple triggers named t0, t1, ….</summary>
    public static DbDataReader Reader(int count, DateTimeOffset nextFireTime)
    {
        object[][] rows = new object[count][];
        for (int i = 0; i < count; i++)
        {
            rows[i] = Row("t" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), nextFireTime);
        }

        return new TableReader(columns, rows);
    }

    private static object[] Row(string name, DateTimeOffset nextFireTime) =>
    [
        "j1",
        "jg1",
        DBNull.Value,
        nextFireTime.UtcTicks,
        DBNull.Value,
        AdoConstants.TriggerTypeSimple,
        nextFireTime.AddHours(-1).UtcTicks,
        DBNull.Value,
        DBNull.Value,
        0,
        5,
        DBNull.Value,
        DBNull.Value,
        DBNull.Value,
        -1L,
        TimeSpan.FromMinutes(1).Ticks,
        0,
        DBNull.Value,
        DBNull.Value,
        DBNull.Value,
        DBNull.Value,
        name,
        "g1"
    ];
}
