using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Tests;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The cluster-wide lock itself, under contention, against a real database. Everything a clustered
/// store serializes it serializes through this one row, and until now the only coverage the row-lock
/// handlers had was retry behaviour in front of a faked provider — which proves the handler tries
/// again, and nothing at all about whether the statement it issues excludes anybody.
/// </summary>
/// <remarks>
/// <para>
/// The handler under test is not constructed here. It is read off a real store that has been
/// initialized with clustering on, so this fixture exercises whichever handler and whichever statement
/// the store picks for the dialect — <c>SELECT … FOR UPDATE</c> on PostgreSQL, <c>(UPDLOCK,ROWLOCK)</c>
/// on SQL Server — rather than a second copy of that decision that could drift from it.
/// </para>
/// <para>
/// Exclusion here belongs to the transaction, not to the handler: <c>ReleaseLock</c> only forgets the
/// in-process record of ownership, and the row stays locked until the transaction commits. So each
/// contender below leaves the critical section, releases, and only then commits — which is the order
/// the job store uses, and the order that makes "one holder at a time" mean anything.
/// </para>
/// <para>
/// The <c>TRIGGER_ACCESS</c> row exists before any contender starts. The other race — two nodes
/// reaching for a lock row that does not exist yet, both falling through to the <c>INSERT</c> — is what
/// <see cref="PostgreSQLLockTest" /> covers and what
/// <see cref="Quartz.Impl.AdoJobStore.PostgreSqlSelectForUpdateLockHandler" /> exists for; this fixture
/// is about the steady state, where the row is there and two nodes want it.
/// </para>
/// </remarks>
public abstract class DbLockHandlerContentionTestBase
{
    /// <summary>
    /// How many callers reach for the lock at once, spread over the two nodes. Enough that a handler
    /// which excluded nobody would have several of them inside at the same instant, because they are
    /// all released from the same gate.
    /// </summary>
    private const int Contenders = 8;

    /// <summary>
    /// How long the holder in <see cref="ANodeBlocksWhileAnotherHoldsTheRow" /> keeps the row before
    /// committing. An absence cannot be polled for, only waited out — and the wait is given teeth by
    /// the assertion that follows it, which requires the handover after the commit to take less time
    /// than this window did.
    /// </summary>
    private static readonly TimeSpan HoldWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// When an awaited handover is reported as a hang instead of hanging the run. Not a timing
    /// assertion.
    /// </summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(60);

    protected DbLockHandlerContentionTestBase(string provider)
    {
        Database = ClusteredTestDatabase.For(provider);
    }

    protected ClusteredTestDatabase Database { get; }

    /// <summary>
    /// The scheduler name whose lock row this fixture contends for. Derived from the fixture's own type
    /// name, because the lock statement filters by it and two dialect fixtures sharing a name in one
    /// database would be contending with each other.
    /// </summary>
    private string SchedulerName => "DbLockContention_" + GetType().Name;

    /// <summary>
    /// The handler this dialect's store picks for itself, named by the subclass so that each leg says
    /// which statement it is the coverage for. Asserted rather than assumed: a store that quietly
    /// started handing out a different handler would otherwise leave this fixture testing something
    /// else under the same name.
    /// </summary>
    protected abstract Type ExpectedLockHandler { get; }

    [SetUp]
    public async Task GiveTheLockRowAKnownStartingPoint()
    {
        await ExecuteNonQuery("DELETE FROM QRTZ_LOCKS WHERE SCHED_NAME = @schedulerName", ("schedulerName", SchedulerName));
        await ExecuteNonQuery(
            "INSERT INTO QRTZ_LOCKS (SCHED_NAME, LOCK_NAME) VALUES (@schedulerName, @lockName)",
            ("schedulerName", SchedulerName),
            ("lockName", "TRIGGER_ACCESS"));
    }

    [TearDown]
    public Task RemoveTheLockRow()
    {
        return ExecuteNonQuery("DELETE FROM QRTZ_LOCKS WHERE SCHED_NAME = @schedulerName", ("schedulerName", SchedulerName));
    }

