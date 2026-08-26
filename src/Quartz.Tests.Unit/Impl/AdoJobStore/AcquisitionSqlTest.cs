using System.Text;

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
        string sql = StdAdoConstants.BuildSqlSelectNextTriggerToAcquire(excludedJobTypeBucket: 4);

        // Placement is what makes "no per-dialect SQL change" true: every dialect splices its row
        // limit into the projection or around the whole statement, so a clause that landed after
        // ORDER BY - or inside the projection - would break one of them without breaking the rest.
        await Verify(sql, extension: "txt")
            .UseDirectory("../../Verify")
            .UseFileName("AcquisitionSqlTest_FourExclusions")
            .DisableRequireUniquePrefix();
    }

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

        string MisfireRecovery(int count);
    }

    private sealed class AcquisitionSqlStdAdoDelegate : StdAdoDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);
    }

    private sealed class AcquisitionSqlSqlServerDelegate : SqlServerDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);
    }

    private sealed class AcquisitionSqlPostgreSQLDelegate : PostgreSQLDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);
    }

    private sealed class AcquisitionSqlMySQLDelegate : MySQLDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);
    }

    private sealed class AcquisitionSqlSQLiteDelegate : SQLiteDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);
    }

    private sealed class AcquisitionSqlOracleDelegate : OracleDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);
    }

    private sealed class AcquisitionSqlFirebirdDelegate : FirebirdDelegate, IDialectSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));

        public string MisfireRecovery(int count) => GetSelectMisfiredTriggersToRecoverSql(count);
    }

    private static TriggerAcquisitionSqlShape Shape(int maxCount) =>
        new TriggerAcquisitionSqlShape(maxCount, ExcludedJobTypeBucket: 0);
}
