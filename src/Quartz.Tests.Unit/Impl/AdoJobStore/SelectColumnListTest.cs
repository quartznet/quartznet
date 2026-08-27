using System.Data.Common;

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Every statement that used to read <c>SELECT *</c> now names its columns, and each named list is
/// exactly what the code reading the rows asks for.
/// </summary>
/// <remarks>
/// <para>
/// Both halves matter. A list missing a column the reader wants is a runtime failure on a database
/// and nowhere else; a list carrying a column no reader wants is bytes off the wire for nobody. So
/// each case drives the real reader against a <see cref="ProjectionDataReader" /> built from the
/// statement's own projection: an unprojected column throws, and an unread column is left over.
/// </para>
/// <para>
/// The projection is taken from the statement text rather than from the constant it was built out
/// of, so what is measured is what the database would be sent.
/// </para>
/// </remarks>
public class SelectColumnListTest
{
    [Test]
    public void CronTriggerStatementsNameWhatTheirDelegateReads()
    {
        CronTriggerPersistenceDelegate persistence = new();
        persistence.Initialize(PersistenceContext());

        ReadsExactly(
            StdAdoConstants.SqlSelectCronTriggers,
            reader => persistence.ReadTriggerPropertyBundle(reader));

        ReadsExactly(
            StdAdoConstants.SqlSelectCronTriggersByKeysPrefix,
            reader =>
            {
                persistence.ReadTriggerPropertyBundle(reader);
                ReadBatchKey(reader);
            });
    }

    [Test]
    public void SimpleTriggerStatementsNameWhatTheirDelegateReads()
    {
        SimpleTriggerPersistenceDelegate persistence = new();
        persistence.Initialize(PersistenceContext());

        ReadsExactly(
            StdAdoConstants.SqlSelectSimpleTrigger,
            reader => persistence.ReadTriggerPropertyBundle(reader));

        ReadsExactly(
            StdAdoConstants.SqlSelectSimpleTriggersByKeysPrefix,
            reader =>
            {
                persistence.ReadTriggerPropertyBundle(reader);
                ReadBatchKey(reader);
            });
    }

    [Test]
    public void TheSimplePropertiesStatementNamesWhatTheBaseDelegateReads()
    {
        // Every simple-properties type shares one table and one row reader, so the delegate asked here
        // stands for all of them.
        CalendarIntervalTriggerPersistenceDelegate persistence = new();
        persistence.Initialize(PersistenceContext());

        ReadsExactly(
            StdAdoConstants.SqlSelectSimpropTriggersByKeysPrefix,
            reader =>
            {
                persistence.ReadTriggerPropertyBundle(reader);
                ReadBatchKey(reader);
            });
    }

    [Test]
    public async Task TheSchedulerStateStatementsNameWhatTheRecordIsMadeOf()
    {
        // One statement per shape of the same read: by instance, and every instance of the scheduler.
        await ReadsExactly(StdAdoConstants.SqlSelectSchedulerState,
            (del, conn) => del.SelectSchedulerStateRecords(conn, "node").AsTask());

        await ReadsExactly(StdAdoConstants.SqlSelectSchedulerStates,
            (del, conn) => del.SelectSchedulerStateRecords(conn, instanceId: null).AsTask());
    }

    [Test]
    public async Task TheFiredTriggerStatementNamesWhatTheRecordIsMadeOf()
    {
        await ReadsExactly(StdAdoConstants.SqlSelectFiredTriggers,
            (del, conn) => del.SelectFiredTriggerRecords(conn, new FiredTriggerQuery()).AsTask());
    }

    /// <summary>
    /// The set form of the stored trigger header, which pause and resume decide a whole set's
    /// transitions from. Its projection is the single-trigger header's with the key columns in front,
    /// because a set read has to say which row each header came from.
    /// </summary>
    [Test]
    public async Task TheBatchTriggerHeaderStatementNamesWhatTheHeaderIsMadeOf()
    {
        await ReadsExactly(StdAdoConstants.SqlSelectTriggerHeadersByKeysPrefix,
            (del, conn) => del.SelectStoredTriggerHeaders(conn, [new TriggerKey("trigger", "group")]).AsTask(),
            statementIsAPrefix: true);
    }

