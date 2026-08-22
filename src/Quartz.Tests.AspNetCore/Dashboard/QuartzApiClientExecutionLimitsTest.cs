using System.Net;
using System.Text;

using FakeItEasy;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Quartz.Dashboard;
using Quartz.Dashboard.Services;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// How the dashboard's remote API client reads the execution-limits body.
/// </summary>
/// <remarks>
/// This client talks to whatever HTTP API it was pointed at, which need not be the same build, so the
/// body's shape is a contract rather than an implementation detail. The scope is the part worth
/// pinning: a limit that lost it in transit would be shown as a per-node one, which reads as a quota
/// silently multiplied by the node count.
/// </remarks>
public class QuartzApiClientExecutionLimitsTest
{
    [Test]
    public async Task GetExecutionLimitsShouldReadTheScopeOfEachLimit()
    {
        QuartzApiClient client = CreateClient("""
            {
              "limits": {
                "batch": { "maxConcurrent": 2, "scope": "Node" },
                "tenant-acme": { "maxConcurrent": 8, "scope": "Cluster" },
                "_": { "maxConcurrent": 3, "scope": "Node" },
                "free": { "maxConcurrent": null, "scope": "Node" }
              },
              "useTriggerGroupWhenUnset": false
            }
            """);

        ExecutionLimitsDto? limits = await client.GetExecutionLimits("core");

        limits.Should().NotBeNull();
        limits.Limits.Should().BeEquivalentTo(new Dictionary<string, DashboardExecutionLimit>
        {
            ["batch"] = new(2, ExecutionLimitScope.Node),
            ["tenant-acme"] = new(8, ExecutionLimitScope.Cluster),
            ["(default)"] = new(3, ExecutionLimitScope.Node),
            ["free"] = new(null, ExecutionLimitScope.Node),
        }, "the scope travels with the number, and the default bucket's wire spelling becomes a label a reader can understand");
    }

    [Test]
    public async Task GetExecutionLimitsShouldReadALimitThatOmitsItsScopeAsPerNode()
    {
        QuartzApiClient client = CreateClient("""{"limits":{"batch":{"maxConcurrent":2}}}""");

        ExecutionLimitsDto? limits = await client.GetExecutionLimits("core");

        limits.Should().NotBeNull();
        limits.Limits["batch"].Scope.Should().Be(ExecutionLimitScope.Node,
            "an omitted scope is what an execution limit has always meant, so it degrades to the safe reading rather than failing");
    }

    [Test]
    public async Task GetExecutionLimitsShouldBeNullWhenTheSchedulerLimitsNothing()
    {
        QuartzApiClient client = CreateClient("""{"limits":null}""");

        (await client.GetExecutionLimits("core")).Should().BeNull();
    }

    [Test]
    public async Task GetExecutionLimitsShouldBeNullWhenTheApiRefuses()
    {
        QuartzApiClient client = CreateClient("", HttpStatusCode.NotFound);

        (await client.GetExecutionLimits("core")).Should().BeNull(
            "a dashboard pointed at an API that does not answer shows nothing, rather than throwing on a page load");
    }

    private static QuartzApiClient CreateClient(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        IHttpClientFactory factory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => factory.CreateClient(A<string>._))
            .ReturnsLazily(() => new HttpClient(new CannedResponseHandler(body, status)));

        return new QuartzApiClient(
            factory,
            A.Fake<IHttpContextAccessor>(),
            Options.Create(new QuartzDashboardOptions { BaseUrl = new Uri("http://quartz.test/") }),
            new DashboardSerializerOptions(new SystemTextJsonSerializerRegistry()));
    }

    /// <summary>
    /// Answers every request with one prepared body, so the client's reading is what the test is about
    /// rather than any part of the transport.
    /// </summary>
    private sealed class CannedResponseHandler : HttpMessageHandler
    {
        private readonly string body;
        private readonly HttpStatusCode status;

        public CannedResponseHandler(string body, HttpStatusCode status)
        {
            this.body = body;
            this.status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
