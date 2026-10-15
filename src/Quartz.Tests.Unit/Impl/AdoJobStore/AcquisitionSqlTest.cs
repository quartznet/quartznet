using System.Text;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The two row-limited statements every shipped dialect produces, pinned as snapshots.
/// </summary>
/// <remarks>
/// Each is assembled from a shared template, a per-dialect row-limiting clause and — for acquisition
/// — an optional job-type exclusion clause. The text here is the one from before that assembly was
/// consolidated, so a change that is meant to leave a statement alone has to prove it did —
/// including the parts that look like whitespace and are not, such as SQL Server's row-limit splice
/// and MySQL's index hint, both of which used to be positional and would have broken silently if the
/// projection moved.
/// </remarks>
public class AcquisitionSqlTest
{
    private const int MaxCount = 5;

    /// <summary>
    /// A misfire recovery batch size, and the sentinel that asks for an unlimited one. Both are
    /// snapshotted, because the row limit is what tells the two apart.
    /// </summary>
    private const int MisfireCount = 20;

    private const int Unlimited = -1;

    [Test]
    public async Task EveryDialectBuildsTheAcquisitionStatementItAlwaysDidWhenNothingIsExcluded()
    {
        StringBuilder statements = new StringBuilder();

        foreach (IDialectSql dialect in Dialects())
        {
            statements.Append("=== ").Append(dialect.GetType().BaseType!.Name).AppendLine(" ===");
            statements.AppendLine(dialect.NoExclusions(MaxCount));
            statements.AppendLine();
        }

        await Verify(statements.ToString(), extension: "txt")
            .UseDirectory("../../Verify")
            .UseFileName("AcquisitionSqlTest_NoExclusions")
            .DisableRequireUniquePrefix();
    }

    [Test]
    public async Task EveryDialectBuildsTheMisfireRecoveryStatementItAlwaysDid()
    {
        StringBuilder statements = new StringBuilder();

        foreach (IDialectSql dialect in Dialects())
        {
            string name = dialect.GetType().BaseType!.Name;

            statements.Append("=== ").Append(name).AppendLine(" ===");
            statements.AppendLine(dialect.MisfireRecovery(MisfireCount));
            statements.AppendLine();

            statements.Append("=== ").Append(name).AppendLine(" (unlimited) ===");
            statements.AppendLine(dialect.MisfireRecovery(Unlimited));
            statements.AppendLine();
        }

        await Verify(statements.ToString(), extension: "txt")
            .UseDirectory("../../Verify")
            .UseFileName("AcquisitionSqlTest_MisfireRecovery")
            .DisableRequireUniquePrefix();
    }

    [Test]
    public async Task TheExclusionClauseSitsBetweenTheNodeFilterAndTheOrdering()
    {
        string sql = StdAdoConstants.BuildSqlSelectNextTriggerToAcquire(excludedJobTypeBucket: 4, SqlRowLimit.Unlimited);

        // Placement is what makes "no per-dialect SQL change" true: every dialect splices its row
        // limit into the projection or around the whole statement, so a clause that landed after
        // ORDER BY - or inside the projection - would break one of them without breaking the rest.
        await Verify(sql, extension: "txt")
            .UseDirectory("../../Verify")
            .UseFileName("AcquisitionSqlTest_FourExclusions")
            .DisableRequireUniquePrefix();
    }

    /// <summary>
    /// Every shipped dialect says it keeps the excluded job types out itself, and the store takes that
    /// at its word — so a dialect whose statement lost the clause would silently run what a deployment
    /// excluded. The declaration and the statement are checked together for exactly that reason.
    /// </summary>
    [Test]
    public void EveryShippedDialectDeclaresTheExclusionFilterItsStatementActuallyCarries()
    {
        foreach (IDialectSql dialect in Dialects())
        {
            string name = dialect.GetType().BaseType!.Name;

            ((IDriverDelegate) dialect).FiltersAcquisitionJobTypeExclusions.Should().BeTrue(
                $"{name} builds its acquisition statement from the one that carries the NOT IN clause");

            dialect.WithExclusions(MaxCount, bucket: 2).Should()
                .Contain("@" + StdAdoConstants.ExcludedJobTypeParameter(0), $"{name} must bind the first excluded name")
                .And.Contain("@" + StdAdoConstants.ExcludedJobTypeParameter(1),
                    $"{name} declares that it filters, which is only true while its statement carries a term per excluded name");
        }
    }

