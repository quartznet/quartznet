using System.Data;
using System.Data.Common;
using System.Globalization;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Benchmark;

/// <summary>
/// Round-trip latency for the real-database benchmarks, opted into with an environment variable.
/// </summary>
/// <remarks>
/// <para>
/// Every database benchmark here runs against a container over loopback, where a round trip is tens of
/// microseconds and a cut that removes one is invisible. A database across a network, or in another
/// availability zone, charges one to two milliseconds for each, and that is where a round trip saved
/// is worth measuring. <c>QUARTZ_BENCHMARK_ROUND_TRIP_MS</c> names the milliseconds to charge; unset or
/// zero, the benchmarks run as they always have, against the bare driver.
/// </para>
/// <para>
/// <b>What is charged.</b> One delay before every statement a command or a batch executes and before
/// every commit and rollback — the exchanges a client waits on. Opening a pooled connection, <c>BEGIN</c>,
/// which Npgsql sends with the first statement of the transaction, and the pool's reset on return are
/// not charged. A <see cref="DbBatch" /> is one delay however many statements it carries, which is the
/// point of batching and what the model has to say.
/// </para>
/// <para>
/// <b>It is a model.</b> The delay is <see cref="Task.Delay(TimeSpan)" />, whose floor on Windows is
/// about a millisecond, and it is paid in the client where a wire would pay it in flight; a database
/// does nothing in the meantime where a real one would be serving another node. The numbers rank the
/// round trips a path takes honestly; they do not reproduce any particular network.
/// </para>
/// </remarks>
internal static class RoundTripLatency
{
    public const string Variable = "QUARTZ_BENCHMARK_ROUND_TRIP_MS";

    /// <summary>The latency configured, or <see langword="null" /> for none.</summary>
    public static TimeSpan? Configured { get; } = Read();

    /// <summary>
    /// Points a persistent store at <paramref name="connectionString" /> through a provider that charges
    /// the configured latency, when one is configured. Call it after the <c>Use&lt;Database&gt;</c> call,
    /// which registers the delegate; this replaces only the connection provider.
    /// </summary>
    public static void Inject(IPersistentStoreBuilder store, string providerName, string connectionString)
    {
        if (Configured is { } latency)
        {
            store.UseConnectionProvider(_ => new DelayingDbProvider(new DbProvider(providerName, connectionString), latency));
        }
    }

    /// <summary>A line for a benchmark's output saying what, if anything, is being charged.</summary>
    public static string Describe()
    {
        return Configured is { } latency
            ? string.Create(CultureInfo.InvariantCulture, $"{Variable}={latency.TotalMilliseconds:F0}: every statement, batch and commit is charged {latency.TotalMilliseconds:F0} ms.")
            : $"{Variable} is not set: round trips cost what loopback costs.";
    }

    private static TimeSpan? Read()
    {
        string? value = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int milliseconds) || milliseconds < 0)
        {
            throw new InvalidOperationException($"{Variable} must be a whole number of milliseconds; it is '{value}'.");
        }

        return milliseconds == 0 ? null : TimeSpan.FromMilliseconds(milliseconds);
    }
}

/// <summary>
/// An <see cref="IDbProvider" /> over another, whose connections charge <see cref="RoundTripLatency" />.
/// </summary>
internal sealed class DelayingDbProvider : IDbProvider
{
    private readonly IDbProvider inner;
    private readonly TimeSpan latency;

    public DelayingDbProvider(IDbProvider inner, TimeSpan latency)
    {
        this.inner = inner;
        this.latency = latency;
    }

    public DbCommand CreateCommand() => new DelayingCommand(inner.CreateCommand(), latency);

    public DbConnection CreateConnection() => new DelayingConnection(inner.CreateConnection(), latency);

    public string ConnectionString => inner.ConnectionString;

    public DbMetadata Metadata => inner.Metadata;

    public void Shutdown() => inner.Shutdown();
}

/// <inheritdoc cref="DelayingDbProvider" />
internal sealed class DelayingConnection : DbConnection
{
    private readonly TimeSpan latency;

    public DelayingConnection(DbConnection inner, TimeSpan latency)
    {
        Inner = inner;
        this.latency = latency;
    }

    /// <summary>The driver's connection, which the commands and batches made here execute against.</summary>
    public DbConnection Inner { get; }

    [System.Diagnostics.CodeAnalysis.AllowNull]
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

    public override void ChangeDatabase(string databaseName) => Inner.ChangeDatabase(databaseName);
    public override Task ChangeDatabaseAsync(string databaseName, CancellationToken cancellationToken = default) => Inner.ChangeDatabaseAsync(databaseName, cancellationToken);
    public override void Open() => Inner.Open();
    public override Task OpenAsync(CancellationToken cancellationToken) => Inner.OpenAsync(cancellationToken);
    public override void Close() => Inner.Close();
    public override Task CloseAsync() => Inner.CloseAsync();
    public override void EnlistTransaction(System.Transactions.Transaction? transaction) => Inner.EnlistTransaction(transaction);
    public override DataTable GetSchema() => Inner.GetSchema();
    public override DataTable GetSchema(string collectionName) => Inner.GetSchema(collectionName);
    public override DataTable GetSchema(string collectionName, string?[] restrictionValues) => Inner.GetSchema(collectionName, restrictionValues);

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        return new DelayingTransaction(Inner.BeginTransaction(isolationLevel), this, latency);
    }

    protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
    {
        DbTransaction transaction = await Inner.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        return new DelayingTransaction(transaction, this, latency);
    }

    protected override DbCommand CreateDbCommand()
    {
        DelayingCommand command = new(Inner.CreateCommand(), latency);
        command.Connection = this;
        return command;
    }

    protected override DbBatch CreateDbBatch() => new DelayingBatch(Inner.CreateBatch(), latency);

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
        await Inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>What a command, batch or transaction made here unwraps a connection it is handed to.</summary>
    public static DbConnection? Unwrap(DbConnection? connection) => connection is DelayingConnection delaying ? delaying.Inner : connection;
}

