using AwesomeAssertions.Execution;
using FakeItEasy;

using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

public class CalendarEndpointsTest : WebApiTest
{
    [Test]
    public async Task GetCalendarNamesShouldWork()
    {
        A.CallTo(() => FakeScheduler.QueryCalendarNames(A<CalendarQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<string>(["Calendar 1", "Calendar 2"], HasMore: false));

        var calendarNames = await HttpScheduler.GetCalendarNames();
        using (new AssertionScope())
        {
            calendarNames.Count.Should().Be(2);
            calendarNames.Should().ContainSingle(x => x == "Calendar 1");
            calendarNames.Should().ContainSingle(x => x == "Calendar 2");
        }

        // the compat listing asks for everything, and the server bounds it to QuartzHttpApiOptions.MaxPageSize
        A.CallTo(() => FakeScheduler.QueryCalendarNames(new CalendarQuery { Take = QuartzHttpApiOptions.DefaultMaxPageSize }, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task QueryCalendarNamesShouldPassPaging()
    {
        A.CallTo(() => FakeScheduler.QueryCalendarNames(A<CalendarQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<string>(["Calendar 2"], HasMore: true, TotalCount: 3));

        var query = new CalendarQuery { Skip = 1, Take = 1, IncludeTotalCount = true };
        var result = await HttpScheduler.QueryCalendarNames(query);

        using (new AssertionScope())
        {
            result.Items.Should().ContainSingle().Which.Should().Be("Calendar 2");
            result.HasMore.Should().BeTrue();
            result.TotalCount.Should().Be(3);
        }

        A.CallTo(() => FakeScheduler.QueryCalendarNames(query, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task QueryCalendarNamesShouldPassTheNameFilter()
    {
        NameMatcher[] matchers =
        [
            NameMatcher.NameEquals("equals"),
            NameMatcher.NameStartsWith("starts"),
            NameMatcher.NameEndsWith("ends"),
            NameMatcher.NameContains("contains")
        ];

        foreach (NameMatcher matcher in matchers)
        {
            Fake.ClearRecordedCalls(FakeScheduler);
            A.CallTo(() => FakeScheduler.QueryCalendarNames(A<CalendarQuery>._, A<CancellationToken>._))
                .Returns(new PagedResult<string>(["Calendar 1"], HasMore: false));

            var query = new CalendarQuery { Name = matcher };
            var result = await HttpScheduler.QueryCalendarNames(query);

            result.Items.Should().ContainSingle().Which.Should().Be("Calendar 1");
            A.CallTo(() => FakeScheduler.QueryCalendarNames(query, A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
    }

    [Test]
    public async Task GetCalendarShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetCalendar("AnnualCalendar", A<CancellationToken>._)).Returns(TestData.AnnualCalendar);
        A.CallTo(() => FakeScheduler.GetCalendar("CronCalendar", A<CancellationToken>._)).Returns(TestData.CronCalendar);
        A.CallTo(() => FakeScheduler.GetCalendar("DailyCalendar", A<CancellationToken>._)).Returns(TestData.DailyCalendar);
        A.CallTo(() => FakeScheduler.GetCalendar("HolidayCalendar", A<CancellationToken>._)).Returns(TestData.HolidayCalendar);
        A.CallTo(() => FakeScheduler.GetCalendar("MonthlyCalendar", A<CancellationToken>._)).Returns(TestData.MonthlyCalendar);
        A.CallTo(() => FakeScheduler.GetCalendar("WeeklyCalendar", A<CancellationToken>._)).Returns(TestData.WeeklyCalendar);
        A.CallTo(() => FakeScheduler.GetCalendar("NonExistingCalendar", A<CancellationToken>._)).Returns(null);

        var calendar = await HttpScheduler.GetCalendar("AnnualCalendar");
        calendar.Should().BeEquivalentTo(TestData.AnnualCalendar);

        calendar = await HttpScheduler.GetCalendar("CronCalendar");
        calendar.Should().BeEquivalentTo(TestData.CronCalendar);

        calendar = await HttpScheduler.GetCalendar("DailyCalendar");
        calendar.Should().BeEquivalentTo(TestData.DailyCalendar);

        calendar = await HttpScheduler.GetCalendar("HolidayCalendar");
        calendar.Should().BeEquivalentTo(TestData.HolidayCalendar);

        calendar = await HttpScheduler.GetCalendar("MonthlyCalendar");
        calendar.Should().BeEquivalentTo(TestData.MonthlyCalendar);

        calendar = await HttpScheduler.GetCalendar("WeeklyCalendar");
        calendar.Should().BeEquivalentTo(TestData.WeeklyCalendar);

        calendar = await HttpScheduler.GetCalendar("NonExistingCalendar");
        calendar.Should().BeNull();
    }

    [Test]
    public async Task AddCalendarShouldWork()
    {
        await HttpScheduler.AddCalendar("MyNewCalendar", TestData.DailyCalendar, new AddCalendarOptions { Replace = true });

        A.CallTo(() => FakeScheduler.AddCalendar(A<string>._, A<ICalendar>._, A<AddCalendarOptions>._, A<CancellationToken>._))
            .WhenArgumentsMatch((string name, ICalendar calendar, AddCalendarOptions options, CancellationToken _) =>
                name == "MyNewCalendar" &&
                options.Replace &&
                !options.UpdateTriggers &&
                calendar is DailyCalendar dailyCalendar &&
                dailyCalendar.TimeZone.Id == TestData.DailyCalendar.TimeZone.Id &&
                dailyCalendar.Description == TestData.DailyCalendar.Description &&
                dailyCalendar.InvertTimeRange == TestData.DailyCalendar.InvertTimeRange &&
                dailyCalendar.CalendarBase?.Description == TestData.DailyCalendar.CalendarBase?.Description
            )
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task DeleteCalendarShouldWork()
    {
        await HttpScheduler.DeleteCalendar("MyOldCalendar");
        A.CallTo(() => FakeScheduler.DeleteCalendar("MyOldCalendar", A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
    }

    /// <summary>
    /// The calendar existence route mirrors the job and trigger ones, and — the point of the member —
    /// it asks the scheduler the existence question rather than fetching the calendar to look at it.
    /// </summary>
    [Test]
    public async Task CalendarExistsShouldRoundTripTheAnswerWithoutFetchingTheCalendar()
    {
        A.CallTo(() => FakeScheduler.Exists("AnnualCalendar", A<CancellationToken>._)).Returns(true);
        A.CallTo(() => FakeScheduler.Exists("NonExistingCalendar", A<CancellationToken>._)).Returns(false);

        (await HttpScheduler.Exists("AnnualCalendar")).Should().BeTrue();
        (await HttpScheduler.Exists("NonExistingCalendar")).Should().BeFalse();

        A.CallTo(() => FakeScheduler.GetCalendar(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public async Task CalendarExistsShouldRejectABlankNameBeforeTheRequestIsMade()
    {
        Func<Task> blank = async () => await HttpScheduler.Exists("  ");

        await blank.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("calendarName", "a blank name would produce a URL that means a different route");
    }
}