    /// <summary>
    /// Two nodes, one lock name, eight callers released together: every one is served, no two are
    /// inside at once, and the run finishes — which is the whole of what a clustered store asks of the
    /// row.
    /// </summary>
    [Test]
    public async Task TwoNodesContendingForOneLockAreServedOneAtATime()
    {
        ILockHandler nodeA = await NodeLockHandler("nodeA");
        ILockHandler nodeB = await NodeLockHandler("nodeB");

        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentBag<int> occupancyOnEntry = [];
        ConcurrentBag<string> order = [];
        int inside = 0;

        async Task Contend(int index)
        {
            ILockHandler lockHandler = index % 2 == 0 ? nodeA : nodeB;
            Guid requestorId = Guid.NewGuid();

            await using DbConnection connection = Database.CreateConnection();
            await connection.OpenAsync();
            await using DbTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            // Owns nothing: the transaction is committed below, by the caller, after the lock has been
            // given back — which is the sequence the row lock's lifetime actually depends on.
            using ConnectionAndTransactionHolder holder = new(connection, transaction, ownsResources: false);

            // Everyone opens a connection and begins a transaction first, so that what is being timed
            // when the gate opens is the reach for the row rather than the connection pool.
            await gate.Task;

            bool taken = await lockHandler.AcquireLock(requestorId, holder, SchedulerLock.TriggerAccess);
            taken.Should().BeTrue("a caller that does not hold the lock has no business proceeding");

            occupancyOnEntry.Add(Interlocked.Increment(ref inside));
            order.Add(index.ToString(CultureInfo.InvariantCulture));

            // A real round trip inside the section: a contender queued on the row in the server is
            // released the instant the holder commits, so any failure to exclude shows up as two of
            // them counted here rather than as a race this has to be lucky to catch.
            await ReadLockRowCount(holder);

            Interlocked.Decrement(ref inside);

            await lockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess);
            await transaction.CommitAsync();
        }

        Task all = Task.WhenAll(Enumerable.Range(0, Contenders).Select(index => Task.Run(() => Contend(index))));
        gate.SetResult();

        await all.WaitAsync(GiveUpAfter);

        order.Should().HaveCount(Contenders,
            "a caller that never reaches the row is a node that stops writing; waiting is fine, starving is not");
        occupancyOnEntry.Should().OnlyContain(count => count == 1,
            "the row is the only thing standing between two nodes writing the same trigger rows, so two "
            + "callers counted inside it at once is a clustered store with no mutual exclusion at all");

        // Nothing was left holding: a lock that survives its owner's commit blocks the cluster for good,
        // and the only way to see that is to ask for it again.
        ILockHandler after = await NodeLockHandler("nodeC");
        await using DbConnection connection = Database.CreateConnection();
        await connection.OpenAsync();
        await using DbTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        using ConnectionAndTransactionHolder holder = new(connection, transaction, ownsResources: false);

        Task<bool> free = after.AcquireLock(Guid.NewGuid(), holder, SchedulerLock.TriggerAccess).AsTask();
        (await free.WaitAsync(GiveUpAfter)).Should().BeTrue(
            "every contender committed, so the row is free — a hang here is the deadlock this whole "
            + "arrangement is watched for");

        await transaction.CommitAsync();

