using System.Globalization;


using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class StdAdoConstantsTest
{
    [Test]
    public void ShouldProduceResultsInInvariantCulture()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("lt-LT");

        var sql = StdAdoConstants.SqlSelectMisfiredTriggers;

        sql.Should().Be("SELECT * FROM {0}TRIGGERS WHERE SCHED_NAME = @schedulerName AND MISFIRE_INSTR <> -1 AND NEXT_FIRE_TIME < @nextFireTime ORDER BY NEXT_FIRE_TIME ASC, PRIORITY DESC");
    }

    /// <summary>
    /// <see cref="StdAdoDelegate" /> reads JOB_DATA positionally out of this result set, so the column
    /// order is load-bearing. New columns belong at the end.
    /// </summary>
    [Test]
    public void SqlSelectTrigger_ShouldKeepJobDataAtOrdinal11()
    {
        SelectedColumns(StdAdoConstants.SqlSelectTrigger)[11].Should().Be("JOB_DATA");
    }

    [Test]
    public void SqlSelectMisfiredTriggersToRecover_ShouldSelectSameColumnsAsSqlSelectTrigger()
    {
        // The batch read materializes triggers with the same code as the single read, so it has to hand
        // that code the same columns in the same order, plus the keys it needs to tell the rows apart.
        var expected = SelectedColumns(StdAdoConstants.SqlSelectTrigger)
            .Concat(["t.TRIGGER_NAME", "t.TRIGGER_GROUP"]);

        SelectedColumns(StdAdoConstants.SqlSelectMisfiredTriggersToRecover).Should().Equal(expected);
    }

    /// <summary>
    /// SqlServerDelegate splices its <c>TOP n</c> in at offset 6, so the statement has to start with
    /// exactly <c>SELECT </c>.
    /// </summary>
    [Test]
    public void SqlSelectMisfiredTriggersToRecover_ShouldStartWithSelectKeyword()
    {
        StdAdoConstants.SqlSelectMisfiredTriggersToRecover.Substring(0, 7).Should().Be("SELECT ");
    }

    [Test]
    public void SqlSelectMisfiredTriggersToRecover_ShouldUseSameMisfirePredicateAsKeyOnlyQuery()
    {
        var sql = StdAdoConstants.SqlSelectMisfiredTriggersToRecover;

        sql.Should().Contain("t.MISFIRE_INSTR <> -1");
        sql.Should().Contain("t.NEXT_FIRE_TIME < @nextFireTime");
        sql.Should().Contain("t.TRIGGER_STATE = @state1");
        sql.Should().Contain("ORDER BY t.NEXT_FIRE_TIME ASC, t.PRIORITY DESC");
    }

    /// <summary>
    /// Returns the selected column expressions, in order, of a single-SELECT statement.
    /// </summary>
    private static string[] SelectedColumns(string sql)
    {
        var start = sql.IndexOf("SELECT", StringComparison.Ordinal) + "SELECT".Length;
        var end = sql.IndexOf("FROM", StringComparison.Ordinal);

        return sql.Substring(start, end - start)
            .Split(',')
            .Select(x => x.Trim())
            .ToArray();
    }
}