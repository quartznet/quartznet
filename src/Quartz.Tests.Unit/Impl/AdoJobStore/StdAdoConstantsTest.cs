using System.Globalization;
using System.Threading;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class StdAdoConstantsTest
{
    [Test]
    public void ShouldProduceResultsInInvariantCulture()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("lt-LT");

        var sql = StdAdoConstants.SqlSelectMisfiredTriggers;

        sql.Should().Be("SELECT * FROM {0}TRIGGERS WHERE SCHED_NAME = @schedulerName AND MISFIRE_INSTR <> -1 AND NEXT_FIRE_TIME <= @nextFireTime ORDER BY NEXT_FIRE_TIME ASC, PRIORITY DESC");
    }

    /// <summary>
    /// A waiting trigger belongs to acquisition or to the misfire handler, never to both and never to
    /// neither: the acquisition statement's <c>@noEarlierThan</c> predicate has to be the exact
    /// complement of the misfire statements' <c>@nextFireTime</c> one, since both parameters are bound
    /// to the same <c>now - MisfireThreshold</c>.
    /// </summary>
    /// <remarks>
    /// The comparison is <c>&lt;=</c> because that is what the whole scheduler means by a misfire —
    /// <c>RAMJobStore.ApplyMisfire</c> and <c>JobStoreSupport.UpdateMisfiredTrigger</c> decline only a
    /// trigger whose fire time is strictly later — so the threshold instant itself is late (#3462).
    /// </remarks>
    [Test]
    public void TheAcquisitionPredicateShouldBeTheComplementOfTheMisfirePredicate()
    {
        StdAdoConstants.SqlCountMisfiredTriggersInStates.Should().Contain("NEXT_FIRE_TIME <= @nextFireTime",
            "a trigger due at or before now - MisfireThreshold is misfired");
        StdAdoConstants.SqlSelectHasMisfiredTriggersInState.Should().Contain("NEXT_FIRE_TIME <= @nextFireTime",
            "the sweep and the count it is peeked at with have to select the same rows");
        StdAdoConstants.SqlSelectMisfiredTriggersInState.Should().Contain("NEXT_FIRE_TIME <= @nextFireTime");
        StdAdoConstants.SqlSelectMisfiredTriggersInGroupInState.Should().Contain("NEXT_FIRE_TIME <= @nextFireTime");

        StdAdoConstants.SqlSelectNextTriggerToAcquire.Should().Contain("NEXT_FIRE_TIME > @noEarlierThan",
            "acquisition takes what is not misfired, so a trigger exactly on the threshold instant is left to the misfire handler rather than fired late without its policy");
        StdAdoConstants.SqlSelectNextTriggerToAcquireWithExecutionGroup.Should().Contain("NEXT_FIRE_TIME > @noEarlierThan");
        StdAdoConstants.SqlSelectNextTriggerToAcquireWithPreferredNode.Should().Contain("NEXT_FIRE_TIME > @noEarlierThan");
        StdAdoConstants.SqlSelectNextTriggerToAcquireWithPreferredNodeOnly.Should().Contain("NEXT_FIRE_TIME > @noEarlierThan");

        foreach (string sql in new[]
                 {
                     StdAdoConstants.SqlCountMisfiredTriggersInStates,
                     StdAdoConstants.SqlSelectHasMisfiredTriggersInState,
                     StdAdoConstants.SqlSelectMisfiredTriggers,
                     StdAdoConstants.SqlSelectMisfiredTriggersInState,
                     StdAdoConstants.SqlSelectMisfiredTriggersInGroupInState,
                     StdAdoConstants.SqlSelectNextTriggerToAcquire,
                     StdAdoConstants.SqlSelectNextTriggerToAcquireWithExecutionGroup,
                     StdAdoConstants.SqlSelectNextTriggerToAcquireWithPreferredNode,
                     StdAdoConstants.SqlSelectNextTriggerToAcquireWithPreferredNodeOnly
                 })
        {
            sql.Should().NotContain("NEXT_FIRE_TIME < @nextFireTime", "strictly before was the rule the sweep alone had");
            sql.Should().NotContain("NEXT_FIRE_TIME >= @noEarlierThan", "at or after would overlap the misfire predicate on the threshold instant");
        }
    }
}
