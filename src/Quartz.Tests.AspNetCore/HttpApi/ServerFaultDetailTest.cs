using System.Net;

using FakeItEasy;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What a <c>500</c> says, and what it stops saying.
/// </summary>
/// <remarks>
/// The handler's own rule — a fault the caller cannot act on names nothing about this server — held for
/// the exception's type and not for its message, and a driver message names the server, the database,
/// the login or the constraint as readily as anything else.
/// <see cref="QuartzHttpApiOptions.IncludeStackTraceInProblemDetails" />, the switch that already says
/// "I am debugging this", is what puts it back.
/// </remarks>
public sealed class ServerFaultDetailTest
{
    private const string Secret = "Login failed for user 'quartz' on server db-prod-01";

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
    public async Task AFaultWithheldTheMessageAndSaysWhereItWent()
    {
        using HttpClient client = ApiWith(includeStackTrace: false);

        string body = await PauseAll(client);

        body.Should().NotContain("db-prod-01", "the caller can do nothing with the server's name");
        body.Should().Contain("recorded in the server's log",
            "and has to be told where the answer is instead of being told nothing");
    }

    [Test]
    public async Task AFaultKeepsItsMessageWhileDebugging()
    {
        using HttpClient client = ApiWith(includeStackTrace: true);

        string body = await PauseAll(client);

        body.Should().Contain("db-prod-01",
            "IncludeStackTraceInProblemDetails is the existing 'I am debugging this' switch, and one "
            + "switch for both halves of the same disclosure is one thing to get wrong rather than two");
    }

    private static async Task<string> PauseAll(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsync(
            $"schedulers/{TestData.SchedulerName}/pause-all", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        return await response.Content.ReadAsStringAsync();
    }

    private HttpClient ApiWith(bool includeStackTrace)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        WebApplicationFactory<Program> configured = root.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => services.AddQuartzHttpApi(options => options.IncludeStackTraceInProblemDetails = includeStackTrace)));
        factories.Add(configured);

        IScheduler fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.SchedulerName);
        A.CallTo(() => fake.PauseAll(A<CancellationToken>._)).Throws(_ => new InvalidOperationException(Secret));

        HttpClient client = configured.CreateClient();

        ISchedulerRepository repository = configured.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        repository.Bind(fake);
        return client;
    }
}
