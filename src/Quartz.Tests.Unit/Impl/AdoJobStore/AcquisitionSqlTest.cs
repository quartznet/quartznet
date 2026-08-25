using System.Text;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The acquisition statement every shipped dialect produces when nothing is excluded, pinned as a
/// snapshot.
/// </summary>
/// <remarks>
/// The statement is assembled from a shared template, a per-dialect row-limiting splice and an
/// optional job-type exclusion clause. The text here is the one from before that assembly was
/// consolidated into a single template, so a change that is meant to leave the no-exclusion
/// statement alone has to prove it did — including the parts that look like whitespace and are not,
/// such as SQL Server's <c>Substring(6)</c> splice and MySQL's index hint, both of which are
/// positional and would break silently if the projection moved.
/// </remarks>
public class AcquisitionSqlTest
{
    private const int MaxCount = 5;

    [Test]
    public async Task EveryDialectBuildsTheAcquisitionStatementItAlwaysDidWhenNothingIsExcluded()
    {
        StringBuilder statements = new StringBuilder();

        foreach (IAcquisitionSql dialect in Dialects())
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

    private static IAcquisitionSql[] Dialects() =>
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
    /// What each dialect is asked for. The hook is protected, so reaching it means deriving — which
    /// is what a dialect author does anyway.
    /// </summary>
    private interface IAcquisitionSql
    {
        string NoExclusions(int maxCount);
    }

    private sealed class AcquisitionSqlStdAdoDelegate : StdAdoDelegate, IAcquisitionSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));
    }

    private sealed class AcquisitionSqlSqlServerDelegate : SqlServerDelegate, IAcquisitionSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));
    }

    private sealed class AcquisitionSqlPostgreSQLDelegate : PostgreSQLDelegate, IAcquisitionSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));
    }

    private sealed class AcquisitionSqlMySQLDelegate : MySQLDelegate, IAcquisitionSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));
    }

    private sealed class AcquisitionSqlSQLiteDelegate : SQLiteDelegate, IAcquisitionSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));
    }

    private sealed class AcquisitionSqlOracleDelegate : OracleDelegate, IAcquisitionSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));
    }

    private sealed class AcquisitionSqlFirebirdDelegate : FirebirdDelegate, IAcquisitionSql
    {
        public string NoExclusions(int maxCount) => GetSelectNextTriggerToAcquireSql(Shape(maxCount));
    }

    private static TriggerAcquisitionSqlShape Shape(int maxCount) =>
        new TriggerAcquisitionSqlShape(maxCount, ExcludedJobTypeBucket: 0);
}
