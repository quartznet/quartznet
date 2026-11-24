#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// A running firing's log over HTTP (#4437): the <c>…/jobs/fire-instances/{fireInstanceId}/log</c> route, read in
/// the host's process where the firing runs, and <see cref="HttpScheduler" />'s read of it.
/// </summary>
[NonParallelizable]
public sealed class FireInstanceLogOverHttpTest
{
    private const string Node = "node-h";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(20);
    private static readonly JobKey jobKey = new("held", "running-log");

    private readonly List<WebApplicationFactory<Program>> factories = [];
    private readonly List<IScheduler> schedulers = [];

    [SetUp]
    public void SetUp()
    {
        HeldLoggingJob.Reset();
    }

    [TearDown]
    public async Task TearDown()
    {
        HeldLoggingJob.Release.TrySetResult();
        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        schedulers.Clear();

        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
    }

    [Test]
    public async Task TheRouteAnswersTheLinesTheFiringLoggedInTheShapeItDocuments()
    {
        Api api = await StartApi();

        using HttpResponseMessage response = await api.Client.GetAsync(api.LogPath());
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using JsonDocument answer = JsonDocument.Parse(body);
        answer.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal(
            ["status", "schedulerInstanceId", "entries", "next", "missed", "hasMore"]);
        answer.RootElement.GetProperty("status").GetString().Should().Be("Running", "the state travels as its name");
        answer.RootElement.GetProperty("schedulerInstanceId").GetString().Should().Be(Node);
        answer.RootElement.GetProperty("entries").EnumerateArray().Select(entry => Message(entry.GetString()!))
            .Should().Equal(["line 1", "line 2", "line 3"]);
        answer.RootElement.GetProperty("next").GetInt64().Should().Be(3);
        answer.RootElement.GetProperty("missed").GetInt64().Should().Be(0);
        answer.RootElement.GetProperty("hasMore").GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task AfterAndTakePageThroughTheLines()
    {
        Api api = await StartApi();

        using JsonDocument rest = await api.Read("?after=1");
        rest.RootElement.GetProperty("entries").EnumerateArray().Select(entry => Message(entry.GetString()!))
            .Should().Equal(["line 2", "line 3"], "the reader had one entry");
        rest.RootElement.GetProperty("next").GetInt64().Should().Be(3);

        using JsonDocument first = await api.Read("?take=1");
        first.RootElement.GetProperty("entries").GetArrayLength().Should().Be(1);
        first.RootElement.GetProperty("next").GetInt64().Should().Be(1);
        first.RootElement.GetProperty("hasMore").GetBoolean().Should().BeTrue("two more lines are kept");
    }

    [TestCase("?take=0", "take must be from 1 to 1000, was 0")]
    [TestCase("?take=1001", "take must be from 1 to 1000, was 1001")]
    [TestCase("?after=-1", "after must be 0 or more, was -1")]
    public async Task ACursorOrAPageSizeOutOfRangeIsA400(string query, string detail)
    {
        Api api = await StartApi(runJob: false);

        using HttpResponseMessage response = await api.Client.GetAsync(api.LogPath("any") + query);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        using JsonDocument problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("detail").GetString().Should().Be(detail, "the refusal says what to fix");
        problem.RootElement.GetProperty(HttpApiConstants.ProblemDetailsExceptionType).GetString()
            .Should().Be(HttpApiConstants.RequestRefusedExceptionType);
    }

    [Test]
    public async Task AFiringTheHostDoesNotRunIsNotRunningUnderTheHostsNode()
    {
        Api api = await StartApi(runJob: false);

        using JsonDocument answer = await api.Read(firing: "ended-long-ago");

        answer.RootElement.GetProperty("status").GetString().Should().Be("NotRunning");
        answer.RootElement.GetProperty("schedulerInstanceId").GetString().Should().Be(Node,
            "the node that answered, which tells a firing that ended from one that runs elsewhere");
    }

    [Test]
    public async Task AnUnknownSchedulerIsA404()
    {
        Api api = await StartApi(runJob: false);

        using HttpResponseMessage response = await api.Client.GetAsync("schedulers/no-such-scheduler/jobs/fire-instances/any/log");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A read, so a viewer may: the lines are the ones Execution Detail shows a viewer once the run ends. A caller
    /// with no role is refused before anything reaches the scheduler.
    /// </summary>
    [TestCase(RoleAuthenticationExtensions.Viewer, HttpStatusCode.OK)]
    [TestCase(null, HttpStatusCode.Forbidden)]
    public async Task TheRouteNeedsTheViewRole(string? role, HttpStatusCode expected)
    {
        Api api = await StartApi(policy: RoleAuthenticationExtensions.OperatorRolesPolicy);
        using HttpClient client = api.Factory.CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(RoleAuthenticationHandler.RoleHeaderName, role);
        }

        using HttpResponseMessage response = await client.GetAsync(api.LogPath());

        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task AReadOnlyApiServesTheRoute()
    {
        Api api = await StartApi(readOnly: true);

        using JsonDocument answer = await api.Read();

        answer.RootElement.GetProperty("status").GetString().Should().Be("Running",
            "reading a running firing's log changes nothing, so a read-only API serves it");
    }

    [Test]
    public async Task HttpSchedulerReadsWhatTheHostReadsInProcess()
    {
        Api api = await StartApi();
        await using HttpScheduler client = new(api.SchedulerName, api.Client);
        FireInstanceLogRequest request = new(api.Firing!, After: 1, Take: 10, SchedulerInstanceId: Node);

        FireInstanceLog overHttp = await FireInstanceLogs.Read(client, request);
        FireInstanceLog inProcess = await FireInstanceLogs.Read(api.Scheduler, request);

        overHttp.Should().BeEquivalentTo(inProcess, "the host reads the lines where the firing runs and sends them as they are");
        overHttp.State.Should().Be(FireInstanceLogState.Running);
        overHttp.Entries.Should().HaveCount(2);
    }

    /// <summary>
    /// A host older than 4.9 has no such route and answers it with a bare <c>404</c>. Nothing can be read in its
    /// place: the lines are in the host's process.
    /// </summary>
    [Test]
    public async Task AHostWithoutTheRouteCannotShowARunningLog()
    {
        Api api = await StartApi(runJob: false);
        WithoutTheLogRoute transport = new(new HttpWireTransport(api.Client));
        await using HttpScheduler client = new(api.SchedulerName, transport, jsonSerializerOptions: null, serializerRegistry: null);

        Func<Task> read = async () => await FireInstanceLogs.Read(client, new FireInstanceLogRequest("any", 0, 10, null));

        await read.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("This target runs Quartz older than 4.9, which cannot show a running firing's log*");
    }

    /// <summary>
    /// A refusal the host made comes back as the <see cref="ArgumentException" /> an in-process read raises, with
    /// the host's words.
    /// </summary>
    [Test]
    public async Task AHostsRefusalIsAnArgumentExceptionWithItsWords()
    {
        Api api = await StartApi(runJob: false);
        SendingTakeZero transport = new(new HttpWireTransport(api.Client));
        await using HttpScheduler client = new(api.SchedulerName, transport, jsonSerializerOptions: null, serializerRegistry: null);

        Func<Task> read = async () => await FireInstanceLogs.Read(client, new FireInstanceLogRequest("any", 0, 10, null));

        await read.Should().ThrowAsync<ArgumentException>().WithMessage("take must be from 1 to 1000, was 0*");
    }

    private async Task<Api> StartApi(bool runJob = true, string? policy = null, bool readOnly = false)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        string schedulerName = $"running-log-{Guid.NewGuid():N}";
        WebApplicationFactory<Program> application = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            if (policy is not null)
            {
                services.AddRoleAuthorization();
            }

            services.AddQuartzHttpApi(options =>
            {
                options.OperationAuthorizationPolicy = policy;
                options.ReadOnly = readOnly;
            });
            services.AddQuartz(schedulerName, quartz =>
            {
                quartz.ConfigureScheduler(options => options.InstanceId = Node);
                quartz.UseInMemoryStore();
                quartz.UseExecutionLogCapture();
            });
        }));
        factories.Add(application);

        IScheduler scheduler = await application.Services.GetRequiredKeyedService<ISchedulerFactory>(schedulerName).GetScheduler();
        schedulers.Add(scheduler);

        string? firing = null;
        if (runJob)
        {
            await scheduler.Start();
            await scheduler.AddJob(JobBuilder.Create<HeldLoggingJob>().WithIdentity(jobKey).StoreDurably().Build());
            await scheduler.TriggerJob(jobKey);
            firing = await HeldLoggingJob.Logged.Task.WaitAsync(waitLimit);
        }

        return new Api(schedulerName, application.CreateClient(), application, scheduler, firing);
    }

    private static string Message(string entry) => entry[(entry.IndexOf(": ", StringComparison.Ordinal) + 2)..];

    private sealed record Api(string SchedulerName, HttpClient Client, WebApplicationFactory<Program> Factory, IScheduler Scheduler, string? Firing)
    {
        public string LogPath(string? firing = null)
        {
            return $"schedulers/{SchedulerName}/jobs/fire-instances/{Uri.EscapeDataString(firing ?? Firing!)}/log";
        }

        public async Task<JsonDocument> Read(string query = "", string? firing = null)
        {
            using HttpResponseMessage response = await Client.GetAsync(LogPath(firing) + query);
            string body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
            return JsonDocument.Parse(body);
        }
    }

    /// <summary>
    /// A host as 4.8 is to a 4.9 client: every route but the log, which answers a <c>404</c> with no problem
    /// details, as ASP.NET Core does for a path nothing is mapped at.
    /// </summary>
    private sealed class WithoutTheLogRoute(IWireTransport inner) : IWireTransport
    {
        public ValueTask<WireResponse> Send(WireRequest request, CancellationToken cancellationToken = default)
        {
            return request.Route == SchedulerRoutes.GetFireInstanceLog
                ? ValueTask.FromResult(new WireResponse(HttpStatusCode.NotFound, []))
                : inner.Send(request, cancellationToken);
        }
    }

    /// <summary>
    /// Sends what the client built with a page size the host refuses, for a case about how the client reads the
    /// host's refusal: the client refuses it itself before sending.
    /// </summary>
    private sealed class SendingTakeZero(IWireTransport inner) : IWireTransport
    {
        public ValueTask<WireResponse> Send(WireRequest request, CancellationToken cancellationToken = default)
        {
            return inner.Send(request with { Path = request.Path.Replace("take=10", "take=0", StringComparison.Ordinal) }, cancellationToken);
        }
    }

    /// <summary>Logs three lines and runs until it is released.</summary>
    public sealed class HeldLoggingJob : IJob
    {
        private readonly ILogger<HeldLoggingJob> logger;

        public HeldLoggingJob(ILogger<HeldLoggingJob> logger)
        {
            this.logger = logger;
        }

        /// <summary>The firing's fire instance id, once it has logged.</summary>
        public static TaskCompletionSource<string> Logged { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            Logged = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("line 1");
            logger.LogInformation("line 2");
            logger.LogInformation("line 3");

            Logged.TrySetResult(context.FireInstanceId);
            await Release.Task.WaitAsync(waitLimit, cancellationToken);
        }
    }
}
