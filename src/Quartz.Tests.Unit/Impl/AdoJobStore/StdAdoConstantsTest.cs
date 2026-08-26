using System.Globalization;


using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class StdAdoConstantsTest
{
    [Test]
    public void ShouldProduceResultsInInvariantCulture()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("lt-LT");

        var sql = StdAdoConstants.SqlCountMisfiredTriggersInStates;

        sql.Should().Be("SELECT COUNT(TRIGGER_NAME) FROM {0}TRIGGERS WHERE SCHED_NAME = @schedulerName AND MISFIRE_INSTR <> -1 AND NEXT_FIRE_TIME <= @nextFireTime AND TRIGGER_STATE = @state");
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
        sql.Should().Contain("t.NEXT_FIRE_TIME <= @nextFireTime");
        sql.Should().Contain("t.TRIGGER_STATE = @state");
        sql.Should().Contain("ORDER BY t.NEXT_FIRE_TIME ASC, t.PRIORITY DESC");
    }

    /// <summary>
    /// A waiting trigger belongs to acquisition or to the misfire handler, never to both and never to
    /// neither: the acquisition statement's <c>@noEarlierThan</c> predicate has to be the exact
    /// complement of the misfire statements' <c>@nextFireTime</c> one, since both parameters are bound
    /// to the same <c>now - MisfireThreshold</c>.
    /// </summary>
    /// <remarks>
    /// The comparison is <c>&lt;=</c> because that is what the whole scheduler means by a misfire —
    /// <c>RAMJobStore.ApplyMisfireNoLock</c> and <c>AdoJobStoreBase.UpdateMisfiredTrigger</c> decline
    /// only a trigger whose fire time is strictly later — so the threshold instant itself is late.
    /// </remarks>
    [Test]
    public void TheAcquisitionPredicateShouldBeTheComplementOfTheMisfirePredicate()
    {
        StdAdoConstants.SqlCountMisfiredTriggersInStates.Should().Contain("NEXT_FIRE_TIME <= @nextFireTime",
            "a trigger due at or before now - MisfireThreshold is misfired");
        StdAdoConstants.SqlSelectMisfiredTriggersToRecover.Should().Contain("t.NEXT_FIRE_TIME <= @nextFireTime",
            "the sweep and the count it is peeked at with have to select the same rows");
        StdAdoConstants.SqlSelectNextTriggerToAcquire.Should().Contain("NEXT_FIRE_TIME > @noEarlierThan",
            "acquisition takes what is not misfired, so a trigger exactly on the threshold instant is left to the misfire handler rather than fired late without its policy");
    }

    [Test]
    public void BuildSqlSelectNextTriggerToAcquire_WithNoExclusions_ShouldKeepExistingSqlExactly()
    {
        string sql = StdAdoConstants.BuildSqlSelectNextTriggerToAcquire(excludedJobTypeBucket: 0);

        sql.Should().BeSameAs(StdAdoConstants.SqlSelectNextTriggerToAcquire,
            "the two are one template with an empty exclusion clause, so asking for no exclusions hands back the very string every exclusion-free caller already uses rather than an equal copy built beside it");
    }

    [Test]
    public void BuildSqlSelectNextTriggerToAcquire_ShouldUseFixedWidthExclusionParameters()
    {
        string sql = StdAdoConstants.BuildSqlSelectNextTriggerToAcquire(excludedJobTypeBucket: 16);

        sql.Should().Contain("AND jd.JOB_CLASS_NAME NOT IN (@excludedJobType0000, @excludedJobType0001");
        sql.Should().Contain("@excludedJobType0009, @excludedJobType0010");
        sql.Should().Contain("@excludedJobType0015)");
        sql.Should().NotContain("@excludedJobType0016");
    }

    [Test]
    public void RoundUpExcludedJobTypeCount_ShouldRoundPastBucketBoundary()
    {
        int bucket = StdAdoConstants.RoundUpExcludedJobTypeCount(129);

        bucket.Should().Be(256);
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
