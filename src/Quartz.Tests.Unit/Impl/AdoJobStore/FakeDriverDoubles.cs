#nullable enable

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

// A driver made of nothing, for the tests that are about what Quartz does with a driver rather than
// about a database. A real driver would only add the requirement that its server be reachable; these
// carry the two things a driver description says beyond a type — a command's BindByName and a
// parameter's type — and count what was asked of them.

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A data source made of nothing, for the tests that are about what Quartz does with a data source
/// rather than about a database.
/// </summary>
internal sealed class FakeDataSource : DbDataSource
{
    public override string ConnectionString => "";

    protected override DbConnection CreateDbConnection() => new FakeConnection();
}

/// <summary>
/// The factory a driver ships, made of nothing. It is what a registration passes instead of naming the
/// driver's types, so it hands back the same fakes the description would have named.
/// </summary>
internal sealed class FakeDbProviderFactory : DbProviderFactory
{
    public static readonly FakeDbProviderFactory Instance = new();

    public override DbConnection CreateConnection() => new FakeConnection();

    public override DbCommand CreateCommand() => new FakeCommand();

    public override DbParameter CreateParameter() => new FakeParameter();
}

/// <summary>
/// A connection that counts the commands asked of it, which is all these tests look at.
/// </summary>
internal sealed class FakeConnection : DbConnection
{
    public int CommandsCreated { get; private set; }

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

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotSupportedException();

    protected override DbCommand CreateDbCommand()
    {
        CommandsCreated++;
        return new FakeCommand();
    }
}

internal sealed class FakeCommand : DbCommand
{
    /// <summary>
    /// The property the managed Oracle driver has, and the one thing a driver description says about
    /// a command beyond its type.
    /// </summary>
    public bool BindByName { get; set; } = true;

    [AllowNull]
    public override string CommandText { get; set; } = "";

    public override int CommandTimeout { get; set; }

    public override CommandType CommandType { get; set; }

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection { get; set; }

    protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override int ExecuteNonQuery() => throw new NotSupportedException();

    public override object? ExecuteScalar() => throw new NotSupportedException();

    public override void Prepare()
    {
    }

    protected override DbParameter CreateDbParameter() => new FakeParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
}

internal sealed class FakeParameterCollection : DbParameterCollection
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
        foreach (object value in values)
        {
            Add(value);
        }
    }

    public override void Clear() => parameters.Clear();

    public override bool Contains(object value) => parameters.Contains(value);

    public override bool Contains(string value) => IndexOf(value) >= 0;

    public override void CopyTo(Array array, int index) => ((System.Collections.ICollection) parameters).CopyTo(array, index);

    public override System.Collections.IEnumerator GetEnumerator() => parameters.GetEnumerator();

    public override int IndexOf(object value) => parameters.IndexOf((DbParameter) value);

    public override int IndexOf(string parameterName) => parameters.FindIndex(p => p.ParameterName == parameterName);

    public override void Insert(int index, object value) => parameters.Insert(index, (DbParameter) value);

    public override void Remove(object value) => parameters.Remove((DbParameter) value);

    public override void RemoveAt(int index) => parameters.RemoveAt(index);

    public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));

    protected override DbParameter GetParameter(int index) => parameters[index];

    protected override DbParameter GetParameter(string parameterName) => GetParameter(IndexOf(parameterName));

    protected override void SetParameter(int index, DbParameter value) => parameters[index] = value;

    protected override void SetParameter(string parameterName, DbParameter value) => SetParameter(IndexOf(parameterName), value);
}

internal sealed class FakeParameter : DbParameter
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
