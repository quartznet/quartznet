using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.Tests.Unit.DashboardAgent;

/// <summary>
/// The agent reads a query string the way ASP.NET Core binds a handler's parameters from one.
/// </summary>
public class AgentQueryTest
{
    [Test]
    public void ValuesAreReadByNameIgnoringCaseWithEscapesUndone()
    {
        AgentQuery query = AgentQuery.Parse("schedulers/x/jobs?Skip=5&take=all&groupContains=night%20shift&name=a+b&flag");

        query.GetInt("skip", 0).Should().Be(5);
        query.Get("TAKE").Should().Be("all");
        query.Get("groupContains").Should().Be("night shift");
        query.Get("name").Should().Be("a b", "a plus is a space in a query string, as ASP.NET Core reads it");
        query.Get("flag").Should().Be(string.Empty, "a name with no value is present and empty");
        query.Get("absent").Should().BeNull();
    }

    [Test]
    public void ARepeatedParameterIsReadAsEveryValue()
    {
        AgentQuery query = AgentQuery.Parse("?results=Failed&results=Skipped");

        query.GetAll("results").Should().Equal(["Failed", "Skipped"]);
        query.Get("results").Should().Be("Failed", "the first value is the one a single-valued parameter binds");
    }

    [Test]
    public void TypedValuesAreParsedInvariantlyAndRefusedWhenMalformed()
    {
        AgentQuery query = AgentQuery.Parse("?since=2026-07-01T12:00:00Z&bucket=01:30:00&paused=true&state=Paused&bad=nope");

        query.GetDateTimeOffset("since").Should().Be(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        query.GetTimeSpan("bucket").Should().Be(TimeSpan.FromMinutes(90));
        query.GetNullableBool("paused").Should().BeTrue();
        query.GetEnum<TriggerState>("state").Should().Be(TriggerState.Paused);
        query.GetEnum<TriggerState>("absent").Should().BeNull();

        Action badInt = () => query.GetInt("bad", 0);
        badInt.Should().Throw<InvalidRequestException>().WithMessage("*Failed to bind parameter \"bad\" from \"nope\"*",
            "a value that does not parse is a 400, under the words ASP.NET Core uses");

        Action badEnum = () => query.GetEnum<TriggerState>("bad");
        badEnum.Should().Throw<InvalidRequestException>();
    }

    [Test]
    public void APathWithNoQueryReadsAsEmpty()
    {
        AgentQuery query = AgentQuery.Parse("schedulers/x/jobs");

        query.Get("skip").Should().BeNull();
        query.GetInt("skip", 7).Should().Be(7);
        query.GetBool("includeTotalCount", false).Should().BeFalse();
    }
}
