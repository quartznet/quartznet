#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

#nullable enable

using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// An <see cref="IDbProvider" /> over a real one that records every statement and batch the store sends,
/// and can stand in a <see cref="DbBatch" /> for a driver that has none.
/// </summary>
/// <remarks>
/// <para>
/// A round trip is a <see cref="DbCommand" /> executed or a <see cref="DbBatch" /> executed, whichever
/// statements the batch carries; opening the connection and committing are not counted, since every
/// path pays them alike. The text of each is kept, so a test can say which statements a round made and
/// in what order rather than how many.
/// </para>
/// <para>
/// With <see cref="CountingDbProvider.EmulateBatches" /> the connection says it can batch, and a batch is
/// executed as its statements one after another on the driver's own commands — which is what SQLite has
/// to offer — counted as the one round trip a batching driver would make. The store's batching paths
/// then run against a real schema, with the per-command counts a batching driver reports.
/// </para>
/// </remarks>
internal sealed class CountingDbProvider : IDbProvider
{
    private readonly IDbProvider inner;

    public CountingDbProvider(IDbProvider inner, bool emulateBatches)
    {
        this.inner = inner;
        EmulateBatches = emulateBatches;
    }

    /// <summary>Whether connections claim to batch, and run a batch as its statements in turn.</summary>
    public bool EmulateBatches { get; }

    /// <summary>Every command and batch executed, in order, since the last <see cref="Reset" />.</summary>
    public List<RecordedRoundTrip> RoundTrips { get; } = [];

    /// <summary>
    /// Runs before a command or batch executes, with the text of the statement about to run — a batch's
    /// first — and the driver's connection and transaction it will run on, so that a test can interleave
    /// a write of its own between two of the store's statements, inside the store's transaction.
    /// </summary>
    public Func<string, DbConnection, DbTransaction?, Task>? BeforeCommand { get; set; }

    internal Task BeforeExecute(string sql, DbConnection? connection, DbTransaction? transaction)
    {
        return BeforeCommand is { } hook && connection is not null ? hook(sql, connection, transaction) : Task.CompletedTask;
    }

    public DbCommand CreateCommand() => new CountingCommand(inner.CreateCommand(), this, connection: null);

    public DbConnection CreateConnection() => new CountingConnection(inner.CreateConnection(), this);

    public string ConnectionString => inner.ConnectionString;

    public DbMetadata Metadata => inner.Metadata;

    public void Shutdown() => inner.Shutdown();

    public void Reset() => RoundTrips.Clear();

    internal void Record(RecordedRoundTrip roundTrip) => RoundTrips.Add(roundTrip);
}

/// <summary>One round trip the provider recorded: a statement, or a batch of them.</summary>
/// <param name="Statements">The statement texts, one for a command and as many as a batch carried.</param>
internal sealed record RecordedRoundTrip(List<string> Statements)
{
    public bool IsBatch => Statements.Count > 1 || BatchOfOne;

    public bool BatchOfOne { get; init; }
}

/// <inheritdoc cref="CountingDbProvider" />
internal sealed class CountingConnection : DbConnection
{
    private readonly CountingDbProvider provider;

    public CountingConnection(DbConnection inner, CountingDbProvider provider)
    {
        Inner = inner;
        this.provider = provider;
    }

    public DbConnection Inner { get; }

    /// <summary>The driver's transaction the connection last began, for a command to run inside.</summary>
    public DbTransaction? InnerTransaction { get; private set; }

    [AllowNull]
    public override string ConnectionString
    {
        get => Inner.ConnectionString;
        set => Inner.ConnectionString = value;
    }

    public override string Database => Inner.Database;
    public override string DataSource => Inner.DataSource;
    public override string ServerVersion => Inner.ServerVersion;
    public override ConnectionState State => Inner.State;
    public override bool CanCreateBatch => provider.EmulateBatches || Inner.CanCreateBatch;

    public override void ChangeDatabase(string databaseName) => Inner.ChangeDatabase(databaseName);
    public override void Open() => Inner.Open();
    public override Task OpenAsync(CancellationToken cancellationToken) => Inner.OpenAsync(cancellationToken);
    public override void Close() => Inner.Close();
    public override Task CloseAsync() => Inner.CloseAsync();

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        InnerTransaction = Inner.BeginTransaction(isolationLevel);
        return new CountingTransaction(InnerTransaction, this);
    }

    protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
    {
        InnerTransaction = await Inner.BeginTransactionAsync(isolationLevel, cancellationToken);
        return new CountingTransaction(InnerTransaction, this);
    }

    protected override DbCommand CreateDbCommand() => new CountingCommand(Inner.CreateCommand(), provider, this);

    protected override DbBatch CreateDbBatch()
    {
        return provider.EmulateBatches
            ? new EmulatedBatch(provider, this)
            : throw new NotSupportedException("The driver cannot batch, and emulation is off.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await Inner.DisposeAsync();
        await base.DisposeAsync();
    }

    public static DbConnection? Unwrap(DbConnection? connection) => connection is CountingConnection counting ? counting.Inner : connection;
}

