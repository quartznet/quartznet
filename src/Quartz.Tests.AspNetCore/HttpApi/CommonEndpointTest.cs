using System.Net;
using System.Text;

using FakeItEasy;

using FluentAssertions;

using NUnit.Framework;

using Quartz.HttpClient;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

public class CommonEndpointTest : WebApiTest
{
    [Test]
    public void HttpSchedulerShouldThrowIfSchedulerIsNotFound()
    {
        var nonExistingHttpScheduler = new HttpScheduler(TestData.SchedulerName + "_non_existing", WebApplicationFactory.CreateClient());
        Assert.ThrowsAsync<HttpClientException>(() => nonExistingHttpScheduler.GetMetaData())!.Message.Should().ContainEquivalentOf("Scheduler not found");

        // Getting non existing job returns null, but should throw if scheduler is not found
        Assert.ThrowsAsync<HttpClientException>(() => nonExistingHttpScheduler.GetJobDetail(new JobKey("non", "existing")))!.Message.Should().ContainEquivalentOf("Scheduler not found");
    }

    [Test]
    public void ShouldPropagateSchedulerExceptions()
    {
        A.CallTo(() => FakeScheduler.Start(A<CancellationToken>._)).Throws(_ => new SchedulerException("Test exception"));
        A.CallTo(() => FakeScheduler.Standby(A<CancellationToken>._)).Throws(_ => new JobExecutionException("Second test exception"));

        Assert.ThrowsAsync<SchedulerException>(() => HttpScheduler.Start())!.Message.Should().ContainEquivalentOf("Test exception");
        Assert.ThrowsAsync<JobExecutionException>(() => HttpScheduler.Standby())!.Message.Should().ContainEquivalentOf("Second test exception");
    }

    [Test]
    public void ShouldNotPropagateNonSchedulerExceptions()
    {
        A.CallTo(() => FakeScheduler.PauseAll(A<CancellationToken>._)).Throws(_ => new Exception("Non scheduler exception"));
        Assert.ThrowsAsync<HttpClientException>(() => HttpScheduler.PauseAll())!.Message.Should().ContainEquivalentOf("Non scheduler exception");
    }

    [Test]
    public async Task ShouldReturnBadRequestIfRequestJsonIsInvalid()
    {
        using var httpClient = WebApplicationFactory.CreateClient();

        // TODO: This requires adding validation into the endpoints as minimal APIs does not have ModelState
        // Completely empty request
        //var response = await Post("{}");
        //response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Valid request except missing calendar type which is required by CalendarConverter
        const string requestJson = @"{
    ""CalendarName"": ""SomeCalendar"",
    ""Replace"": true,
    ""UpdateTriggers"": true,
    ""Calendar"": {
        ""Description"": ""My new and shiny calendar""
    }
}";

        var response = await Post(requestJson);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var stringContent = await response.Content.ReadAsStringAsync();
        stringContent.Should().ContainEquivalentOf("Failed to parse ICalendar");

        Task<HttpResponseMessage> Post(string contentToPost)
        {
            return httpClient.PostAsync($"schedulers/{HttpScheduler.SchedulerName}/calendars", new StringContent(contentToPost, Encoding.UTF8, "application/json"));
        }
    }
}