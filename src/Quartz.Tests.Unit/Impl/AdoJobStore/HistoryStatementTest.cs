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

using System.Reflection;
using System.Text.RegularExpressions;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The one thing about the history statements that is not dialect-neutral: joining two strings.
/// </summary>
/// <remarks>
/// A <c>Contains</c> filter matches <c>group.name</c>, so the statement has to build that. <c>||</c> is
/// ANSI and is what four of the six dialects take; on MySQL it is a logical OR unless the server runs
/// in <c>PIPES_AS_CONCAT</c>, where <c>LOWER(a || '.' || b)</c> silently becomes <c>LOWER(0)</c> and the
/// filter matches nothing. SQL Server rejects it outright. Neither failure is one a SQLite-backed test
/// can see, so the three forms are asserted here.
/// </remarks>
public sealed class HistoryStatementTest
{
    [Test]
    public void TheAnsiDialectsJoinWithDoublePipe()
    {
        new SQLiteDelegate().HistoryKeyExpression(AdoConstants.ColumnJobGroup, AdoConstants.ColumnJobName)
            .Should().Be("LOWER(JOB_GROUP || '.' || JOB_NAME)",
                "PostgreSQL, Oracle, SQLite and Firebird all take the ANSI concatenation operator");
    }

    [Test]
    public void SqlServerJoinsWithPlus()
    {
        new SqlServerDelegate().HistoryKeyExpression(AdoConstants.ColumnTriggerGroup, AdoConstants.ColumnTriggerName)
            .Should().Be("LOWER(TRIGGER_GROUP + '.' + TRIGGER_NAME)",
                "T-SQL reads || as a syntax error unless the connection asked for ANSI concatenation, "
                + "which nothing here does");
    }

    [Test]
    public void MySqlJoinsWithConcat()
    {
        new MySQLDelegate().HistoryKeyExpression(AdoConstants.ColumnJobGroup, AdoConstants.ColumnJobName)
            .Should().Be("LOWER(CONCAT(JOB_GROUP, '.', JOB_NAME))",
                "MySQL reads || as a logical OR, which would not fail — it would match nothing, which "
                + "is far worse");
    }

    /// <summary>
    /// The two feeds are two tables, and each statement names its own.
    /// </summary>
    /// <remarks>
    /// Every one of these is built from <c>AdoConstants</c>, so what this catches is a statement
    /// assembled from the wrong half of the pair — which against a real database is a misfire written
    /// into the executions, or a sweep that trims a feed nobody asked it to.
    /// </remarks>
    [Test]
    public void EachStatementNamesTheFeedItBelongsTo()
    {
        string[] executions =
        [
            StdAdoConstants.SqlInsertExecutionHistory,
            StdAdoConstants.SqlSelectExecutionHistory,
            StdAdoConstants.SqlCountExecutionHistory,
            StdAdoConstants.SqlDeleteExecutionHistoryBefore,
            StdAdoConstants.SqlSelectExecutionHistoryCountBoundary,
            StdAdoConstants.SqlSelectExecutionHistoryBatchBoundary,
            StdAdoConstants.SqlSelectExecutionHistoryFiredTimeBefore,
            StdAdoConstants.SqlSelectExecutionHistoryFiredTime,
            StdAdoConstants.SqlSelectJobsOverHistoryCap
        ];

        executions.Should().AllSatisfy(sql =>
            sql.Should().Contain(AdoConstants.TableExecutionHistory)
                .And.NotContain(AdoConstants.TableMisfireHistory));

        string[] misfires =
        [
            StdAdoConstants.SqlInsertMisfireHistory,
            StdAdoConstants.SqlSelectMisfireHistory,
            StdAdoConstants.SqlCountMisfireHistory,
            StdAdoConstants.SqlCountMisfiresSince,
            StdAdoConstants.SqlDeleteMisfireHistoryBefore,
            StdAdoConstants.SqlSelectMisfireHistoryCountBoundary,
            StdAdoConstants.SqlSelectMisfireHistoryBatchBoundary
        ];

        misfires.Should().AllSatisfy(sql =>
            sql.Should().Contain(AdoConstants.TableMisfireHistory)
                .And.NotContain(AdoConstants.TableExecutionHistory));
    }

    /// <summary>
    /// Both reads break a tie on <c>ENTRY_ID</c>.
    /// </summary>
    /// <remarks>
    /// A batch firing writes several rows on one instant, and an <c>ORDER BY</c> that stops at the
    /// instant leaves the engine free to return them in any order — so the same row can appear on two
    /// consecutive pages and another on neither.
    /// </remarks>
    [Test]
    public void BothReadsOrderNewestFirstWithAStableTieBreak()
    {
        StdAdoConstants.SqlOrderByExecutionHistory.Should()
            .Be($" ORDER BY {AdoConstants.ColumnFiredTime} DESC, {AdoConstants.ColumnEntryId} DESC");

        StdAdoConstants.SqlOrderByMisfireHistory.Should()
            .Be($" ORDER BY {AdoConstants.ColumnMisfireTime} DESC, {AdoConstants.ColumnEntryId} DESC");
    }

    private static readonly JobStatusStatement[] statusUpdates =
    [
        StdAdoConstants.SqlUpdateJobStatusSucceeded,
        StdAdoConstants.SqlUpdateJobStatusFailedFinally,
        StdAdoConstants.SqlUpdateJobStatusFailedRetried,
        StdAdoConstants.SqlUpdateJobStatusOther
    ];

