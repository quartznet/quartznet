using System.Data;
using System.Data.Common;

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// An in-memory ADO.NET provider that records what the store issued, and can be told whether it
/// supports batching and whether a batch fails.
/// </summary>
/// <remarks>
/// Shared, because the batched write paths cannot be reached from the integration tests: SQLite is
/// the only database those run without Docker and it reports <c>CanCreateBatch = false</c>, while
/// every provider that can batch needs a live server. The failure and fallback branches need a
/// provider that can be made to misbehave on demand, which no real one can.
/// </remarks>
internal sealed class StubBatchingConnection : DbConnection
{
    public bool SupportsBatching { get; init; } = true;

    public bool FailBatchExecution { get; init; }

    /// <summary>
    /// What a failing batch throws, so that a test can choose a failure the store recognises as
    /// transient and one it does not.
    /// </summary>
    public Func<Exception> BatchFailure { get; init; }

    public List<StubBatch> Batches { get; } = [];

    public override bool CanCreateBatch => SupportsBatching;

    protected override DbBatch CreateDbBatch()
    {
        var batch = new StubBatch
        {
            Failure = BatchFailure ?? (FailBatchExecution ? () => new InvalidOperationException("batch execution failed") : null)
        };
        Batches.Add(batch);
        return batch;
    }

    protected override DbCommand CreateDbCommand() => new StubDbCommand();

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

internal sealed class StubBatch : DbBatch
{
    private readonly StubBatchCommandCollection commands = [];

    public Func<Exception> Failure { get; init; }

    public int ExecuteCount { get; private set; }

    public List<StubBatchCommand> Commands => commands.Items;

    protected override DbBatchCommandCollection DbBatchCommands => commands;

    protected override DbBatchCommand CreateDbBatchCommand() => new StubBatchCommand();

    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        ExecuteCount++;
        if (Failure is not null)
        {
            throw Failure();
        }

        return Task.FromResult(commands.Count);
    }

    public override int ExecuteNonQuery() => throw new NotSupportedException();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) => throw new NotSupportedException();
    public override object ExecuteScalar() => throw new NotSupportedException();
    public override Task<object> ExecuteScalarAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public override int Timeout { get; set; }
    protected override DbConnection DbConnection { get; set; }
    protected override DbTransaction DbTransaction { get; set; }
    public override void Prepare() { }
    public override Task PrepareAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public override void Cancel() { }
    public override void Dispose() { }
}

internal sealed class StubBatchCommandCollection : DbBatchCommandCollection
{
    public List<StubBatchCommand> Items { get; } = [];

    public override int Count => Items.Count;
    public override bool IsReadOnly => false;
    public override void Add(DbBatchCommand item) => Items.Add((StubBatchCommand) item);
    public override void Clear() => Items.Clear();
    public override bool Contains(DbBatchCommand item) => Items.Contains(item);
    public override void CopyTo(DbBatchCommand[] array, int arrayIndex) => Items.CopyTo(array.Cast<StubBatchCommand>().ToArray(), arrayIndex);
    public override IEnumerator<DbBatchCommand> GetEnumerator() => Items.Cast<DbBatchCommand>().GetEnumerator();
    public override int IndexOf(DbBatchCommand item) => Items.IndexOf((StubBatchCommand) item);
    public override void Insert(int index, DbBatchCommand item) => Items.Insert(index, (StubBatchCommand) item);
    public override bool Remove(DbBatchCommand item) => Items.Remove((StubBatchCommand) item);
    public override void RemoveAt(int index) => Items.RemoveAt(index);
    protected override DbBatchCommand GetBatchCommand(int index) => Items[index];
    protected override void SetBatchCommand(int index, DbBatchCommand batchCommand) => Items[index] = (StubBatchCommand) batchCommand;
}

internal sealed class StubBatchCommand : DbBatchCommand
{
    private readonly RecordingParameterCollection parameters = new();

    public override string CommandText { get; set; } = "";
    public override CommandType CommandType { get; set; }
    public override int RecordsAffected => 0;
    protected override DbParameterCollection DbParameterCollection => parameters;

    // Left at the default (false) on purpose: the delegate has to cope with providers that have not
    // implemented CreateParameter on batch commands.
    public override bool CanCreateParameter => false;
}

internal sealed class StubDbCommand : DbCommand
{
    private readonly RecordingParameterCollection parameters = new();

    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection => parameters;
    protected override DbTransaction DbTransaction { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override void Cancel() { }
    public override int ExecuteNonQuery() => 0;
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    public override object ExecuteScalar() => null;
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new StubDbParameter();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
}

internal sealed class RecordingParameterCollection : DbParameterCollection
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
        foreach (var value in values)
        {
            Add(value);
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

internal sealed class StubDbParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override string ParameterName { get; set; } = "";
    public override string SourceColumn { get; set; } = "";
    public override bool SourceColumnNullMapping { get; set; }
    public override object Value { get; set; }
    public override int Size { get; set; }
    public override void ResetDbType() { }
}

/// <summary>
/// Records every statement issued as a standalone command, which is how the batching tests tell the
/// batched path from the fallback.
/// </summary>
internal sealed class CountingDelegate : StdAdoDelegate
{
    /// <summary>
    /// A delegate wired to <see cref="StubBatchingConnection" />'s provider, with the '@' parameter
    /// prefix and by-name binding the shipped databases mostly use.
    /// </summary>
    public static CountingDelegate Create()
    {
        var dbProvider = A.Fake<IDbProvider>();
        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata { ParameterNamePrefix = "@", BindByName = true });
        A.CallTo(() => dbProvider.CreateCommand()).ReturnsLazily(() => new StubDbCommand());

        var del = new CountingDelegate();
        del.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            SchedulerName = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            UseProperties = false,
            DbProvider = dbProvider,
            ObjectSerializer = A.Fake<IObjectSerializer>(),
            TimeProvider = TimeProvider.System
        });

        return del;
    }

    public List<string> PreparedCommands { get; } = [];

    public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
    {
        PreparedCommands.Add(commandText);
        var cmd = new StubDbCommand { CommandText = commandText };
        cth.Attach(cmd);
        return cmd;
    }
}
