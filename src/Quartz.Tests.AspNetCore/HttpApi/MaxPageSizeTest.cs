using System.Net;

using FakeItEasy;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What <see cref="QuartzHttpApiOptions.MaxPageSize" /> does to a paged request.
/// </summary>
/// <remarks>
/// A page size is the only thing on this API a caller can use to make the server do arbitrary work, and
/// nothing bounded it before 4.0.0-beta.1 — one request could materialize every trigger in the store
/// while the bulk key fetch next door refused a thousand and one keys. The two spellings are answered
/// differently and that is the whole design: a number the server will not do is refused, and
/// <c>all</c> — which says "as many as you will give me" — is answered with as many as it will.
/// </remarks>
public sealed class MaxPageSizeTest
{
    private readonly List<WebApplicationFactory<Program>> factories = [];

    [TearDown]
    public async Task TearDown()
    {
        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
    }

    [Test]
    public async Task ATakeAboveTheDefaultCapIsRefusedWithTheCapAndTheSpellingThatWouldWork()
    {
        using HttpClient client = ApiWith(maxPageSize: null, out _);

        using HttpResponseMessage response = await client.GetAsync(JobsUrl + "?take=2000");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("1000", "the refusal has to name the limit the caller is over");
        body.Should().Contain("MaxPageSize", "and the setting that would raise it");
    }

    [Test]
    public async Task ATakeAboveTheDefaultCapIsAnsweredOnceTheCapIsRaised()
    {
        using HttpClient client = ApiWith(maxPageSize: 5000, out _);

        using HttpResponseMessage response = await client.GetAsync(JobsUrl + "?take=2000");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ATakeUnderTheCapIsUntouched()
    {
        using HttpClient client = ApiWith(maxPageSize: null, out IScheduler scheduler);

        using HttpResponseMessage response = await client.GetAsync(JobsUrl + "?take=7");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        A.CallTo(() => scheduler.QueryJobs(A<JobQuery>.That.Matches(query => query.Take == 7), A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task AskingForEverythingIsBoundedByTheCapRatherThanRefusedByIt()
    {
        using HttpClient client = ApiWith(maxPageSize: 3, out IScheduler scheduler);

        using HttpResponseMessage response = await client.GetAsync(JobsUrl + "?take=all");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "'all' is what every SchedulerQueryExtensions listing asks for through HttpScheduler, three "
            + "matches or three million, so refusing it would refuse a three-job listing");
        A.CallTo(() => scheduler.QueryJobs(A<JobQuery>.That.Matches(query => query.Take == 3), A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    [Test]
    public async Task AskingForEverythingWithNoCapStillReachesTheSchedulerAsEverything()
    {
        using HttpClient client = ApiWith(maxPageSize: 0, out IScheduler scheduler);

        using HttpResponseMessage response = await client.GetAsync(JobsUrl + $"?take={int.MaxValue}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "zero is the unbounded opt-out");
        A.CallTo(() => scheduler.QueryJobs(A<JobQuery>.That.Matches(query => query.Take == PagedQuery.All), A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);
    }

    /// <summary>
    /// A bounded page handed back as if it were everything would read as the whole store.
    /// </summary>
    [Test]
    public async Task TheClientRefusesATruncatedAnswerToARequestForEverything()
    {
        using HttpClient client = ApiWith(maxPageSize: 1, out IScheduler scheduler);
        A.CallTo(() => scheduler.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobHeader>([HeaderFor("one")], HasMore: true));

        HttpScheduler remote = new(TestData.SchedulerName, client);

        Func<Task> act = async () => await remote.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());

        await act.Should().ThrowAsync<HttpClientException>()
            .WithMessage("*MaxPageSize*",
                "the 3.x-compatible listings answer with a bare list, so a page short of the matches has "
                + "to be a throw rather than a shorter list");
    }

    [Test]
    public async Task TheCompatListingIsAnsweredWholeWhenItFitsUnderTheCap()
    {
        using HttpClient client = ApiWith(maxPageSize: null, out IScheduler scheduler);
        A.CallTo(() => scheduler.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobHeader>([HeaderFor("one"), HeaderFor("two")], HasMore: false));

        HttpScheduler remote = new(TestData.SchedulerName, client);

        List<JobKey> keys = await remote.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());

        keys.Should().HaveCount(2,
            "a listing whose matches fit under the cap answers exactly as it would with no cap at all");
    }

    [Test]
    public void ANegativeCapIsRefusedAtStartup()
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        WebApplicationFactory<Program> configured = root.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => services.AddQuartzHttpApi(options => options.MaxPageSize = -1)));
        factories.Add(configured);

        Action act = () => configured.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxPageSize*",
            "a cap nobody can be under is a setting that silently refuses everything");
    }

    private const string JobsUrl = "schedulers/" + TestData.SchedulerName + "/jobs";

    private HttpClient ApiWith(int? maxPageSize, out IScheduler scheduler)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        WebApplicationFactory<Program> configured = maxPageSize is null
            ? root
            : root.WithWebHostBuilder(builder => builder.ConfigureServices(
                services => services.AddQuartzHttpApi(options => options.MaxPageSize = maxPageSize.Value)));
        factories.Add(configured);

        IScheduler fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.SchedulerName);
        A.CallTo(() => fake.QueryJobs(A<JobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobHeader>([], HasMore: false));
        scheduler = fake;

        HttpClient client = configured.CreateClient();

        ISchedulerRepository repository = configured.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        repository.Bind(fake);
        return client;
    }

    private static JobHeader HeaderFor(string name) => new(
        new JobKey(name, "group"),
        Description: null,
        JobTypeName: "Quartz.Job.NoOpJob, Quartz",
        Durable: false,
        ConcurrentExecutionDisallowed: false,
        PersistJobDataAfterExecution: false,
        RequestsRecovery: false);
}
