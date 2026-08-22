using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using BenchmarkDotNet.Attributes;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Benchmark;

/// <summary>
/// Preparing one statement and binding its parameters, which is what every trigger acquisition, every
/// trigger write and every check-in does before it reaches the database.
/// </summary>
/// <remarks>
/// The statement is the real acquisition SQL — around a kilobyte once the table prefix is folded in —
/// and it binds the seven parameters acquisition binds. The interesting case is a driver that does not
/// spell parameters with '@': Npgsql and Oracle use ':', MySQL '?'. Binding used to rewrite the whole
/// statement once per parameter; it is now rewritten once per statement and remembered.
/// </remarks>
[MemoryDiagnoser]
public class AdoParameterBindingBenchmark
{
    /// <summary>'@' is the no-op case, ':' the one that has to rewrite.</summary>
    [Params("@", ":")]
    public string ParameterNamePrefix { get; set; } = "@";

    private static readonly (string Name, object Value)[] acquisitionParameters =
    [
        ("schedulerName", "TestScheduler"),
        ("state", "WAITING"),
        ("noLaterThan", 638000000000000000L),
        ("noEarlierThan", 637000000000000000L),
        ("instanceId", "NODE-01"),
        ("autoPinSentinel", "*"),
        ("liveNodeCutoff", 637900000000000000L)
    ];

    private AdoUtil adoUtil = null!;
    private ConnectionAndTransactionHolder holder = null!;
    private string sql = null!;

    [GlobalSetup]
    public void Setup()
    {
        DbMetadata metadata = new()
        {
            ParameterNamePrefix = ParameterNamePrefix,
            BindByName = true
        };

        adoUtil = new AdoUtil(new StubDbProvider(metadata));
        holder = new ConnectionAndTransactionHolder(new StubDbConnection(), null);
        sql = AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectNextTriggerToAcquire, "QRTZ_");
    }

    /// <summary>
    /// What binding used to do: copy and scan the whole statement once for every parameter bound to it.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int RewritePerParameter()
    {
        DbCommand command = new StubDbCommand { CommandText = sql };
        holder.Attach(command);

        foreach ((string name, object value) in acquisitionParameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = ParameterNamePrefix + name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
            command.CommandText = LegacyRewrite(command.CommandText, name, ParameterNamePrefix);
        }

        return command.CommandText.Length;
    }

    /// <summary>
    /// What it does now: the statement is rewritten as the command is prepared, and the result is
    /// remembered against the text it came from, so this scan happens once per statement per process.
    /// </summary>
    [Benchmark]
    public int RewriteOncePerStatement()
    {
        DbCommand command = adoUtil.PrepareCommand(holder, sql);

        foreach ((string name, object value) in acquisitionParameters)
        {
            adoUtil.AddCommandParameter(command, name, value);
        }

        return command.CommandText.Length;
    }

    private static string LegacyRewrite(string commandText, string parameterName, string prefix)
    {
        return prefix == "@" ? commandText : commandText.Replace("@" + parameterName, prefix + parameterName);
    }

    private sealed class StubDbProvider : IDbProvider
    {
        public StubDbProvider(DbMetadata metadata) => Metadata = metadata;

        public DbCommand CreateCommand() => new StubDbCommand();

        public DbConnection CreateConnection() => new StubDbConnection();

        public string ConnectionString { get; set; } = "";

        public DbMetadata Metadata { get; }

        public void Shutdown()
        {
        }
    }

    private sealed class StubDbCommand : DbCommand
    {
        private readonly StubParameterCollection parameters = new();

        [AllowNull]
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => 0;

        public override object? ExecuteScalar() => null;

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new StubDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class StubDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = "";
        public override int Size { get; set; }
        [AllowNull]
        public override string SourceColumn { get; set; } = "";
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }

        public override void ResetDbType()
        {
        }
    }

    private sealed class StubParameterCollection : DbParameterCollection
    {
        private readonly List<object> items = [];

        public override int Count => items.Count;
        public override object SyncRoot => items;

        public override int Add(object? value)
        {
            items.Add(value!);
            return items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (object value in values)
            {
                items.Add(value);
            }
        }

        public override void Clear() => items.Clear();

        public override bool Contains(object? value) => value is not null && items.Contains(value);

        public override bool Contains(string value) => IndexOf(value) >= 0;

        public override void CopyTo(Array array, int index) => ((ICollection) items).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => items.GetEnumerator();

        public override int IndexOf(object? value) => value is null ? -1 : items.IndexOf(value);

        public override int IndexOf(string parameterName)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (((DbParameter) items[i]).ParameterName == parameterName)
                {
                    return i;
                }
            }

            return -1;
        }

        public override void Insert(int index, object? value) => items.Insert(index, value!);

        public override void Remove(object? value) => items.Remove(value!);

        public override void RemoveAt(int index) => items.RemoveAt(index);

        public override void RemoveAt(string parameterName) => items.RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => (DbParameter) items[index];

        protected override DbParameter GetParameter(string parameterName) => (DbParameter) items[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value) => items[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value) => items[IndexOf(parameterName)] = value;
    }

    private sealed class StubDbConnection : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = "";
        public override string Database => "";
        public override string DataSource => "";
        public override string ServerVersion => "";
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new StubDbCommand();
    }
}
