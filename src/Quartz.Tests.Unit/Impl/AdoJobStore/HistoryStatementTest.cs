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
            StdAdoConstants.SqlSelectExecutionHistoryBatchBoundary
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
}
