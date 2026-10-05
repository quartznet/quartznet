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

using System.Data;
using System.Numerics;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What the run statistics say differently per dialect: how a bucket is named, and whether the percentiles
/// are the database's or interpolated here.
/// </summary>
/// <remarks>
/// SQLite runs the ranked read in the unit suite; the aggregate read is PostgreSQL's and Oracle's, which only
/// the integration legs reach, so its reader is held to a hand-made result set here.
/// </remarks>
public sealed class StatisticsStatementTest
{
    [Test]
    public void EachDialectNamesABucketByIntegerDivision()
    {
        new SQLiteDelegate().HistoryStatisticsBucketExpression.Should().Be("FIRED_TIME / @historyBucketSize",
            "SQLite, PostgreSQL, SQL Server and Firebird divide two integers as integers");
        new PostgreSQLDelegate().HistoryStatisticsBucketExpression.Should().Be("FIRED_TIME / @historyBucketSize");
        new SqlServerDelegate().HistoryStatisticsBucketExpression.Should().Be("FIRED_TIME / @historyBucketSize");
        new FirebirdDelegate().HistoryStatisticsBucketExpression.Should().Be("FIRED_TIME / CAST(@historyBucketSize AS BIGINT)",
            "Firebird refuses a bare parameter in a division inside a common table expression, 'Invalid data type for division in dialect 3'");
        new MySQLDelegate().HistoryStatisticsBucketExpression.Should().Be("FIRED_TIME DIV @historyBucketSize",
            "MySQL's / divides exactly, and every run would be a bucket of its own");
        new OracleDelegate().HistoryStatisticsBucketExpression.Should().Be("FLOOR(FIRED_TIME / @historyBucketSize)",
            "a NUMBER divides exactly too");
    }

    [Test]
    public void OnlyPostgreSqlAndOracleAggregateThePercentiles()
    {
        new PostgreSQLDelegate().HistoryHasPercentileAggregate.Should().BeTrue();
        new OracleDelegate().HistoryHasPercentileAggregate.Should().BeTrue();

        new SqlServerDelegate().HistoryHasPercentileAggregate.Should().BeFalse(
            "SQL Server's PERCENTILE_CONT is a window function, which sorts every row once more per percentile");
        new MySQLDelegate().HistoryHasPercentileAggregate.Should().BeFalse();
        new SQLiteDelegate().HistoryHasPercentileAggregate.Should().BeFalse();
        new FirebirdDelegate().HistoryHasPercentileAggregate.Should().BeFalse();
    }

    /// <summary>
    /// The derived table is grouped by a column, never by an expression holding a parameter, which Oracle and
    /// Firebird refuse because they cannot prove the SELECT list's occurrence is the GROUP BY's.
    /// </summary>
    [Test]
    public void TheBucketIsGroupedAsAColumnOfTheDerivedTable()
    {
        StdAdoConstants.SqlSelectExecutionStatisticsAggregateTail.Should().Be(") h GROUP BY STAT_BUCKET ORDER BY STAT_BUCKET");
        StdAdoConstants.SqlSelectExecutionStatisticsAggregate.Should()
            .Contain("ROUND(PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY RUN_TIME))")
            .And.Contain("ROUND(PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY RUN_TIME))")
            .And.EndWith("FROM (SELECT ");
        StdAdoConstants.SqlSelectExecutionStatisticsRanked.Should().Be("WITH STAT_ROWS AS (SELECT ",
            "the rows are read once as a common table expression, so the parameters are bound once");
        StdAdoConstants.SqlSelectExecutionStatisticsRankedTail.Should()
            .Contain("FROM STAT_ROWS) r JOIN (SELECT STAT_BUCKET, COUNT(*) AS STAT_RUNS")
            .And.Contain("FROM STAT_ROWS GROUP BY STAT_BUCKET) c ON c.STAT_BUCKET = r.STAT_BUCKET")
            .And.Contain(
                "((r.STAT_RANK - 1) * 100 > (c.STAT_RUNS - 1) * 95 - 100 AND (r.STAT_RANK - 1) * 100 < (c.STAT_RUNS - 1) * 95 + 100)",
                "which ranks to keep is decided with integers alone, because the dialects divide integers differently");
    }

