using System.Data;
using System.Data.Common;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Covers the batched misfire write path. SQLite reports CanCreateBatch = false so the integration
/// tests cannot reach it, and the providers that can batch all need a live server.
/// </summary>
public class UpdateMisfiredTriggersBatchTest
{
    [Test]
    public async Task UsesOneBatchForTheWholeUpdate_WhenProviderSupportsBatching()
    {
        var connection = new StubBatchingConnection();
        var conn = new ConnectionAndTransactionHolder(connection, null);
        CountingDelegate del = CreateDelegate();

        await del.UpdateMisfiredTriggers(conn, CreateUpdates(5));

        connection.Batches.Should().HaveCount(1, "the whole batch should go out as one round-trip");
        connection.Batches[0].ExecuteCount.Should().Be(1);

        // One narrow TRIGGERS update plus one SIMPLE_TRIGGERS update per trigger.
        connection.Batches[0].Commands.Should().HaveCount(10);
        connection.Batches[0].Commands.Count(x => x.CommandText.Contains("UPDATE {0}TRIGGERS".Replace("{0}", "QRTZ_"))).Should().Be(5);
        connection.Batches[0].Commands.Count(x => x.CommandText.Contains("QRTZ_SIMPLE_TRIGGERS")).Should().Be(5);

        del.PreparedCommands.Should().BeEmpty("nothing should have been issued as a standalone command");
    }

    [Test]
    public async Task BindsParametersOnBatchCommands_WhenProviderCannotCreateThemItself()
    {
        // DbBatchCommand.CreateParameter throws by default and several providers still have not
        // implemented it, so the delegate has to mint parameters from a command instead.
        var connection = new StubBatchingConnection();
        var conn = new ConnectionAndTransactionHolder(connection, null);
        CountingDelegate del = CreateDelegate();

        await del.UpdateMisfiredTriggers(conn, CreateUpdates(1));

        StubBatchCommand triggerUpdate = connection.Batches[0].Commands[0];
        triggerUpdate.Parameters.Count.Should().BeGreaterThan(0, "the statement must not go out unbound");

        var names = triggerUpdate.Parameters.Cast<DbParameter>().Select(x => x.ParameterName).ToArray();
        names.Should().Contain("@triggerState");
        names.Should().Contain("@triggerName");
        names.Should().Contain("@triggerGroup");
    }

    [Test]
    public async Task FallsBackToIndividualStatements_WhenProviderCannotBatch()
    {
        var connection = new StubBatchingConnection { SupportsBatching = false };
        var conn = new ConnectionAndTransactionHolder(connection, null);
        CountingDelegate del = CreateDelegate();

        await del.UpdateMisfiredTriggers(conn, CreateUpdates(3));

        connection.Batches.Should().BeEmpty();
        del.PreparedCommands.Should().HaveCount(6, "each trigger still needs its two statements");
    }

    /// <summary>
    /// A batch fails as a unit, so one bad trigger would otherwise block the whole recovery pass.
    /// </summary>
    [Test]
    public async Task FallsBackToIndividualStatements_WhenBatchExecutionFails()
    {
        var connection = new StubBatchingConnection { FailBatchExecution = true };
        var conn = new ConnectionAndTransactionHolder(connection, null);
        CountingDelegate del = CreateDelegate();

        await del.UpdateMisfiredTriggers(conn, CreateUpdates(3));

        connection.Batches.Should().HaveCount(1, "the batch should have been attempted");
        del.PreparedCommands.Should().HaveCount(6, "every statement should still have been issued after the batch failed");
    }

    /// <summary>
    /// Full recovery runs unbounded, so a pass can produce thousands of statements. They must not all be
    /// handed to the provider as one batch.
    /// </summary>
    [Test]
    public async Task ChunksLargeBatches()
    {
        var connection = new StubBatchingConnection();
        var conn = new ConnectionAndTransactionHolder(connection, null);
        CountingDelegate del = CreateDelegate();

        // 250 triggers is 500 statements, which has to span more than one batch.
        await del.UpdateMisfiredTriggers(conn, CreateUpdates(250));

        connection.Batches.Should().HaveCountGreaterThan(1);
        connection.Batches.Should().OnlyContain(x => x.Commands.Count <= 100);
        connection.Batches.Sum(x => x.Commands.Count).Should().Be(500, "every statement should still be issued exactly once");
        del.PreparedCommands.Should().BeEmpty();
    }