    /// <summary>
    /// Each placeholder of a status update is a parameter of its own, bound in the order the statement
    /// names it.
    /// </summary>
    /// <remarks>
    /// The fire time is compared once per column. A provider that binds by position takes one value per
    /// placeholder in order, so one name used twice would leave every later value one place off.
    /// </remarks>
    [Test]
    public void EveryStatusUpdatePlaceholderIsAParameterOfItsOwnInOrder()
    {
        foreach (JobStatusStatement statement in statusUpdates)
        {
            List<string> placeholders = [.. Regex.Matches(statement.Sql, "@([A-Za-z0-9_]+)").Select(match => match.Groups[1].Value)];

            placeholders.Should().Equal(statement.Parameters.Select(parameter => parameter.Name),
                "the binder walks Parameters, so they are the statement's placeholders in its order");
            placeholders.Should().OnlyHaveUniqueItems("a name bound twice is a value short for a positional provider");
        }
    }

    /// <summary>
    /// <c>LAST_FIRED_TIME</c> is the last column a status update assigns, and <c>LAST_FAILURE_MESSAGE</c>
    /// comes before <c>LAST_FAILURE_TIME</c>.
    /// </summary>
    /// <remarks>
    /// MySQL evaluates a <c>SET</c> list left to right, so an expression after an assignment reads the new
    /// value: a <c>LAST_*</c> column assigned after <c>LAST_FIRED_TIME</c> would compare the execution with
    /// itself and always move.
    /// </remarks>
    [Test]
    public void EveryStatusUpdateAssignsTheInstantsItComparesAgainstLast()
    {
        foreach (JobStatusStatement statement in statusUpdates)
        {
            List<string> assigned = Assignments(statement.Sql);

            assigned.Should().EndWith(AdoConstants.ColumnLastFiredTime, "every other column compares against it");

            if (assigned.Contains(AdoConstants.ColumnLastFailureTime))
            {
                assigned.IndexOf(AdoConstants.ColumnLastFailureMessage).Should().BeLessThan(
                    assigned.IndexOf(AdoConstants.ColumnLastFailureTime),
                    "the message moves with the failure time it is compared against");
            }
        }
    }

    [Test]
    public void EachStatusUpdateMovesWhatItsKindOfRunMoves()
    {
        Assignments(StdAdoConstants.SqlUpdateJobStatusSucceeded.Sql).Should()
            .Contain([AdoConstants.ColumnLastSuccessTime, AdoConstants.ColumnConsecutiveFailures])
            .And.NotContain([AdoConstants.ColumnFailureCount, AdoConstants.ColumnLastFailureTime]);

        Assignments(StdAdoConstants.SqlUpdateJobStatusFailedFinally.Sql).Should()
            .Contain([AdoConstants.ColumnFailureCount, AdoConstants.ColumnConsecutiveFailures, AdoConstants.ColumnLastFailureTime])
            .And.NotContain(AdoConstants.ColumnLastSuccessTime);

        Assignments(StdAdoConstants.SqlUpdateJobStatusFailedRetried.Sql).Should()
            .Contain(AdoConstants.ColumnLastFailureTime, "a retried failure is still the latest failure")
            .And.NotContain([AdoConstants.ColumnFailureCount, AdoConstants.ColumnConsecutiveFailures, AdoConstants.ColumnLastSuccessTime],
                "it is not a final one, so it neither counts nor breaks a run of failures");

        Assignments(StdAdoConstants.SqlUpdateJobStatusOther.Sql).Should()
            .NotContain([AdoConstants.ColumnFailureCount, AdoConstants.ColumnConsecutiveFailures, AdoConstants.ColumnLastSuccessTime, AdoConstants.ColumnLastFailureTime],
                "a cancelled run is counted as a run and nothing else");
    }

    /// <summary>
    /// No history statement uses a window function.
    /// </summary>
    /// <remarks>
    /// Six dialects spell <c>ROW_NUMBER() OVER (…)</c> differently or, on old Firebird and MySQL, not at all.
    /// The bounds are found by paging to a boundary row and by <c>GROUP BY … HAVING</c>.
    /// </remarks>
    [Test]
    public void NoHistoryStatementUsesAWindowFunction()
    {
        IEnumerable<string> statements = typeof(StdAdoConstants)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string) field.GetValue(null)!)
            .Concat(statusUpdates.Select(statement => statement.Sql));

        statements.Should().NotContain(sql => sql.Contains("OVER (", StringComparison.OrdinalIgnoreCase)
                                              || sql.Contains("OVER(", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The per-job cap's <c>GROUP BY</c> ends with its <c>ORDER BY</c>, which the dialect's paging clause
    /// then follows.
    /// </summary>
    /// <remarks>
    /// SQL Server, Oracle and Firebird refuse <c>OFFSET … FETCH</c> without an <c>ORDER BY</c>, and a page
    /// of an unordered set is a different page every time.
    /// </remarks>
    [Test]
    public void TheJobsOverTheCapAreOrderedBeforeTheyArePaged()
    {
        string sql = StdAdoConstants.SqlSelectJobsOverHistoryCap;

        sql.Should().EndWith($" ORDER BY {AdoConstants.ColumnJobGroup}, {AdoConstants.ColumnJobName}");
        sql.IndexOf(" GROUP BY ", StringComparison.Ordinal).Should().BeLessThan(sql.IndexOf(" HAVING ", StringComparison.Ordinal));
        sql.IndexOf(" HAVING ", StringComparison.Ordinal).Should().BeLessThan(sql.IndexOf(" ORDER BY ", StringComparison.Ordinal));
    }

    /// <summary>The columns a status update's <c>SET</c> list assigns, in order.</summary>
    private static List<string> Assignments(string sql)
    {
        int set = sql.IndexOf(" SET ", StringComparison.Ordinal) + " SET ".Length;
        int where = sql.IndexOf(" WHERE ", StringComparison.Ordinal);

        return [.. Regex.Matches(sql[set..where], @"(?:^|, )([A-Z_]+) = ").Select(match => match.Groups[1].Value)];
    }
}
