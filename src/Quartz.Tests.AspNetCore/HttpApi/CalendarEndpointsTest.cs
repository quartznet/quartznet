using FakeItEasy;

using FluentAssertions;
using FluentAssertions.Execution;

using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

public class CalendarEndpointsTest : WebApiTest
{
    [Test]
    public async Task GetCalendarNamesShouldWork()
    {
        A.CallTo(() => FakeScheduler.GetCalendarNames(A<CancellationToken>._)).Returns(["Calendar 1", "Calendar 2"]);

        var calendarNames = await HttpScheduler.GetCalendarNames();
        using (new AssertionScope())
        {
            calendarNames.Count.Should().Be(2);
            calendarNames.Should().ContainSingle(x => x == "Calendar 1");
            calendarNames.Should().ContainSingle(x => x == "Calendar 2");
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
        await HttpScheduler.AddCalendar("MyNewCalendar", TestData.DailyCalendar, true, false);

        A.CallTo(() => FakeScheduler.AddCalendar(A<string>._, A<ICalendar>._, A<bool>._, A<bool>._, A<CancellationToken>._))
            .WhenArgumentsMatch((string name, ICalendar calendar, bool replace, bool updateTriggers, CancellationToken _) =>
                name == "MyNewCalendar" &&
                replace == true &&
                updateTriggers == false &&
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
}