/// <inheritdoc cref="CountingDbProvider" />
internal sealed class CountingTransaction : DbTransaction
{
    private readonly CountingConnection connection;

    public CountingTransaction(DbTransaction inner, CountingConnection connection)
    {
        Inner = inner;
        this.connection = connection;
    }

    public DbTransaction Inner { get; }

    protected override DbConnection? DbConnection => connection;
    public override IsolationLevel IsolationLevel => Inner.IsolationLevel;

    public override void Commit() => Inner.Commit();
    public override Task CommitAsync(CancellationToken cancellationToken = default) => Inner.CommitAsync(cancellationToken);
    public override void Rollback() => Inner.Rollback();
    public override Task RollbackAsync(CancellationToken cancellationToken = default) => Inner.RollbackAsync(cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public static DbTransaction? Unwrap(DbTransaction? transaction) => transaction is CountingTransaction counting ? counting.Inner : transaction;
}

/// <inheritdoc cref="CountingDbProvider" />
internal sealed class CountingCommand : DbCommand
{
    private readonly DbCommand inner;
    private readonly CountingDbProvider provider;
    private DbConnection? connection;
    private DbTransaction? transaction;

    public CountingCommand(DbCommand inner, CountingDbProvider provider, CountingConnection? connection)
    {
        this.inner = inner;
        this.provider = provider;
        this.connection = connection;
    }

    [AllowNull]
    public override string CommandText
    {
        get => inner.CommandText;
        set => inner.CommandText = value;
    }

    public override int CommandTimeout
    {
        get => inner.CommandTimeout;
        set => inner.CommandTimeout = value;
    }

    public override CommandType CommandType
    {
        get => inner.CommandType;
        set => inner.CommandType = value;
    }

    public override bool DesignTimeVisible
    {
        get => inner.DesignTimeVisible;
        set => inner.DesignTimeVisible = value;
    }

    public override UpdateRowSource UpdatedRowSource
    {
        get => inner.UpdatedRowSource;
        set => inner.UpdatedRowSource = value;
    }

    protected override DbConnection? DbConnection
    {
        get => connection;
        set
        {
            connection = value;
            inner.Connection = CountingConnection.Unwrap(value);
        }
    }

    protected override DbTransaction? DbTransaction
    {
        get => transaction;
        set
        {
            transaction = value;
            inner.Transaction = CountingTransaction.Unwrap(value);
        }
    }

    protected override DbParameterCollection DbParameterCollection => inner.Parameters;

    public override void Cancel() => inner.Cancel();
    public override void Prepare() => inner.Prepare();
    protected override DbParameter CreateDbParameter() => inner.CreateParameter();

    public override int ExecuteNonQuery()
    {
        int rows = inner.ExecuteNonQuery();
        Record();
        return rows;
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        await provider.BeforeExecute(inner.CommandText, inner.Connection, inner.Transaction);
        int rows = await inner.ExecuteNonQueryAsync(cancellationToken);
        Record();
        return rows;
    }

    public override object? ExecuteScalar()
    {
        object? value = inner.ExecuteScalar();
        Record();
        return value;
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        await provider.BeforeExecute(inner.CommandText, inner.Connection, inner.Transaction);
        object? value = await inner.ExecuteScalarAsync(cancellationToken);
        Record();
        return value;
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        DbDataReader reader = inner.ExecuteReader(behavior);
        Record();
        return reader;
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        await provider.BeforeExecute(inner.CommandText, inner.Connection, inner.Transaction);
        DbDataReader reader = await inner.ExecuteReaderAsync(behavior, cancellationToken);
        Record();
        return reader;
    }

