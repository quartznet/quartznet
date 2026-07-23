
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class AdoUtilTest
{
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(3, 4)]
    [TestCase(5, 8)]
    [TestCase(20, 32)]
    [TestCase(129, 200)]
    [TestCase(200, 200)]
    public void RoundUpTriggerKeyCount_ShouldRoundToBucket(int count, int expected)
    {
        AdoUtil.RoundUpTriggerKeyCount(count).Should().Be(expected);
    }

    [Test]
    public void RoundUpTriggerKeyCount_ShouldProduceFewDistinctStatements()
    {
        // The point of bucketing: a plan cache should see a handful of statements, not one per batch size.
        var distinct = Enumerable.Range(1, AdoUtil.MaxTriggerKeysPerPredicate)
            .Select(AdoUtil.RoundUpTriggerKeyCount)
            .Distinct()
            .ToArray();

        distinct.Should().HaveCountLessThan(12);
    }

    [Test]
    public void BuildTriggerKeyPredicate_ShouldProduceParameterizedDisjunction()
    {
        AdoUtil.BuildTriggerKeyPredicate(2).Should().Be(
            "((TRIGGER_NAME = @tkn000 AND TRIGGER_GROUP = @tkg000) OR (TRIGGER_NAME = @tkn001 AND TRIGGER_GROUP = @tkg001))");
    }

    [Test]
    public void BuildTriggerKeyPredicate_ShouldRejectUnroundedCount()
    {
        var act = () => AdoUtil.BuildTriggerKeyPredicate(3);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Parameter placeholders are rewritten in the statement text by substring replacement for providers
    /// that do not use the '@' prefix, so no generated name may be a prefix of another one — otherwise
    /// replacing @tkn001 would also corrupt @tkn0010.
    /// </summary>
    [Test]
    public void TriggerKeyParameterNames_ShouldBeFixedWidth()
    {
        var names = Enumerable.Range(0, AdoUtil.MaxTriggerKeysPerPredicate)
            .SelectMany(i => new[] { AdoUtil.TriggerKeyNameParameter(i), AdoUtil.TriggerKeyGroupParameter(i) })
            .ToArray();

        names.Should().OnlyHaveUniqueItems();
        names.Select(x => x.Length).Distinct().Should().ContainSingle();
    }

    /// <summary>
    /// The predicate is appended to SQL that later goes through <see cref="AdoJobStoreUtil.ReplaceTablePrefix" />,
    /// which runs string.Format over it — so it must not contain unescaped braces.
    /// </summary>
    [Test]
    public void BuildTriggerKeyPredicate_ShouldSurviveTablePrefixSubstitution()
    {
        var sql = StdAdoConstants.SqlSelectSimpropTriggersByKeysPrefix + AdoUtil.BuildTriggerKeyPredicate(8);

        var act = () => AdoJobStoreUtil.ReplaceTablePrefix(sql, "QRTZ_");

        act.Should().NotThrow();
        AdoJobStoreUtil.ReplaceTablePrefix(sql, "QRTZ_").Should().StartWith("SELECT * FROM QRTZ_SIMPROP_TRIGGERS WHERE");
    }
}
