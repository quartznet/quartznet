
namespace Quartz.Tests.Unit.Queries;

public class PagedQueryTest
{
    [Test]
    public void DefaultsSelectTheFirstBoundedPage()
    {
        JobQuery query = new JobQuery();

        query.Skip.Should().Be(0, "a query without paging must start at the first item");
        query.Take.Should().Be(PagedQuery.DefaultTake, "an unpaged query must not materialize an unbounded result by accident");
        PagedQuery.DefaultTake.Should().Be(250, "the default page size is a documented wire contract");
        query.IncludeTotalCount.Should().BeFalse("counting costs a second query and must be opt-in");
        query.Group.Should().BeNull("no filter means all groups");
    }

    [Test]
    public void UnboundedIsAnExplicitOptIn()
    {
        JobQuery query = new JobQuery { Take = PagedQuery.All };

        query.Take.Should().Be(int.MaxValue, "int.MaxValue is the documented 'everything' opt-in");
        PagedQuery.All.Should().Be(int.MaxValue,
            "the constant is a name for the number the documentation used to tell readers to type, not a "
            + "different bound - a query written against either spelling asks for the same thing");
    }

    [Test]
    public void NegativeSkipIsRejected()
    {
        Action act = () => _ = new TriggerQuery { Skip = -1 };

        act.Should().Throw<ArgumentOutOfRangeException>("a negative offset has no meaning");
    }

    [Test]
    public void NegativeTakeIsRejected()
    {
        Action act = () => _ = new TriggerQuery { Take = -1 };

        act.Should().Throw<ArgumentOutOfRangeException>("a negative page size has no meaning");
    }

    [Test]
    public void ZeroTakeIsValidForCountOnlyQueries()
    {
        JobQuery query = new JobQuery { Take = 0, IncludeTotalCount = true };

        query.Take.Should().Be(0, "Take = 0 with IncludeTotalCount is the count-only idiom");
    }

    [Test]
    public void WithExpressionsPreservePagingAndValidate()
    {
        TriggerQuery query = new TriggerQuery { Group = GroupMatcher<TriggerKey>.GroupEquals("g"), Take = 25 };

        TriggerQuery secondPage = query with { Skip = 25 };

        secondPage.Group.Should().Be(query.Group, "with-expressions must keep the filter");
        secondPage.Take.Should().Be(25);
        secondPage.Skip.Should().Be(25);

        Action act = () => _ = query with { Skip = -5 };
        act.Should().Throw<ArgumentOutOfRangeException>("validation must also run through with-expressions");
    }

    [Test]
    public void QueriesWithSameValuesAreEqual()
    {
        TriggerQuery left = new TriggerQuery { Job = new JobKey("a", "b"), Skip = 10, Take = 5 };
        TriggerQuery right = new TriggerQuery { Job = new JobKey("a", "b"), Skip = 10, Take = 5 };

        left.Should().Be(right, "queries are value objects and must compare by value");
    }
}