    private void Record()
    {
        provider.Record(new RecordedRoundTrip([inner.CommandText]));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// A <see cref="DbBatch" /> over a driver without one: its commands run one after another on the
/// driver's own commands when it executes, and each reports the rows it affected.
/// </summary>
internal sealed class EmulatedBatch : DbBatch
{
    private readonly CountingDbProvider provider;
    private readonly CountingConnection connection;
    private readonly EmulatedBatchCommandCollection commands = [];
    private DbTransaction? transaction;

    public EmulatedBatch(CountingDbProvider provider, CountingConnection connection)
    {
        this.provider = provider;
        this.connection = connection;
    }

    protected override DbBatchCommandCollection DbBatchCommands => commands;

    public override int Timeout { get; set; }

    protected override DbConnection? DbConnection
    {
        get => connection;
        set
        {
            if (!ReferenceEquals(value, connection))
            {
                throw new InvalidOperationException("An emulated batch runs on the connection that made it.");
            }
        }
    }

    protected override DbTransaction? DbTransaction
    {
        get => transaction;
        set => transaction = value;
    }

    protected override DbBatchCommand CreateDbBatchCommand() => new EmulatedBatchCommand();

    public override int ExecuteNonQuery() => ExecuteNonQueryAsync().GetAwaiter().GetResult();

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        DbTransaction? driverTransaction = CountingTransaction.Unwrap(transaction) ?? connection.InnerTransaction;
        if (commands.Count > 0)
        {
            await provider.BeforeExecute(commands[0].CommandText, connection.Inner, driverTransaction);
        }

        int total = 0;
        List<string> statements = new(commands.Count);
        foreach (DbBatchCommand command in commands)
        {
            EmulatedBatchCommand emulated = (EmulatedBatchCommand) command;
            using DbCommand driverCommand = connection.Inner.CreateCommand();
            driverCommand.CommandText = emulated.CommandText;
            driverCommand.Transaction = driverTransaction;
            foreach (DbParameter parameter in emulated.Parameters)
            {
                driverCommand.Parameters.Add(parameter);
            }

            int rows = await driverCommand.ExecuteNonQueryAsync(cancellationToken);
            emulated.SetRecordsAffected(rows);
            total += rows;
            statements.Add(emulated.CommandText);
        }

        provider.Record(new RecordedRoundTrip(statements) { BatchOfOne = statements.Count == 1 });
        return total;
    }

    public override object? ExecuteScalar() => throw new NotSupportedException();
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) => throw new NotSupportedException();
    public override void Prepare() { }
    public override Task PrepareAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public override void Cancel() { }
}

/// <inheritdoc cref="EmulatedBatch" />
internal sealed class EmulatedBatchCommand : DbBatchCommand
{
    private readonly ListParameterCollection parameters = new();
    private int recordsAffected = -1;

    [AllowNull]
    public override string CommandText { get; set; } = "";
    public override CommandType CommandType { get; set; }
    public override int RecordsAffected => recordsAffected;
    protected override DbParameterCollection DbParameterCollection => parameters;

    /// <summary>What the statement affected when the batch ran it, as a batching driver reports per command.</summary>
    internal void SetRecordsAffected(int rows) => recordsAffected = rows;

    // False, like several shipped providers: the delegate then mints parameters from a throwaway
    // command, which is the path those providers take.
    public override bool CanCreateParameter => false;
}

/// <inheritdoc cref="EmulatedBatch" />
internal sealed class EmulatedBatchCommandCollection : DbBatchCommandCollection
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

/// <summary>A parameter collection that holds the driver's parameters until a batch hands them to its commands.</summary>
internal sealed class ListParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> items = [];

    public override int Count => items.Count;
    public override object SyncRoot => items;

    public override int Add(object? value)
    {
        items.Add((DbParameter) value!);
        return items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (object value in values)
        {
            items.Add((DbParameter) value);
        }
    }

    public override void Clear() => items.Clear();
    public override bool Contains(object? value) => value is DbParameter parameter && items.Contains(parameter);
    public override bool Contains(string value) => IndexOf(value) >= 0;
    public override void CopyTo(Array array, int index) => ((ICollection) items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => items.GetEnumerator();
    public override int IndexOf(object? value) => value is DbParameter parameter ? items.IndexOf(parameter) : -1;

    public override int IndexOf(string parameterName)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].ParameterName == parameterName)
            {
                return i;
            }
        }

        return -1;
    }

    public override void Insert(int index, object? value) => items.Insert(index, (DbParameter) value!);
    public override void Remove(object? value) => items.Remove((DbParameter) value!);
    public override void RemoveAt(int index) => items.RemoveAt(index);
    public override void RemoveAt(string parameterName) => items.RemoveAt(IndexOf(parameterName));
    protected override DbParameter GetParameter(int index) => items[index];
    protected override DbParameter GetParameter(string parameterName) => items[IndexOf(parameterName)];
    protected override void SetParameter(int index, DbParameter value) => items[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) => items[IndexOf(parameterName)] = value;
}