        (await CountRows("SELECT COUNT(*) FROM QRTZ_LOCKS WHERE SCHED_NAME = @schedulerName", ("schedulerName", SchedulerName)))
            .Should().Be(1, "eight contenders share one lock row; a handler that inserted one of its own per "
                            + "caller would be locking eight different rows and excluding nobody");
    }

    /// <summary>
    /// The exclusion seen from the waiting side: while one node holds the row, the other's request does
    /// not come back at all. It is the same property the occupancy count asserts, observed as a caller
    /// genuinely parked in the database rather than as a count that happened never to reach two.
    /// </summary>
    /// <remarks>
    /// The negative is waited out rather than polled for, and the assertion after it is what makes the
    /// wait mean something: once the holder commits, the handover has to take less time than the window
    /// in which it demonstrably did not happen. A window too short to have caught anything fails that
    /// comparison instead of passing quietly.
    /// </remarks>
    [Test]
    public async Task ANodeBlocksWhileAnotherHoldsTheRow()
    {
        ILockHandler holderNode = await NodeLockHandler("holder");
        ILockHandler waitingNode = await NodeLockHandler("waiter");

        await using DbConnection holderConnection = Database.CreateConnection();
        await holderConnection.OpenAsync();
        await using DbTransaction holderTransaction = await holderConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        using ConnectionAndTransactionHolder holderHolder = new(holderConnection, holderTransaction, ownsResources: false);

        (await holderNode.AcquireLock(Guid.NewGuid(), holderHolder, SchedulerLock.TriggerAccess)).Should().BeTrue();

        await using DbConnection waiterConnection = Database.CreateConnection();
        await waiterConnection.OpenAsync();
        await using DbTransaction waiterTransaction = await waiterConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        using ConnectionAndTransactionHolder waiterHolder = new(waiterConnection, waiterTransaction, ownsResources: false);

        Task<bool> queued = waitingNode.AcquireLock(Guid.NewGuid(), waiterHolder, SchedulerLock.TriggerAccess).AsTask();

        await Task.Delay(HoldWindow);

        queued.IsCompleted.Should().BeFalse(
            "the other node holds the row and has not committed, so this request is parked in the "
            + "database — a request that came back here is one the store would act on while a peer was "
            + "still writing");

        long handoverStarted = Stopwatch.GetTimestamp();
        await holderTransaction.CommitAsync();

        (await queued.WaitAsync(GiveUpAfter)).Should().BeTrue("committing releases the row to whoever was waiting on it");

        TimeSpan handover = Stopwatch.GetElapsedTime(handoverStarted);
        handover.Should().BeLessThan(HoldWindow,
            "the handover took less time than the window the request spent not completing, so that window "
            + "was long enough to have caught a lock that excluded nobody");

        await waiterTransaction.CommitAsync();
    }

    /// <summary>
    /// The lock handler a store of this dialect builds for itself, taken off a real store rather than
    /// constructed here. Each call builds a separate store, which is what makes two of them two nodes:
    /// a handler remembers in memory which requestors it has given the lock to, so one handler shared
    /// between the two would be answering out of that memory rather than out of the database.
    /// </summary>
    private async ValueTask<ILockHandler> NodeLockHandler(string instanceId)
    {
        IDbProvider dbProvider = new DbProvider(Database.Provider, Database.ConnectionString);

        AdoJobStoreDependencies dependencies = TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions(SchedulerName, instanceId),
            storeOptions: TestJobStores.StoreOptions("lock-contention"),
            clusteringOptions: TestJobStores.ClusteringOptions(options => options.Enabled = true),
            dbProvider: dbProvider,
            driverDelegate: Database.CreateDriverDelegate()) with
        {
            LockHandler = null,
        };

        LocalTransactionJobStore store = new(dependencies);
        await store.Initialize(new SchedulerIdentity { SchedulerName = SchedulerName, InstanceId = instanceId });

        store.LockHandler.Should().BeOfType(ExpectedLockHandler,
            "this fixture is the coverage for the statement that handler issues, so it has to be the one "
            + "a clustered store of this dialect actually ends up with");
        store.LockHandler.RequiresConnection.Should().BeTrue("a row lock is taken on the caller's own unit of work");

        return store.LockHandler;
    }

    /// <summary>
    /// One statement inside the critical section, on the contender's own connection and transaction, so
    /// that the section is a real unit of work rather than two adjacent method calls.
    /// </summary>
    private static async Task ReadLockRowCount(ConnectionAndTransactionHolder holder)
    {
        using DbCommand command = holder.Connection.CreateCommand();
        holder.Attach(command);
        command.CommandText = "SELECT COUNT(*) FROM QRTZ_LOCKS";
        await command.ExecuteScalarAsync();
    }

    private async Task<int> ExecuteNonQuery(string sql, params (string Name, object Value)[] parameters)
    {
        using DbConnection connection = Database.CreateConnection();
        await connection.OpenAsync();
        using DbCommand command = CreateCommand(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountRows(string sql, params (string Name, object Value)[] parameters)
    {
        using DbConnection connection = Database.CreateConnection();
        await connection.OpenAsync();
        using DbCommand command = CreateCommand(connection, sql, parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static DbCommand CreateCommand(DbConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        return command;
    }
}
