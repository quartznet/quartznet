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

using System.Data;
using System.Data.Common;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// An <see cref="IDbProvider" /> over a real one whose next commit, once asked for with
/// <see cref="CommitNextLate" />, reaches the database late and fails at once (#4259).
/// </summary>
/// <remarks>
/// <para>
/// The commit hands its transaction to a task that commits it after <see cref="Delay" />, and throws. That
/// is a commit whose acknowledgement was lost while the commit was still settling on the server. Until
/// the task commits, the transaction is open on its connection: it holds its row locks, and no other
/// transaction sees what it wrote.
/// </para>
/// <para>
/// From the failure on, the transaction answers as Npgsql's does once its connector broke, so the store
/// skips the rollback, as it does there. The connection is closed only once the late commit has gone
/// out, so the commit lands whatever the store did in the meantime.
/// </para>
/// </remarks>
internal sealed class LateCommitDbProvider : IDbProvider
{
    private readonly IDbProvider inner;
    private int commitNextLate;

    public LateCommitDbProvider(IDbProvider inner)
    {
        this.inner = inner;
    }

    /// <summary>How long after the failed commit the transaction is committed.</summary>
    public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The last late commit handed over, which completes once it has gone out.</summary>
    public Task LateCommit { get; private set; } = Task.CompletedTask;

    /// <summary>Has the next commit reach the database late and fail at once.</summary>
    public void CommitNextLate() => Volatile.Write(ref commitNextLate, 1);

    internal bool TakeCommitNextLate() => Interlocked.Exchange(ref commitNextLate, 0) == 1;

    internal void HandOver(Task lateCommit) => LateCommit = lateCommit;

    public DbCommand CreateCommand() => new LateCommitCommand(inner.CreateCommand());

    public DbConnection CreateConnection() => new LateCommitConnection(inner.CreateConnection(), this);

    public string ConnectionString => inner.ConnectionString;

    public DbMetadata Metadata => inner.Metadata;

    public void Shutdown() => inner.Shutdown();

    internal static DbConnection Unwrap(DbConnection connection) => connection is LateCommitConnection late ? late.Inner : connection;

    internal static DbTransaction Unwrap(DbTransaction transaction) => transaction is LateCommitTransaction late ? late.Inner : transaction;
}

/// <inheritdoc cref="LateCommitDbProvider" />
internal sealed class LateCommitConnection : DbConnection
{
    private readonly LateCommitDbProvider provider;
    private Task lateCommit = Task.CompletedTask;

    public LateCommitConnection(DbConnection inner, LateCommitDbProvider provider)
    {
        Inner = inner;
        this.provider = provider;
    }

    public DbConnection Inner { get; }

    internal LateCommitDbProvider Provider => provider;

    public override string ConnectionString
    {
        get => Inner.ConnectionString;
        set => Inner.ConnectionString = value;
    }

    public override string Database => Inner.Database;
    public override string DataSource => Inner.DataSource;
    public override string ServerVersion => Inner.ServerVersion;
    public override ConnectionState State => Inner.State;
    public override bool CanCreateBatch => Inner.CanCreateBatch;

    internal void HandOver(Task commit)
    {
        lateCommit = commit;
        provider.HandOver(commit);
    }

    public override void ChangeDatabase(string databaseName) => Inner.ChangeDatabase(databaseName);
    public override void Open() => Inner.Open();
    public override Task OpenAsync(CancellationToken cancellationToken) => Inner.OpenAsync(cancellationToken);

    public override void Close()
    {
        AwaitLateCommit().GetAwaiter().GetResult();
        Inner.Close();
    }

    public override async Task CloseAsync()
    {
        await AwaitLateCommit();
        await Inner.CloseAsync();
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        return new LateCommitTransaction(Inner.BeginTransaction(isolationLevel), this);
    }

    protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
    {
        return new LateCommitTransaction(await Inner.BeginTransactionAsync(isolationLevel, cancellationToken), this);
    }

    protected override DbCommand CreateDbCommand() => new LateCommitCommand(Inner.CreateCommand()) { Connection = this };

    protected override DbBatch CreateDbBatch() => new LateCommitBatch(Inner.CreateBatch()) { Connection = this };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AwaitLateCommit().GetAwaiter().GetResult();
            Inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await AwaitLateCommit();
        await Inner.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <summary>Waits for a commit handed over to go out; whether it succeeded is the test's to ask.</summary>
    private async Task AwaitLateCommit()
    {
        try
        {
            await lateCommit;
        }
        catch (Exception)
        {
            // Read from LateCommitDbProvider.LateCommit by the test that asked for it.
        }
    }
}

/// <inheritdoc cref="LateCommitDbProvider" />
internal sealed class LateCommitTransaction : DbTransaction
{
    private readonly LateCommitConnection connection;
    private bool handedOver;