    /// <summary>
    /// The six shapes the execution-group routing clause takes (#4069), per dialect: what a node that
    /// serves or refuses named groups, the ungrouped bucket, or both, asks each database for.
    /// </summary>
    /// <remarks>
    /// The <c>IS NULL</c> term on a refuse-list is the one worth a snapshot of its own: <c>NULL NOT IN
    /// (…)</c> is unknown, and a refuse-list spelled without it would drop every ungrouped row.
    /// </remarks>
    [Test]
    public async Task EveryDialectBuildsTheSixRoutedAcquisitionStatements()
    {
        StringBuilder statements = new StringBuilder();

        foreach (IDialectSql dialect in Dialects())
        {
            string name = dialect.GetType().BaseType!.Name;
            foreach ((string label, ExecutionGroupClauseShape shape) in RoutingShapes())
            {
                statements.Append("=== ").Append(name).Append(" (").Append(label).AppendLine(") ===");
                statements.AppendLine(dialect.Routed(MaxCount, shape));
                statements.AppendLine();
            }
        }

        await Verify(statements.ToString(), extension: "txt")
            .UseDirectory("../../Verify")
            .UseFileName("AcquisitionSqlTest_ExecutionGroupRouting")
            .DisableRequireUniquePrefix();
    }

    [Test]
    public void TheRoutingClauseSitsBetweenTheExclusionClauseAndTheOrdering()
    {
        string sql = StdAdoConstants.BuildSqlSelectNextTriggerToAcquire(
            excludedJobTypeBucket: 4,
            new ExecutionGroupClauseShape(IsServeList: true, IncludesUngrouped: true, NamedCount: 2),
            SqlRowLimit.Unlimited);

        int lastExclusion = sql.IndexOf("@excludedJobType0003", StringComparison.Ordinal);
        int firstRouted = sql.IndexOf("@servedGroup0000", StringComparison.Ordinal);
        int orderBy = sql.IndexOf("ORDER BY", StringComparison.Ordinal);

        lastExclusion.Should().BePositive();
        firstRouted.Should().BeGreaterThan(lastExclusion,
            "the routing parameters are bound after the exclusion parameters, so the clause has to follow them for providers that bind positionally");
        orderBy.Should().BeGreaterThan(firstRouted,
            "every dialect splices its row limit into the projection or around the statement, so a clause after ORDER BY would break one of them");
    }

    /// <summary>
    /// The same promise as the exclusion one: every shipped dialect says it keeps the unserved groups
    /// out itself, so each statement has to carry a term per routed group.
    /// </summary>
    [Test]
    public void EveryShippedDialectDeclaresTheRoutingFilterItsStatementActuallyCarries()
    {
        ExecutionGroupClauseShape shape = new(IsServeList: true, IncludesUngrouped: false, NamedCount: 2);

        foreach (IDialectSql dialect in Dialects())
        {
            string name = dialect.GetType().BaseType!.Name;

            ((IDriverDelegate) dialect).FiltersAcquisitionExecutionGroups.Should().BeTrue(
                $"{name} builds its acquisition statement from the one that carries the routing clause");

            dialect.Routed(MaxCount, shape).Should()
                .Contain("@" + StdAdoConstants.ExecutionGroupParameter(isServeList: true, 0), $"{name} must bind the first routed group")
                .And.Contain("@" + StdAdoConstants.ExecutionGroupParameter(isServeList: true, 1),
                    $"{name} declares that it filters, which is only true while its statement carries a term per routed group");
        }
    }

    /// <summary>
    /// Acquisition, the sweep's count, the sweep's read and the pinned-trigger probe carry one predicate,
    /// textually: a node whose sweep and acquisition disagreed about which rows are its own would re-arm
    /// what it never takes, or take what it never re-arms.
    /// </summary>
    [Test]
    public void TheFourStatementsCarryTheSamePredicatePerShape()
    {
        foreach ((string label, ExecutionGroupClauseShape shape) in RoutingShapes())
        {
            string aliased = StdAdoConstants.ExecutionGroupPredicate(shape, alias: "t.");
            string bare = StdAdoConstants.ExecutionGroupPredicate(shape, alias: "");

            StdAdoConstants.BuildSqlSelectNextTriggerToAcquire(0, shape, SqlRowLimit.Unlimited).Should().Contain(aliased, label);
            StdAdoConstants.BuildSqlSelectMisfiredTriggersToRecover(SqlRowLimit.Unlimited, shape).Should().Contain(aliased, label);
            StdAdoConstants.BuildSqlSelectPinnedTriggersBlockedElsewhere(shape).Should().Contain(aliased, label);
            StdAdoConstants.BuildSqlCountMisfiredTriggersInStates(shape).Should().Contain(bare, label);
        }
    }