    /// <summary>
    /// A row of PostgreSQL's or Oracle's: the counts, the longest run and the two percentiles already rounded to
    /// a tick, which Oracle hands back as decimals and PostgreSQL as doubles.
    /// </summary>
    [Test]
    public async Task TheAggregateReadTakesTheDatabasesPercentiles()
    {
        using DataTable table = new();
        table.Columns.Add("STAT_BUCKET", typeof(decimal));
        table.Columns.Add("SUCCEEDED", typeof(decimal));
        table.Columns.Add("FAILED", typeof(long));
        table.Columns.Add("CANCELLED", typeof(long));
        table.Columns.Add("SKIPPED", typeof(long));
        table.Columns.Add("MAX_RUN_TIME", typeof(long));
        table.Columns.Add("P50", typeof(double));
        table.Columns.Add("P95", typeof(decimal));

        long nine = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero).UtcTicks / TimeSpan.TicksPerHour;
        table.Rows.Add(nine, 2m, 1L, 1L, 1L, TimeSpan.FromMilliseconds(1000).Ticks, (double) TimeSpan.FromMilliseconds(300).Ticks, (decimal) TimeSpan.FromMilliseconds(880).Ticks);
        table.Rows.Add(nine + 3, 0m, 0L, 0L, 1L, TimeSpan.FromMilliseconds(70).Ticks, (double) TimeSpan.FromMilliseconds(70).Ticks, (decimal) TimeSpan.FromMilliseconds(70).Ticks);

        using DataTableReader reader = table.CreateDataReader();
        List<ExecutionStatisticsBucket> buckets = await StdAdoDelegate.ReadAggregatedBuckets(reader, TimeSpan.FromHours(1), CancellationToken.None);

        buckets.Should().HaveCount(2);
        buckets[0].StartUtc.Should().Be(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
        buckets[0].RunCount.Should().Be(5);
        buckets[0].SucceededCount.Should().Be(2);
        buckets[0].MaxDuration.Should().Be(TimeSpan.FromMilliseconds(1000));
        buckets[0].P50Duration.Should().Be(TimeSpan.FromMilliseconds(300));
        buckets[0].P95Duration.Should().Be(TimeSpan.FromMilliseconds(880));

        buckets[1].StartUtc.Should().Be(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), "the bucket number counts hours from the start of time");
        buckets[1].SkippedCount.Should().Be(1);
    }

    /// <summary>
    /// The ranked read keeps, per bucket, the runs either side of each percentile and the longest, and
    /// interpolates between them: here two buckets of five and of two runs, as SQL ranks them.
    /// </summary>
    [Test]
    public async Task TheRankedReadInterpolatesBetweenTheRunsEitherSideOfARank()
    {
        using DataTable table = new();
        table.Columns.Add("STAT_BUCKET", typeof(long));
        table.Columns.Add("RUN_TIME", typeof(long));
        table.Columns.Add("STAT_RANK", typeof(long));
        table.Columns.Add("STAT_RUNS", typeof(long));

        // Firebird 4 sums BIGINTs into an INT128, which its driver hands back as a BigInteger.
        table.Columns.Add("STAT_SUCCEEDED", typeof(BigInteger));
        table.Columns.Add("STAT_FAILED", typeof(long));
        table.Columns.Add("STAT_CANCELLED", typeof(long));
        table.Columns.Add("STAT_SKIPPED", typeof(long));

        // Five runs of 100, 200, 300, 400 and 1,000 ms: the median is rank 3; the 95th percentile lies at
        // position 3.8, between ranks 4 and 5, and rank 5 is the longest.
        foreach ((long rank, int milliseconds) in new[] { (3L, 300), (4L, 400), (5L, 1000) })
        {
            table.Rows.Add(100L, TimeSpan.FromMilliseconds(milliseconds).Ticks, rank, 5L, new BigInteger(2), 1L, 1L, 1L);
        }

        // Two runs of 50 and 150 ms: both percentiles lie between them.
        foreach ((long rank, int milliseconds) in new[] { (1L, 50), (2L, 150) })
        {
            table.Rows.Add(101L, TimeSpan.FromMilliseconds(milliseconds).Ticks, rank, 2L, BigInteger.One, 1L, 0L, 0L);
        }

        using DataTableReader reader = table.CreateDataReader();
        List<ExecutionStatisticsBucket> buckets = await StdAdoDelegate.ReadRankedBuckets(reader, TimeSpan.FromHours(1), CancellationToken.None);

        buckets.Select(bucket => (bucket.RunCount, bucket.P50Duration.TotalMilliseconds, bucket.P95Duration.TotalMilliseconds, bucket.MaxDuration.TotalMilliseconds))
            .Should().Equal([(5L, 300d, 880d, 1000d), (2L, 100d, 145d, 150d)]);
        buckets[0].SucceededCount.Should().Be(2, "an INT128 count reads as the number it is");
    }
}