/// <inheritdoc cref="DelayingDbProvider" />
internal sealed class DelayingCommand : DbCommand
{
    private readonly DbCommand inner;
    private readonly TimeSpan latency;
    private DbConnection? connection;
    private DbTransaction? transaction;

    public DelayingCommand(DbCommand inner, TimeSpan latency)
    {
        this.inner = inner;
        this.latency = latency;
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
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
            inner.Connection = DelayingConnection.Unwrap(value);
        }
    }

    protected override DbTransaction? DbTransaction
    {
        get => transaction;
        set
        {
            transaction = value;
            inner.Transaction = DelayingTransaction.Unwrap(value);
        }
    }

    protected override DbParameterCollection DbParameterCollection => inner.Parameters;

    public override void Cancel() => inner.Cancel();
    public override void Prepare() => inner.Prepare();
    public override Task PrepareAsync(CancellationToken cancellationToken = default) => inner.PrepareAsync(cancellationToken);
    protected override DbParameter CreateDbParameter() => inner.CreateParameter();

    public override int ExecuteNonQuery()
    {
        Thread.Sleep(latency);
        return inner.ExecuteNonQuery();
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(latency, cancellationToken).ConfigureAwait(false);
        return await inner.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public override object? ExecuteScalar()
    {
        Thread.Sleep(latency);
        return inner.ExecuteScalar();
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(latency, cancellationToken).ConfigureAwait(false);
        return await inner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        Thread.Sleep(latency);
        return inner.ExecuteReader(behavior);
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        await Task.Delay(latency, cancellationToken).ConfigureAwait(false);
        return await inner.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
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

/// <inheritdoc cref="DelayingDbProvider" />
internal sealed class DelayingBatch : DbBatch
{
    private readonly DbBatch inner;
    private readonly TimeSpan latency;
    private DbConnection? connection;
    private DbTransaction? transaction;

    public DelayingBatch(DbBatch inner, TimeSpan latency)
    {
        this.inner = inner;
        this.latency = latency;
    }

    protected override DbBatchCommandCollection DbBatchCommands => inner.BatchCommands;

    public override int Timeout
    {
        get => inner.Timeout;
        set => inner.Timeout = value;
    }

    protected override DbConnection? DbConnection
    {
        get => connection;
        set
        {
            connection = value;
            inner.Connection = DelayingConnection.Unwrap(value);
        }
    }

    protected override DbTransaction? DbTransaction
    {
        get => transaction;
        set
        {
            transaction = value;
            inner.Transaction = DelayingTransaction.Unwrap(value);
        }
    }

    protected override DbBatchCommand CreateDbBatchCommand() => inner.CreateBatchCommand();

    public override void Cancel() => inner.Cancel();
    public override void Prepare() => inner.Prepare();
    public override Task PrepareAsync(CancellationToken cancellationToken = default) => inner.PrepareAsync(cancellationToken);

    public override int ExecuteNonQuery()
    {
        Thread.Sleep(latency);
        return inner.ExecuteNonQuery();
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(latency, cancellationToken).ConfigureAwait(false);
        return await inner.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public override object? ExecuteScalar()
    {
        Thread.Sleep(latency);
        return inner.ExecuteScalar();
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(latency, cancellationToken).ConfigureAwait(false);
        return await inner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        Thread.Sleep(latency);
        return inner.ExecuteReader(behavior);
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        await Task.Delay(latency, cancellationToken).ConfigureAwait(false);
        return await inner.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        inner.Dispose();
        base.Dispose();
    }
}

/// <inheritdoc cref="DelayingDbProvider" />
internal sealed class DelayingTransaction : DbTransaction
{
    private readonly DelayingConnection connection;
    private readonly TimeSpan latency;

    public DelayingTransaction(DbTransaction inner, DelayingConnection connection, TimeSpan latency)
    {
        Inner = inner;
        this.connection = connection;
        this.latency = latency;
    }

    public DbTransaction Inner { get; }

    protected override DbConnection? DbConnection => connection;
    public override IsolationLevel IsolationLevel => Inner.IsolationLevel;
    public override bool SupportsSavepoints => Inner.SupportsSavepoints;

    public override void Commit()
    {
        Thread.Sleep(latency);
        Inner.Commit();
    }

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(latency, cancellationToken).ConfigureAwait(false);
        await Inner.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Rollback()
    {
        Thread.Sleep(latency);
        Inner.Rollback();
    }

    public override async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(latency, cancellationToken).ConfigureAwait(false);
        await Inner.RollbackAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Save(string savepointName) => Inner.Save(savepointName);
    public override Task SaveAsync(string savepointName, CancellationToken cancellationToken = default) => Inner.SaveAsync(savepointName, cancellationToken);
    public override void Rollback(string savepointName) => Inner.Rollback(savepointName);
    public override Task RollbackAsync(string savepointName, CancellationToken cancellationToken = default) => Inner.RollbackAsync(savepointName, cancellationToken);
    public override void Release(string savepointName) => Inner.Release(savepointName);
    public override Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default) => Inner.ReleaseAsync(savepointName, cancellationToken);

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
        await Inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>What a command or batch made here unwraps a transaction it is handed to.</summary>
    public static DbTransaction? Unwrap(DbTransaction? transaction) => transaction is DelayingTransaction delaying ? delaying.Inner : transaction;
}
