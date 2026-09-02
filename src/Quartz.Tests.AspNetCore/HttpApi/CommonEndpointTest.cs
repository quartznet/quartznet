using System.Net;
using System.Text;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

public class CommonEndpointTest : WebApiTest
{
    [Test]
    public void HttpSchedulerShouldThrowIfSchedulerIsNotFound()
    {
        var nonExistingHttpScheduler = new HttpScheduler(TestData.SchedulerName + "_non_existing", WebApplicationFactory.CreateClient());
        Assert.ThrowsAsync<HttpClientException>(() => nonExistingHttpScheduler.GetMetadata().AsTask())!.Message.Should().ContainEquivalentOf("Scheduler not found");

        // Getting non existing job returns null, but should throw if scheduler is not found
        Assert.ThrowsAsync<HttpClientException>(() => nonExistingHttpScheduler.GetJobDetail(new JobKey("non", "existing")).AsTask())!.Message.Should().ContainEquivalentOf("Scheduler not found");
    }

    /// <summary>
    /// Clearing the execution limits reports an unknown scheduler name the way every other member does.
    /// </summary>
    /// <remarks>
    /// It is the one member whose request carries a body in neither direction, which is how it came to
    /// call <c>HttpClient.DeleteAsync</c> raw: <c>EnsureSuccessStatusCode</c> raises
    /// <see cref="HttpRequestException" /> without reading the problem details, so the 404 that says
    /// which scheduler was not found arrived as a bare "404 (Not Found)".
    /// </remarks>
    [Test]
    public async Task ClearingExecutionLimitsReportsAnUnknownSchedulerLikeEveryOtherMember()
    {
        HttpScheduler nonExistingHttpScheduler = new HttpScheduler(TestData.SchedulerName + "_non_existing", WebApplicationFactory.CreateClient());

        Func<Task> act = () => nonExistingHttpScheduler.SetExecutionLimits(null).AsTask();

        (await act.Should().ThrowAsync<HttpClientException>(
            "clearing the limits goes through the same checked call as every other member, so a name the repository does not hold is the wrong-name mistake it is"))
            .Which.Message.Should().ContainEquivalentOf("Scheduler not found");
    }

    /// <summary>
    /// The client turns the exception name the server sent back into that exception, for all eight names
    /// it knows.
    /// </summary>
    /// <remarks>
    /// The mapping is by type <em>name</em>, so nothing but this test relates the two lists: renaming one
    /// of these exceptions, or teaching the server to raise one the client cannot name, silently
    /// downgrades a caller's <c>catch</c> to <see cref="HttpClientException" /> — and a remote
    /// <c>ScheduleJob</c> that used to report a duplicate as
    /// <see cref="ObjectAlreadyExistsException" /> would stop doing so with nothing failing.
    /// </remarks>
    [TestCaseSource(nameof(TheExceptionsTheClientNames))]
    public async Task TheClientRethrowsEveryExceptionTheServerNames(Func<SchedulerException> create)
    {
        SchedulerException expected = create();
        A.CallTo(() => FakeScheduler.Start(A<CancellationToken>._)).Throws(_ => create());

        Func<Task> act = () => HttpScheduler.Start().AsTask();

        Exception thrown = (await act.Should().ThrowAsync<SchedulerException>()).Which;
        thrown.Should().BeOfType(expected.GetType(), "the client maps the exception name the server sent back to the type a caller catches");
        thrown.Message.Should().ContainEquivalentOf(expected.Message);
    }

    private static IEnumerable<TestCaseData> TheExceptionsTheClientNames()
    {
        yield return Case(() => new SchedulerException("the scheduler refused"));
        yield return Case(() => new InvalidConfigurationException("the configuration is invalid"));
        yield return Case(() => new JobExecutionException("the job faulted"));
        yield return Case(() => new JobPersistenceException("the store refused"));
        yield return Case(() => new SchedulerConfigException("the scheduler is misconfigured"));
        yield return Case(() => new LockException("the lock was not taken"));
        yield return Case(() => new NoSuchDelegateException("there is no such delegate"));
        yield return Case(() => new ObjectAlreadyExistsException("that one exists already"));

        static TestCaseData Case<TException>(Func<TException> create) where TException : SchedulerException
        {
            Func<SchedulerException> asBase = create;
            return new TestCaseData(asBase).SetArgDisplayNames(typeof(TException).Name);
        }
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

        string message = Assert.ThrowsAsync<HttpClientException>(() => HttpScheduler.PauseAll().AsTask())!.Message;

        message.Should().NotContainEquivalentOf("Non scheduler exception",
            "a 500 is a fault the caller cannot act on, and the message behind one names the server, the "
            + "database or the constraint as readily as anything else - it is logged, not returned");
        message.Should().ContainEquivalentOf("The scheduler failed to handle the request",
            "the client still has to be able to say what went wrong, so the detail is a fixed sentence "
            + "rather than nothing at all");
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