    [Test]
    public void TheUnservedCountIsTheSweepsCountWithTheComplementaryClause()
    {
        ExecutionGroupClauseShape serve = new(IsServeList: true, IncludesUngrouped: true, NamedCount: 2);

        string sql = StdAdoConstants.BuildSqlCountUnservedMisfiredTriggers(serve);

        sql.Should().Contain("MIN(NEXT_FIRE_TIME)", "the oldest unserved misfire is what the log line names")
            .And.Contain(StdAdoConstants.ExecutionGroupPredicate(serve.Complement, alias: ""),
                "a serve-list's complement is a refuse-list over the same groups, bound under the other prefix")
            .And.Contain("@refusedGroup0000");
    }

    /// <summary>
    /// A node that routes builds its sweep statements from the context the store hands its delegate,
    /// and MySQL's index hint has to survive the splice.
    /// </summary>
    [Test]
    public void ARoutedDelegateSweepsOnlyWhatItServesAndMySqlKeepsItsHint()
    {
        AcquisitionSqlMySQLDelegate mysql = new();
        mysql.Route(served: [ExecutionGroupScope.Named("reports"), ExecutionGroupScope.Default]);
        ExecutionGroupClauseShape shape = new(IsServeList: true, IncludesUngrouped: true, NamedCount: 1);

        string recovery = mysql.MisfireRecovery(MisfireCount);
        string count = mysql.CountMisfired();

        recovery.Should().Contain("FORCE INDEX (IDX_{1}T_NFT_ST)", "the hint is on the aliased table and the clause is a residual on the rows the index returns")
            .And.Contain(StdAdoConstants.ExecutionGroupPredicate(shape, alias: "t."));
        count.Should().Contain("FORCE INDEX (IDX_{1}T_NFT_ST) WHERE")
            .And.Contain(StdAdoConstants.ExecutionGroupPredicate(shape, alias: ""));
    }

    [Test]
    public void ADelegateThatRoutesNothingSweepsEverythingAsItAlwaysDid()
    {
        AcquisitionSqlStdAdoDelegate plain = new();
        plain.Route(served: null);

        plain.MisfireRecovery(Unlimited).Should().Be(StdAdoConstants.SqlSelectMisfiredTriggersToRecover);
        plain.CountMisfired().Should().Be(StdAdoConstants.SqlCountMisfiredTriggersInStates);
    }

    private static (string Label, ExecutionGroupClauseShape Shape)[] RoutingShapes() =>
    [
        ("serve named", new ExecutionGroupClauseShape(IsServeList: true, IncludesUngrouped: false, NamedCount: 2)),
        ("serve named and ungrouped", new ExecutionGroupClauseShape(IsServeList: true, IncludesUngrouped: true, NamedCount: 2)),
        ("serve ungrouped only", new ExecutionGroupClauseShape(IsServeList: true, IncludesUngrouped: true, NamedCount: 0)),
        ("refuse named", new ExecutionGroupClauseShape(IsServeList: false, IncludesUngrouped: false, NamedCount: 2)),
        ("refuse named and ungrouped", new ExecutionGroupClauseShape(IsServeList: false, IncludesUngrouped: true, NamedCount: 2)),
        ("refuse ungrouped only", new ExecutionGroupClauseShape(IsServeList: false, IncludesUngrouped: true, NamedCount: 0)),
    ];

    private static IDialectSql[] Dialects() =>
    [
        new AcquisitionSqlStdAdoDelegate(),
        new AcquisitionSqlSqlServerDelegate(),
        new AcquisitionSqlPostgreSQLDelegate(),
        new AcquisitionSqlMySQLDelegate(),
        new AcquisitionSqlSQLiteDelegate(),
        new AcquisitionSqlOracleDelegate(),
        new AcquisitionSqlFirebirdDelegate()
    ];

    /// <summary>
    /// What each dialect is asked for. The hooks are protected, so reaching them means deriving —
    /// which is what a dialect author does anyway.
    /// </summary>
    private interface IDialectSql
    {
        string NoExclusions(int maxCount);

        string WithExclusions(int maxCount, int bucket);

        string Routed(int maxCount, ExecutionGroupClauseShape executionGroups);

        string MisfireRecovery(int count);

        string CountMisfired();

        /// <summary>
        /// Initializes the delegate as a store that serves <paramref name="served" /> would, which is
        /// what its sweep statements are built from.
        /// </summary>
        void Route(IReadOnlyCollection<ExecutionGroupScope> served);
    }

    private static DriverDelegateContext RoutedContext(IReadOnlyCollection<ExecutionGroupScope> served) => new()
    {
        TablePrefix = AdoConstants.DefaultTablePrefix,
        SchedulerName = "AcquisitionSqlTest",
        InstanceId = "node",
        DbProvider = new StubDbProvider(),
        TypeLoader = new SimpleTypeLoader(),
        ServedExecutionGroups = served,
    };

