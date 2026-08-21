#nullable enable

using System.Data.Common;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// <see cref="DbSemaphore.ExecuteSql" /> is the one method a subclass exists to implement, and until
/// <see cref="DbSemaphore.PrepareCommand" /> and <see cref="DbSemaphore.AddCommandParameter" /> were
/// <c>protected</c> there was no way to implement it: the accessor that prepares a statement is
/// <c>private protected</c>, so a semaphore written outside this assembly could not see it. The
/// migration guide told such an author to "derive from <c>DbSemaphore</c> and use <c>IDbProvider</c>",
/// which does not work — a command minted from the provider is attached to no connection and no
/// transaction, so it would run outside the unit of work the lock is supposed to protect.
/// </summary>
/// <remarks>
/// This test is mostly its own subject: <see cref="OutsiderSemaphore" /> uses nothing that is not
/// <c>public</c> or <c>protected</c>, so it compiles exactly where a semaphore in someone else's
/// assembly would. If the helpers were narrowed again, this file would stop building.
/// </remarks>
public class DbSemaphoreSubclassContractTest
{
    [Test]
    public async Task ASemaphoreWrittenOutsideQuartzCanIssueItsOwnLockStatement()
    {
        var provider = new RecordingDbProvider();
        var semaphore = new OutsiderSemaphore(provider);
        semaphore.Initialize(new SemaphoreContext
        {
            SchedulerName = "TESTSCHED",
            InstanceId = "node-1",
            TablePrefix = "MYAPP_",
            CommandTimeout = TimeSpan.FromSeconds(20),
        });

        using var connection = new RecordingConnection();
        using var holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        bool obtained = await semaphore.ObtainLock(Guid.NewGuid(), holder, SchedulerLock.TriggerAccess);

        obtained.Should().BeTrue();

        provider.LastCommand.Should().NotBeNull();
        DbCommand command = provider.LastCommand!;
        command.CommandText.Should().Be("SELECT 1 FROM MYAPP_LOCKS WHERE SCHED_NAME = @schedulerName AND LOCK_NAME = @lockName",
            "the base class folds the store's table prefix into the statement it was constructed with");
        command.Connection.Should().BeSameAs(connection,
            "PrepareCommand attaches the command to the unit of work, which is the whole reason a subclass "
            + "cannot just mint one from IDbProvider");
        command.CommandTimeout.Should().Be(20, "the context's command timeout reaches a subclass's statements too");
        command.Parameters.Cast<DbParameter>().Select(x => x.Value)
            .Should().Equal(["TESTSCHED", "TRIGGER_ACCESS"]);
    }

    /// <summary>
    /// A lock handler as an author outside this assembly would write one: it derives from
    /// <see cref="DbSemaphore" />, overrides <see cref="DbSemaphore.ExecuteSql" />, and reaches for
    /// nothing beyond what that base class offers a derived type.
    /// </summary>
    private sealed class OutsiderSemaphore : DbSemaphore
    {
        private const string LockStatement =
            "SELECT 1 FROM {0}LOCKS WHERE SCHED_NAME = @schedulerName AND LOCK_NAME = @lockName";

        private const string InsertStatement =
            "INSERT INTO {0}LOCKS (SCHED_NAME, LOCK_NAME) VALUES (@schedulerName, @lockName)";

        public OutsiderSemaphore(IDbProvider dbProvider)
            : base("QRTZ_", schedulerName: null, LockStatement, InsertStatement, dbProvider)
        {
        }

        protected override async ValueTask ExecuteSql(
            Guid requestorId,
            ConnectionAndTransactionHolder conn,
            string lockName,
            string expandedSql,
            string expandedInsertSql,
            CancellationToken cancellationToken = default)
        {
            using DbCommand command = PrepareCommand(conn, expandedSql);
            AddCommandParameter(command, "schedulerName", SchedulerName);
            AddCommandParameter(command, "lockName", lockName);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class RecordingDbProvider : IDbProvider
    {
        public DbCommand? LastCommand { get; private set; }

        public string ConnectionString => "";

        public DbMetadata Metadata { get; } = new()
        {
            ProductName = "Fake",
            ParameterNamePrefix = "@",
            BindByName = true,
        };

        public DbCommand CreateCommand()
        {
            var command = new RecordingCommand();
            LastCommand = command;
            return command;
        }

        public DbConnection CreateConnection() => new RecordingConnection();

        public void Shutdown()
        {
        }
    }

    private sealed class RecordingCommand : DbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = "";

        public override int CommandTimeout { get; set; }

        public override System.Data.CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override System.Data.UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection { get; } = new RecordingParameterCollection();

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => 1;

        public override object? ExecuteScalar() => 1;

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new RecordingParameter();

        protected override DbDataReader ExecuteDbDataReader(System.Data.CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class RecordingConnection : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "";

        public override string Database => "";

        public override string DataSource => "";

        public override string ServerVersion => "";

        public override System.Data.ConnectionState State => System.Data.ConnectionState.Open;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class RecordingParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> parameters = [];

        public override int Count => parameters.Count;

        public override object SyncRoot { get; } = new();

        public override int Add(object value)
        {
            parameters.Add((DbParameter) value);
            return parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (object? value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => parameters.Clear();

        public override bool Contains(object value) => parameters.Contains((DbParameter) value);

        public override bool Contains(string value) => IndexOf(value) >= 0;

        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection) parameters).CopyTo(array, index);

        public override System.Collections.IEnumerator GetEnumerator() => parameters.GetEnumerator();

        public override int IndexOf(object value) => parameters.IndexOf((DbParameter) value);

        public override int IndexOf(string parameterName) => parameters.FindIndex(x => x.ParameterName == parameterName);

        public override void Insert(int index, object value) => parameters.Insert(index, (DbParameter) value);

        public override void Remove(object value) => parameters.Remove((DbParameter) value);

        public override void RemoveAt(int index) => parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => parameters[index];

        protected override DbParameter GetParameter(string parameterName) => parameters[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value) => parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value) => parameters[IndexOf(parameterName)] = value;
    }

    private sealed class RecordingParameter : DbParameter
    {
        public override System.Data.DbType DbType { get; set; }

        public override System.Data.ParameterDirection Direction { get; set; }

        public override bool IsNullable { get; set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ParameterName { get; set; } = "";

        public override int Size { get; set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string SourceColumn { get; set; } = "";

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override void ResetDbType()
        {
        }
    }
}
