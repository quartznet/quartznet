using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The three places a row limit can go, and the one case where it goes nowhere.
/// </summary>
/// <remarks>
/// The statements themselves are pinned by <see cref="AcquisitionSqlTest" />; this is about the slot
/// values in isolation, including the spaces they carry, which are what make an unlimited statement
/// the text it was before there was a slot to fill. The template below is the shape of the real ones:
/// the <c>SELECT</c> keyword, then a line break before the projection.
/// </remarks>
public class SqlRowLimitTest
{
    [Test]
    public void UnlimitedFillsNothing()
    {
        Fill(SqlRowLimit.Unlimited).Should().Be("SELECT\n  A\nFROM T",
            "a dialect that cannot limit rows has to get the statement back byte for byte, or every statement changes the day a slot is added");
    }

    [Test]
    public void DefaultIsUnlimited()
    {
        default(SqlRowLimit).Should().Be(SqlRowLimit.Unlimited,
            "the delegate compares against Unlimited to decide whether a statement can be served from the cache");
    }

    [Test]
    public void InProjectionSitsBetweenTheSelectKeywordAndTheColumns()
    {
        Fill(SqlRowLimit.InProjection("TOP", 5)).Should().Be("SELECT TOP 5 \n  A\nFROM T");
    }

    [Test]
    public void AtStatementEndFollowsEverythingElse()
    {
        Fill(SqlRowLimit.AtStatementEnd("LIMIT", 5)).Should().Be("SELECT\n  A\nFROM T LIMIT 5");
    }

    [Test]
    public void InEnclosingSelectWrapsTheWholeStatement()
    {
        Fill(SqlRowLimit.InEnclosingSelect("rownum", 5)).Should().Be("SELECT * FROM (SELECT\n  A\nFROM T) WHERE rownum <= 5");
    }

    [Test]
    public void TwoLimitsThatSayTheSameThingAreEqual()
    {
        SqlRowLimit.AtStatementEnd("LIMIT", 5).Should().Be(SqlRowLimit.AtStatementEnd("LIMIT", 5));
        SqlRowLimit.AtStatementEnd("LIMIT", 5).Should().NotBe(SqlRowLimit.AtStatementEnd("ROWS", 5),
            "the keyword is part of what a limit says");
        SqlRowLimit.AtStatementEnd("LIMIT", 5).Should().NotBe(SqlRowLimit.AtStatementEnd("LIMIT", 6));
        SqlRowLimit.AtStatementEnd("LIMIT", 5).Should().NotBe(SqlRowLimit.InProjection("LIMIT", 5),
            "where the clause goes is part of what a limit says");
    }

    /// <summary>
    /// Builds a statement the way <c>StdAdoConstants</c> does: the two slots in the template, then the
    /// enclosing select around the finished text.
    /// </summary>
    private static string Fill(SqlRowLimit limit) =>
        limit.Enclose("SELECT" + limit.AfterSelect + "\n  A\nFROM T" + limit.AtEnd);
}