    public LateCommitTransaction(DbTransaction inner, LateCommitConnection connection)
    {
        Inner = inner;
        this.connection = connection;
    }

    public DbTransaction Inner { get; }

    protected override DbConnection DbConnection => handedOver ? throw new ObjectDisposedException("NpgsqlTransaction") : connection;

    public override IsolationLevel IsolationLevel => Inner.IsolationLevel;

    public override void Commit() => Inner.Commit();

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (!connection.Provider.TakeCommitNextLate())
        {
            await Inner.CommitAsync(cancellationToken);
            return;
        }

        handedOver = true;
        connection.HandOver(CommitLate(connection.Provider.Delay));
        throw new InvalidOperationException("The connection broke while the COMMIT was still settling on the server.");
    }

    private async Task CommitLate(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            await Inner.CommitAsync(CancellationToken.None);
        }
        finally
        {
            await Inner.DisposeAsync();
        }
    }

    public override void Rollback()
    {
        if (!handedOver)
        {
            Inner.Rollback();
        }
    }

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        return handedOver ? Task.CompletedTask : Inner.RollbackAsync(cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !handedOver)
        {
            Inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!handedOver)
        {
            await Inner.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}

/// <inheritdoc cref="LateCommitDbProvider" />
internal sealed class LateCommitCommand : DbCommand
{
    private readonly DbCommand inner;
    private DbConnection connection;
    private DbTransaction transaction;

    public LateCommitCommand(DbCommand inner)
    {
        this.inner = inner;
    }

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

    protected override DbConnection DbConnection
    {
        get => connection;
        set
        {
            connection = value;
            inner.Connection = value is null ? null : LateCommitDbProvider.Unwrap(value);
        }
    }

    protected override DbTransaction DbTransaction
    {
        get => transaction;
        set
        {
            transaction = value;
            inner.Transaction = value is null ? null : LateCommitDbProvider.Unwrap(value);
        }
    }

    protected override DbParameterCollection DbParameterCollection => inner.Parameters;

    public override void Cancel() => inner.Cancel();
    public override void Prepare() => inner.Prepare();
    public override Task PrepareAsync(CancellationToken cancellationToken = default) => inner.PrepareAsync(cancellationToken);
    protected override DbParameter CreateDbParameter() => inner.CreateParameter();

    public override int ExecuteNonQuery() => inner.ExecuteNonQuery();
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => inner.ExecuteNonQueryAsync(cancellationToken);
    public override object ExecuteScalar() => inner.ExecuteScalar();
    public override Task<object> ExecuteScalarAsync(CancellationToken cancellationToken) => inner.ExecuteScalarAsync(cancellationToken);
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => inner.ExecuteReader(behavior);

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        return inner.ExecuteReaderAsync(behavior, cancellationToken);
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

/// <inheritdoc cref="LateCommitDbProvider" />
internal sealed class LateCommitBatch : DbBatch
{
    private readonly DbBatch inner;
    private DbConnection connection;
    private DbTransaction transaction;

    public LateCommitBatch(DbBatch inner)
    {
        this.inner = inner;
    }

    protected override DbBatchCommandCollection DbBatchCommands => inner.BatchCommands;

    public override int Timeout
    {
        get => inner.Timeout;
        set => inner.Timeout = value;
    }

    protected override DbConnection DbConnection
    {
        get => connection;
        set
        {
            connection = value;
            inner.Connection = value is null ? null : LateCommitDbProvider.Unwrap(value);
        }
    }

    protected override DbTransaction DbTransaction
    {
        get => transaction;
        set
        {
            transaction = value;
            inner.Transaction = value is null ? null : LateCommitDbProvider.Unwrap(value);
        }
    }

    protected override DbBatchCommand CreateDbBatchCommand() => inner.CreateBatchCommand();

    public override int ExecuteNonQuery() => inner.ExecuteNonQuery();
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default) => inner.ExecuteNonQueryAsync(cancellationToken);
    public override object ExecuteScalar() => inner.ExecuteScalar();
    public override Task<object> ExecuteScalarAsync(CancellationToken cancellationToken = default) => inner.ExecuteScalarAsync(cancellationToken);
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => inner.ExecuteReader(behavior);

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        return inner.ExecuteReaderAsync(behavior, cancellationToken);
    }

    public override void Prepare() => inner.Prepare();
    public override Task PrepareAsync(CancellationToken cancellationToken = default) => inner.PrepareAsync(cancellationToken);
    public override void Cancel() => inner.Cancel();

    public override void Dispose() => inner.Dispose();

    public override ValueTask DisposeAsync() => inner.DisposeAsync();
}
