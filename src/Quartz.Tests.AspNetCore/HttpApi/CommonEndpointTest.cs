using System.Net;
using System.Text;

using FakeItEasy;

using FluentAssertions;

using Quartz.HttpClient;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

public class CommonEndpointTest : WebApiTest
{
    [Test]
    public void HttpSchedulerShouldThrowIfSchedulerIsNotFound()
    {
        var nonExistingHttpScheduler = new HttpScheduler(TestData.SchedulerName + "_non_existing", WebApplicationFactory.CreateClient());
        Assert.ThrowsAsync<HttpClientException>(() => nonExistingHttpScheduler.GetMetaData().AsTask())!.Message.Should().ContainEquivalentOf("Scheduler not found");

        // Getting non existing job returns null, but should throw if scheduler is not found
        Assert.ThrowsAsync<HttpClientException>(() => nonExistingHttpScheduler.GetJobDetail(new JobKey("non", "existing")).AsTask())!.Message.Should().ContainEquivalentOf("Scheduler not found");
    }

    [Test]
    public void ShouldPropagateSchedulerExceptions()
    {
        A.CallTo(() => FakeScheduler.Start(A<CancellationToken>._)).Throws(_ => new SchedulerException("Test exception"));
        A.CallTo(() => FakeScheduler.Standby(A<CancellationToken>._)).Throws(_ => new JobExecutionException("Second test exception"));

        Assert.ThrowsAsync<SchedulerException>(() => HttpScheduler.Start().AsTask())!.Message.Should().ContainEquivalentOf("Test exception");
        Assert.ThrowsAsync<JobExecutionException>(() => HttpScheduler.Standby().AsTask())!.Message.Should().ContainEquivalentOf("Second test exception");
    }

    [Test]
    public void ShouldNotPropagateNonSchedulerExceptions()
    {
        A.CallTo(() => FakeScheduler.PauseAll(A<CancellationToken>._)).Throws(_ => new InvalidOperationException("Non scheduler exception"));
        Assert.ThrowsAsync<HttpClientException>(() => HttpScheduler.PauseAll().AsTask())!.Message.Should().ContainEquivalentOf("Non scheduler exception");
    }

    [Test]
    public async Task ShouldReturnBadRequestIfRequestJsonIsInvalid()
    {
        using var httpClient = WebApplicationFactory.CreateClient();

        await RunTest("");
        await RunTest("{}");
        await RunTest(@"{""CalendarName"": ""SomeCalendar""}"); // Missing calendar

        // Valid request except missing calendar type which is required by CalendarConverter
        const string requestJson = @"{
    ""CalendarName"": ""SomeCalendar"",
    ""Replace"": true,
    ""UpdateTriggers"": true,
    ""Calendar"": {
        ""Description"": ""My new and shiny calendar""
    }
}";

        var responseContent = await RunTest(requestJson);
        responseContent.Should().ContainEquivalentOf("Failed to parse ICalendar");

        async Task<string> RunTest(string contentToPost)
        {
            var response = await httpClient.PostAsync($"schedulers/{HttpScheduler.SchedulerName}/calendars", new StringContent(contentToPost, Encoding.UTF8, "application/json"));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            return await response.Content.ReadAsStringAsync();
        }
    }
}