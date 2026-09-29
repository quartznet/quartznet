using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The words the pages use for a run's result and a firing's reason, and how a run's metrics are read for
/// display.
/// </summary>
public class RunResultDisplayTest
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("not json")]
    [TestCase("[1, 2]")]
    [TestCase("\"a string\"")]
    public void MetricsThatAreNotAJsonObjectShowNothing(string? metricsJson)
    {
        RunResultDisplay.Metrics(metricsJson).Should().BeEmpty(
            "a store of an application's own may keep anything in the column, and the row still has to render");
    }

    [Test]
    public void EachMetricReadsAsItsNameAndItsValue()
    {
        RunResultDisplay.Metrics("""{"scanned":1200,"note":"café","ok":false,"nothing":null,"nested":{"a":1}}""")
            .Should().Equal(
                new KeyValuePair<string, string>("scanned", "1200"),
                new KeyValuePair<string, string>("note", "café"),
                new KeyValuePair<string, string>("ok", "false"),
                new KeyValuePair<string, string>("nothing", "null"),
                new KeyValuePair<string, string>("nested", """{"a":1}"""));
    }

    [Test]
    public void AResultANewerHostAppendedIsShownAsWhatItIs()
    {
        RunResultDisplay.Label((JobRunResult) 9).Should().Be("9",
            "calling it one of the four results it is not would be a lie about the run");
    }

    [TestCase(MisfireReason.Missed, "Misfire")]
    [TestCase(MisfireReason.Overlap, "Overlap")]
    [TestCase(MisfireReason.Vetoed, "Vetoed")]
    public void EachReasonHasItsWord(MisfireReason reason, string label)
    {
        RunResultDisplay.Label(reason).Should().Be(label);
    }
}