    [Test]
    public async Task DoesNothingForAnEmptyBatch()
    {
        var connection = new StubBatchingConnection();
        var conn = new ConnectionAndTransactionHolder(connection, null);
        CountingDelegate del = CreateDelegate();

        await del.UpdateMisfiredTriggers(conn, []);

        connection.Batches.Should().BeEmpty();
        del.PreparedCommands.Should().BeEmpty();
    }

    private static List<MisfiredTriggerUpdate> CreateUpdates(int count)
    {
        var updates = new List<MisfiredTriggerUpdate>();
        for (var i = 0; i < count; i++)
        {
            var trigger = new SimpleTriggerImpl("t" + i, "g", DateTimeOffset.UtcNow)
            {
                JobKey = new JobKey("j" + i, "jg"),
                RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
                RepeatInterval = TimeSpan.FromMinutes(1)
            };
            trigger.SetNextFireTimeUtc(DateTimeOffset.UtcNow.AddMinutes(1));

            updates.Add(new MisfiredTriggerUpdate(trigger, AdoConstants.StateWaiting, null));
        }

        return updates;
    }

    private static CountingDelegate CreateDelegate()
    {
        var dbProvider = A.Fake<IDbProvider>();
        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata { ParameterNamePrefix = "@", BindByName = true });
        A.CallTo(() => dbProvider.CreateCommand()).ReturnsLazily(() => new StubDbCommand());

        var del = new CountingDelegate();
        del.Initialize(new DelegateInitializationArgs
        {
            TablePrefix = "QRTZ_",
            InstanceId = "TESTSCHED",
            InstanceName = "INSTANCE",
            TypeLoadHelper = new SimpleTypeLoadHelper(),
            UseProperties = false,
            InitString = "",
            DbProvider = dbProvider,
            ObjectSerializer = A.Fake<IObjectSerializer>(),
            TimeProvider = TimeProvider.System
        });

        return del;
    }

    /// <summary>
    /// Records every statement issued as a standalone command, which is how the tests tell the batched
    /// path from the fallback.
    /// </summary>
    private sealed class CountingDelegate : StdAdoDelegate
    {
        public List<string> PreparedCommands { get; } = [];

        public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            PreparedCommands.Add(commandText);
            var cmd = new StubDbCommand { CommandText = commandText };
            cth.Attach(cmd);
            return cmd;
        }
    }

    private sealed class StubBatchingConnection : DbConnection
    {
        public bool SupportsBatching { get; init; } = true;

        public bool FailBatchExecution { get; init; }

        public List<StubBatch> Batches { get; } = [];

        public override bool CanCreateBatch => SupportsBatching;

        protected override DbBatch CreateDbBatch()
        {
            var batch = new StubBatch { Fail = FailBatchExecution };
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

    private sealed class StubBatch : DbBatch
    {
        private readonly StubBatchCommandCollection commands = [];

        public bool Fail { get; init; }

        public int ExecuteCount { get; private set; }

        public List<StubBatchCommand> Commands => commands.Items;

        protected override DbBatchCommandCollection DbBatchCommands => commands;

        protected override DbBatchCommand CreateDbBatchCommand() => new StubBatchCommand();

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            if (Fail)
            {
                throw new InvalidOperationException("batch execution failed");
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

    private sealed class StubBatchCommandCollection : DbBatchCommandCollection
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

    private sealed class StubBatchCommand : DbBatchCommand
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

    private sealed class StubDbCommand : DbCommand
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

    private sealed class RecordingParameterCollection : DbParameterCollection
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

    private sealed class StubDbParameter : DbParameter
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
}