    [Test]
    public async Task TheRecoverableFiredTriggerStatementNamesWhatTheRecoveryTriggerIsMadeOf()
    {
        await ReadsExactly(StdAdoConstants.SqlSelectInstancesRecoverableFiredTriggers,
            (del, conn) => del.SelectTriggersForRecoveringJobs(conn).AsTask(),
            // Each recovered trigger's job data map is read by a second statement, which this test
            // does not measure; an empty result leaves the map empty.
            trailingReaders: 1);
    }

    /// <summary>
    /// The two key columns a batch lookup projects on top of the row's own, read the way the batch
    /// loops read them.
    /// </summary>
    private static void ReadBatchKey(DbDataReader reader)
    {
        _ = new TriggerKey(
            (string) reader[AdoConstants.ColumnTriggerName],
            (string) reader[AdoConstants.ColumnTriggerGroup]);
    }

    /// <summary>
    /// Drives a reader function against the statement's own projection and asserts the two agree.
    /// </summary>
    private static void ReadsExactly(string sql, Action<DbDataReader> read)
    {
        ProjectionDataReader reader = new(ProjectionOf(sql));

        reader.Read();
        read(reader);

        reader.Unread.Should().BeEmpty(
            "'{0}' projects columns nothing reads, which is bytes off the wire for nobody", Projection(sql));
    }

    /// <summary>
    /// The same, for a statement whose reader lives inside <see cref="StdAdoDelegate" /> and is only
    /// reachable by issuing it.
    /// </summary>
    private static async Task ReadsExactly(
        string sql,
        Func<StdAdoDelegate, ConnectionAndTransactionHolder, Task> issue,
        int trailingReaders = 0,
        bool statementIsAPrefix = false)
    {
        ProjectionDataReader reader = new(ProjectionOf(sql));

        ReaderStubDelegate del = ReaderStubDelegate.Create();
        del.Enqueue(reader);
        for (int i = 0; i < trailingReaders; i++)
        {
            del.Enqueue(ProjectionDataReader.Empty);
        }

        using ConnectionAndTransactionHolder conn = new(new StubBatchingConnection(), transaction: null);
        await issue(del, conn);

        string expected = AdoJobStoreUtil.ReplaceTablePrefix(sql, "QRTZ_");
        if (statementIsAPrefix)
        {
            // A key-set statement is this prefix plus the predicate the caller appends, so what is
            // measured is that the projection reached the database unaltered.
            del.Statements[0].Should().StartWith(expected,
                "the statement measured has to begin with the statement issued");
        }
        else
        {
            del.Statements[0].Should().Be(expected,
                "the statement measured has to be the statement issued");
        }

        reader.Unread.Should().BeEmpty(
            "'{0}' projects columns nothing reads, which is bytes off the wire for nobody", Projection(sql));
    }

    /// <summary>
    /// The column names a statement projects. Taken from the text, because that is what the database
    /// is sent.
    /// </summary>
    private static string[] ProjectionOf(string sql) =>
        [.. Projection(sql).Split(',').Select(column => column.Trim())];

    private static string Projection(string sql)
    {
        const string Select = "SELECT ";
        const string From = " FROM ";

        int start = sql.IndexOf(Select, StringComparison.Ordinal);
        int end = sql.IndexOf(From, StringComparison.Ordinal);
        start.Should().Be(0, "these statements start with their projection");
        end.Should().BePositive("a projection is followed by a FROM clause");

        return sql[Select.Length..end];
    }

    private static TriggerPersistenceDelegateContext PersistenceContext() => new()
    {
        TablePrefix = "QRTZ_",
        SchedulerName = "INSTANCE",
        DbAccessor = ReaderStubDelegate.Create()
    };

    /// <summary>
    /// A delegate that answers each statement it is asked to prepare with the next queued reader,
    /// which is how a reader that lives inside the delegate can be driven without a database.
    /// </summary>
    private sealed class ReaderStubDelegate : StdAdoDelegate
    {
        private readonly Queue<ProjectionDataReader> readers = new();

        public static ReaderStubDelegate Create()
        {
            IDbProvider dbProvider = A.Fake<IDbProvider>();
            A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata { ParameterNamePrefix = "@", BindByName = true });

            ReaderStubDelegate del = new();
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

        public List<string> Statements { get; } = [];

        public void Enqueue(ProjectionDataReader reader) => readers.Enqueue(reader);

        public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            Statements.Add(commandText);
            ReaderStubCommand cmd = new(readers.Count > 0 ? readers.Dequeue() : ProjectionDataReader.Empty)
            {
                CommandText = commandText
            };
            cth.Attach(cmd);
            return cmd;
        }
    }
}
