using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Benchmark;

/// <summary>
/// ADO.NET stand-ins shared by the benchmarks that measure command preparation and parameter binding.
/// </summary>
/// <remarks>
/// They implement just enough of <see cref="DbCommand" /> and its neighbours to be prepared and bound,
/// and nothing that talks to a database: what is measured is the managed work done before a statement
/// is sent, and a real driver would only add noise the benchmark cannot control.
/// </remarks>
internal sealed class StubDbProvider : IDbProvider
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

/// <inheritdoc cref="StubDbProvider" />
internal sealed class StubDbCommand : DbCommand
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

/// <inheritdoc cref="StubDbProvider" />
internal sealed class StubDbParameter : DbParameter
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

/// <inheritdoc cref="StubDbProvider" />
internal sealed class StubParameterCollection : DbParameterCollection
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

/// <inheritdoc cref="StubDbProvider" />
internal sealed class StubDbConnection : DbConnection
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