    private sealed class AcquisitionSqlStdAdoDelegate : StdAdoDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string WithExclusions(int maxCount, int bucket) => GetSelectNextTriggerToAcquireSql(new TriggerAcquisitionSqlShape(maxCount, bucket));

        public string Routed(int maxCount, ExecutionGroupClauseShape executionGroups) => GetSelectNextTriggerToAcquireSql(Shape(maxCount) with { ExecutionGroups = executionGroups });

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);

        public string CountMisfired() => GetCountMisfiredTriggersInStateSql();

        public void Route(IReadOnlyCollection<ExecutionGroupScope> served) => Initialize(RoutedContext(served));
    }

    private sealed class AcquisitionSqlSqlServerDelegate : SqlServerDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string WithExclusions(int maxCount, int bucket) => GetSelectNextTriggerToAcquireSql(new TriggerAcquisitionSqlShape(maxCount, bucket));

        public string Routed(int maxCount, ExecutionGroupClauseShape executionGroups) => GetSelectNextTriggerToAcquireSql(Shape(maxCount) with { ExecutionGroups = executionGroups });

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);

        public string CountMisfired() => GetCountMisfiredTriggersInStateSql();

        public void Route(IReadOnlyCollection<ExecutionGroupScope> served) => Initialize(RoutedContext(served));
    }

    private sealed class AcquisitionSqlPostgreSQLDelegate : PostgreSQLDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string WithExclusions(int maxCount, int bucket) => GetSelectNextTriggerToAcquireSql(new TriggerAcquisitionSqlShape(maxCount, bucket));

        public string Routed(int maxCount, ExecutionGroupClauseShape executionGroups) => GetSelectNextTriggerToAcquireSql(Shape(maxCount) with { ExecutionGroups = executionGroups });

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);

        public string CountMisfired() => GetCountMisfiredTriggersInStateSql();

        public void Route(IReadOnlyCollection<ExecutionGroupScope> served) => Initialize(RoutedContext(served));
    }

    private sealed class AcquisitionSqlMySQLDelegate : MySQLDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string WithExclusions(int maxCount, int bucket) => GetSelectNextTriggerToAcquireSql(new TriggerAcquisitionSqlShape(maxCount, bucket));

        public string Routed(int maxCount, ExecutionGroupClauseShape executionGroups) => GetSelectNextTriggerToAcquireSql(Shape(maxCount) with { ExecutionGroups = executionGroups });

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);

        public string CountMisfired() => GetCountMisfiredTriggersInStateSql();

        public void Route(IReadOnlyCollection<ExecutionGroupScope> served) => Initialize(RoutedContext(served));
    }

    private sealed class AcquisitionSqlSQLiteDelegate : SQLiteDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string WithExclusions(int maxCount, int bucket) => GetSelectNextTriggerToAcquireSql(new TriggerAcquisitionSqlShape(maxCount, bucket));

        public string Routed(int maxCount, ExecutionGroupClauseShape executionGroups) => GetSelectNextTriggerToAcquireSql(Shape(maxCount) with { ExecutionGroups = executionGroups });

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);

        public string CountMisfired() => GetCountMisfiredTriggersInStateSql();

        public void Route(IReadOnlyCollection<ExecutionGroupScope> served) => Initialize(RoutedContext(served));
    }

    private sealed class AcquisitionSqlOracleDelegate : OracleDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string WithExclusions(int maxCount, int bucket) => GetSelectNextTriggerToAcquireSql(new TriggerAcquisitionSqlShape(maxCount, bucket));

        public string Routed(int maxCount, ExecutionGroupClauseShape executionGroups) => GetSelectNextTriggerToAcquireSql(Shape(maxCount) with { ExecutionGroups = executionGroups });

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);

        public string CountMisfired() => GetCountMisfiredTriggersInStateSql();

        public void Route(IReadOnlyCollection<ExecutionGroupScope> served) => Initialize(RoutedContext(served));
    }

    private sealed class AcquisitionSqlFirebirdDelegate : FirebirdDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string WithExclusions(int maxCount, int bucket) => GetSelectNextTriggerToAcquireSql(new TriggerAcquisitionSqlShape(maxCount, bucket));

        public string Routed(int maxCount, ExecutionGroupClauseShape executionGroups) => GetSelectNextTriggerToAcquireSql(Shape(maxCount) with { ExecutionGroups = executionGroups });

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);

        public string CountMisfired() => GetCountMisfiredTriggersInStateSql();

        public void Route(IReadOnlyCollection<ExecutionGroupScope> served) => Initialize(RoutedContext(served));
    }

    private static TriggerAcquisitionSqlShape Shape(int maxCount) =>
        new TriggerAcquisitionSqlShape(maxCount, ExcludedJobTypeBucket: 0);
}
