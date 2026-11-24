using System.Net;
using System.Text;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.HttpApiContract;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What a request through a target is answered when the target does not serve it (#4218, design §2.4):
/// the status says whose problem it is, the body names the target and never its address, and the host's
/// log says what failed as a class.
/// </summary>
/// <remarks>
/// Until 4.9 these were classified as a scheduler's own failures: a target's <c>403</c> reached the caller
/// as a <c>400</c>, and a target that was not there as a <c>500</c>.
/// </remarks>
public sealed class GatewayTargetFaultTest
{
    private const string Target = "w1";
    private const string Address = "worker-7.internal:8443";
    private const string SchedulerUrl = "schedulers/" + TestData.SchedulerName;

    private readonly RecordingLoggerProvider logs = new();

    private WebApplication gateway = null!;
    private HttpClient client = null!;
    private HttpClient toTarget = null!;
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer = null!;

    [TearDown]
    public async Task TearDown()
    {
        client?.Dispose();
        toTarget?.Dispose();
        if (gateway is not null)
        {
            await gateway.DisposeAsync();
        }

        logs.Clear();
    }

    [Test]
    public async Task ATargetNothingAnswersForIsUnavailableAndItsAddressStaysInTheHost()
    {
        await StartGateway((_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, $"No connection could be made because the target machine actively refused it. ({Address})"));

        using HttpResponseMessage response = await client.GetAsync($"{SchedulerUrl}?target={Target}");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"Target '{Target}' is not reachable.").And.NotContain("worker-7", "a target's address is the host's business");
        body.Should().NotContain(HttpApiConstants.ProblemDetailsExceptionType, "nothing the caller could rethrow failed");

        RecordedLogEntry line = logs.WithEventId(9010).Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Warning);
        line.Properties["Reason"].Should().Be("HttpRequestException (ConnectionError)");
        logs.Entries.Should().NotContain(entry => entry.Message.Contains("worker-7", StringComparison.Ordinal),
            "the log says what failed as a class, and never where the target is");
    }

    /// <summary>
    /// "Not reachable" is said only when nothing was sent. A failure after the request went — an answer that
    /// ended early, one that was not HTTP — may have followed a change the target made, so it is a bad gateway,
    /// and a change says it may have been applied: a retry of a trigger-now that ran would fire it twice.
    /// </summary>
    [TestCase(HttpRequestError.ConnectionError, HttpStatusCode.ServiceUnavailable, false)]
    [TestCase(HttpRequestError.NameResolutionError, HttpStatusCode.ServiceUnavailable, false)]
    [TestCase(HttpRequestError.SecureConnectionError, HttpStatusCode.ServiceUnavailable, false)]
    [TestCase(HttpRequestError.ResponseEnded, HttpStatusCode.BadGateway, true)]
    [TestCase(HttpRequestError.InvalidResponse, HttpStatusCode.BadGateway, true)]
    [TestCase(HttpRequestError.HttpProtocolError, HttpStatusCode.BadGateway, true)]
    [TestCase(HttpRequestError.Unknown, HttpStatusCode.BadGateway, true)]
    public async Task OnlyAFailureBeforeTheRequestWasSentIsNotReachable(HttpRequestError error, HttpStatusCode status, bool mayHaveApplied)
    {
        await StartGateway((_, _) => throw new HttpRequestException(error, $"The connection to {Address} failed."));

        using HttpResponseMessage change = await client.PostAsync($"{SchedulerUrl}/jobs/reports/nightly/trigger?target={Target}", content: null);
        using HttpResponseMessage read = await client.GetAsync($"{SchedulerUrl}?target={Target}");

        change.StatusCode.Should().Be(status);
        string changeBody = await change.Content.ReadAsStringAsync();
        if (mayHaveApplied)
        {
            changeBody.Should().Contain($"Target '{Target}' failed.").And.Contain(TargetFaultException.MayHaveAppliedSentence,
                "the request went, so the trigger-now may have run and a retry would fire it again");
        }
        else
        {
            changeBody.Should().Contain($"Target '{Target}' is not reachable.").And.NotContain("may or may not", "nothing was sent");
        }

        read.StatusCode.Should().Be(status);
        (await read.Content.ReadAsStringAsync()).Should().NotContain("may or may not", "a read changed nothing").And.NotContain("worker-7");
    }

    [Test]
    public async Task ATargetThatRefusesTheHostsCredentialIsABadGateway()
    {
        await StartGateway((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        using HttpResponseMessage response = await client.PostAsync($"{SchedulerUrl}/pause-all?target={Target}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway, "the caller's credential was fine: the host's was not");
        (await response.Content.ReadAsStringAsync()).Should().Contain($"Target '{Target}' refused this host's credential.");
        RecordedLogEntry line = logs.WithEventId(9014).Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Error);
        line.Properties["Fault"].Should().Be(nameof(TargetFault.CredentialRefused));
    }

    [Test]
    public async Task ATargetThatRefusesTheOperationIsForbiddenWithItsOwnDetail()
    {
        await StartGateway((_, _) => Problem(HttpStatusCode.Forbidden, $"Not authorized for PauseAll on scheduler {TestData.SchedulerName}: it needs Operate"));

        using HttpResponseMessage response = await client.PostAsync($"{SchedulerUrl}/pause-all?target={Target}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a rule at the target said no; until 4.9 this was a 400");
        (await response.Content.ReadAsStringAsync()).Should()
            .Contain($"Target '{Target}' refused: Not authorized for PauseAll on scheduler {TestData.SchedulerName}: it needs Operate");
        logs.WithEventId(9012).Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
        logs.WithEventId(9014).Should().BeEmpty("a refusal is the target's rule, not a failure");
    }

    [Test]
    public async Task ATargetsFaultIsABadGatewayWithoutTheTargetsText()
    {
        await StartGateway((_, _) => Problem(HttpStatusCode.InternalServerError, "Login failed for user 'quartz' on Server=db-1.internal"));

        using HttpResponseMessage response = await client.GetAsync($"{SchedulerUrl}?target={Target}");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"Target '{Target}' failed.").And.NotContain("db-1", "a fault's text may carry the target's internals");
        logs.WithEventId(9014).Should().ContainSingle().Which.Properties["Fault"].Should().Be(nameof(TargetFault.Failed));
    }

    [Test]
    public async Task ATargetThatDoesNotAnswerInTimeIsAGatewayTimeoutAndAChangeMayHaveHappened()
    {
        await StartGateway(
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            timeout: TimeSpan.FromMilliseconds(300));

        using HttpResponseMessage read = await client.GetAsync($"{SchedulerUrl}?target={Target}");
        using HttpResponseMessage change = await client.PostAsync($"{SchedulerUrl}/pause-all?target={Target}", content: null);

        read.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout, "the transport's timeout is the gateway's: there is no second one");
        string readBody = await read.Content.ReadAsStringAsync();
        readBody.Should().Contain($"Target '{Target}' did not answer in time.")
            .And.NotContain("may or may not", "a read changed nothing, so there is nothing to check before retrying");

        change.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        (await change.Content.ReadAsStringAsync()).Should().Contain(TargetFaultException.MayHaveAppliedSentence,
            "a change that reached the target before the answer was lost may have been applied");

        logs.WithEventId(9011).Should().HaveCount(2);
    }

    [Test]
    public async Task ASchedulersOwnRefusalAtTheTargetKeepsItsExceptionType()
    {
        await StartGateway((_, _) => Problem(HttpStatusCode.BadRequest, "Trigger 'reports.nightly' is not paused", nameof(SchedulerException)));

        using HttpResponseMessage response = await client.PostAsync($"{SchedulerUrl}/pause-all?target={Target}", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Trigger 'reports.nightly' is not paused").And.Contain(nameof(SchedulerException),
            "the target's scheduler raised it, and the round trip it always had still rethrows it as itself");
    }

    [Test]
    public async Task ARouteAnOlderTargetDoesNotServeIsNotServed()
    {
        await StartGateway((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        using HttpResponseMessage response = await client.GetAsync($"{SchedulerUrl}/nodes?target={Target}");

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented,
            "a target older than the route answers 404 without problem details, which is a route it does not serve rather than a fault here");
        (await response.Content.ReadAsStringAsync()).Should().Contain(nameof(NotSupportedException));
    }

    private async Task StartGateway(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> targetAnswers, TimeSpan? timeout = null)
    {
        answer = targetAnswers;
        toTarget = new HttpClient(new StubHandler(this)) { BaseAddress = new Uri($"https://{Address}/quartz-api/") };
        if (timeout is { } given)
        {
            toTarget.Timeout = given;
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Debug);
        builder.Services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = TestData.SchedulerName;
            options.Target = Target;
            options.CreateHttpClient = _ => toTarget;
        });
        builder.Services.AddQuartzHttpApi();

        gateway = builder.Build();
        gateway.MapQuartzHttpApi("/quartz-api").AllowAnonymous();
        await gateway.StartAsync();

        client = gateway.GetTestClient();
        client.BaseAddress = new Uri(client.BaseAddress!, "quartz-api/");
    }

    private static Task<HttpResponseMessage> Problem(HttpStatusCode status, string detail, string? exceptionType = null)
    {
        string type = exceptionType is null ? "" : $""", "{HttpApiConstants.ProblemDetailsExceptionType}": "{exceptionType}" """;
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent($$"""{ "status": {{(int) status}}, "detail": "{{detail}}"{{type}} }""", Encoding.UTF8, "application/problem+json")
        });
    }

    private sealed class StubHandler(GatewayTargetFaultTest test) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return test.answer(request, cancellationToken);
        }
    }
}
