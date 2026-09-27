using Quartz.HttpApiContract;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// The one reading of a listing's query string: its page, one matcher per filter, and a fire-instance
/// state.
/// </summary>
public class ListingParametersTest
{
    [Test]
    public void APageIsReadAndApplied()
    {
        ListingParameters listing = ListingParameters.Read(skip: 2, take: "5", includeTotalCount: true, maxPageSize: 1000);

        JobQuery query = listing.Page(new JobQuery());

        query.Skip.Should().Be(2);
        query.Take.Should().Be(5);
        query.IncludeTotalCount.Should().BeTrue();
    }

    [Test]
    public void ARequestThatNamesNoTakeKeepsTheQueryRecordsDefault()
    {
        ListingParameters listing = ListingParameters.Read(skip: 0, take: " ", includeTotalCount: false, maxPageSize: 1000);

        listing.Take.Should().BeNull();
        listing.Page(new TriggerQuery()).Take.Should().Be(PagedQuery.DefaultTake,
            "a listing that names no page size gets the one the query record defaults to, as in process");
    }

    [TestCase("all", 1000, 1000)]
    [TestCase("ALL", 0, PagedQuery.All)]
    [TestCase("5000", 0, 5000)]
    public void AllIsAsManyAsTheCapAllows(string take, int maxPageSize, int expected)
    {
        ListingParameters.Read(0, take, includeTotalCount: false, maxPageSize).Take.Should().Be(expected,
            "'all' is answered with the cap and hasMore, and a server with no cap answers with everything");
    }

    [TestCase(-1, null, "skip must not be negative")]
    [TestCase(0, "many", "take must be a number or 'all', which asks for every match")]
    [TestCase(0, "-3", "take must not be negative")]
    [TestCase(0, "1001", "take must be at most 1000, was 1001.*raise QuartzHttpApiOptions.MaxPageSize.")]
    public void AMalformedPageIsRefused(int skip, string take, string message)
    {
        Action act = () => ListingParameters.Read(skip, take, includeTotalCount: false, maxPageSize: 1000);

        act.Should().Throw<InvalidRequestException>().WithMessage(message, "a page the server cannot meet is a 400 saying what to fix");
    }

    [Test]
    public void AFireInstanceStateIsReadAsTheQueryRecordReadsIt()
    {
        ListingParameters unnamed = ListingParameters.Read(0, null, false, 1000);
        ListingParameters any = ListingParameters.Read(0, null, false, 1000, state: "any");
        ListingParameters executing = ListingParameters.Read(0, null, false, 1000, state: "executing");

        unnamed.StateNamed.Should().BeFalse("a request that names no state gets the record's default, which is Executing");
        any.StateNamed.Should().BeTrue();
        any.State.Should().BeNull("Any is every state, which the record spells as null");
        executing.State.Should().Be(FireInstanceState.Executing);
    }

    [TestCase("bogus")]
    [TestCase("999")]
    public void AnUnknownFireInstanceStateIsRefused(string state)
    {
        Action act = () => ListingParameters.Read(0, null, false, 1000, state);

        act.Should().Throw<InvalidRequestException>().WithMessage($"Unknown fire instance state '{state}'");
    }

    [Test]
    public void EachGroupMatcherBuildsItsFilter()
    {
        ListingParameters.Groups("port", null, null, null).GroupFilter<JobKey>().CompareWithOperator.Should().Be(StringOperator.Contains);
        ListingParameters.Groups(null, "rts", null, null).GroupFilter<JobKey>().CompareWithOperator.Should().Be(StringOperator.EndsWith);
        ListingParameters.Groups(null, null, "rep", null).GroupFilter<JobKey>().CompareWithOperator.Should().Be(StringOperator.StartsWith);
        ListingParameters.Groups(null, null, null, "reports").GroupFilter<TriggerKey>().CompareToValue.Should().Be("reports");
        ListingParameters.Groups(null, null, null, null).GroupFilter<TriggerKey>().CompareWithOperator.Should().Be(StringOperator.Anything,
            "a group filter always ends up as a matcher, and naming none is every group");
    }

    [Test]
    public void EachNameMatcherBuildsItsFilter()
    {
        ListingParameters listing = ListingParameters.Read(0, null, false, 1000);

        (listing with { NameContains = "ight" }).NameFilter<JobKey>()!.CompareWithOperator.Should().Be(StringOperator.Contains);
        (listing with { NameEndsWith = "ly" }).NameFilter<TriggerKey>()!.CompareWithOperator.Should().Be(StringOperator.EndsWith);
        (listing with { NameStartsWith = "ni" }).NameFilter()!.CompareWithOperator.Should().Be(StringOperator.StartsWith);
        (listing with { NameEquals = "nightly" }).NameFilter()!.CompareToValue.Should().Be("nightly");
        (listing with { NameContains = "ight" }).NameFilter()!.CompareWithOperator.Should().Be(StringOperator.Contains);
        (listing with { NameEndsWith = "ly" }).NameFilter()!.CompareWithOperator.Should().Be(StringOperator.EndsWith);
        (listing with { NameStartsWith = "ni" }).NameFilter<JobKey>()!.CompareWithOperator.Should().Be(StringOperator.StartsWith);
        (listing with { NameEquals = "nightly" }).NameFilter<JobKey>()!.CompareToValue.Should().Be("nightly");
        listing.NameFilter<JobKey>().Should().BeNull("a name filter is optional, where a group filter is not");
        listing.NameFilter().Should().BeNull();
    }

    [Test]
    public void OneMatcherPerFilter()
    {
        ListingParameters twoGroups = ListingParameters.Groups("a", "b", null, null);
        ListingParameters twoNames = ListingParameters.Read(0, null, false, 1000) with { NameEquals = "a", NameStartsWith = "b" };

        Action group = () => twoGroups.GroupFilter<JobKey>();
        Action keyedName = () => twoNames.NameFilter<JobKey>();
        Action name = () => twoNames.NameFilter();

        group.Should().Throw<InvalidRequestException>().WithMessage("Only single match rule can be given");
        keyedName.Should().Throw<InvalidRequestException>().WithMessage("Only single match rule can be given");
        name.Should().Throw<InvalidRequestException>().WithMessage("Only single match rule can be given");
    }
}
