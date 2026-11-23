using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// <c>TriggerJobAndWait</c> through <see cref="HttpScheduler" /> (#4414): no completion on the host reaches the
/// client, so the wait polls the host's history and trigger routes, and a host whose history cannot be read
/// refuses before anything is stored.
/// </summary>
[NonParallelizable]
public sealed class FiringWaitOverHttpTest
{
    private const string SchedulerName = "firing-wait-over-http";

    private static readonly JobKey jobKey = new("report", "billing");
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    private readonly List<WebApplicationFactory<Program>> factories = [];
    private readonly List<IScheduler> schedulers = [];

    [TearDown]
    public async Task TearDown()
    {
        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        schedulers.Clear();
        factories.Clear();
    }

    [Test]
    public async Task TheWaitIsAnsweredFromTheHostsHistory()
    {
        (IScheduler host, HttpScheduler client) = await Create();
        await host.Start();

        FiringResult result = await client.TriggerJobAndWait(jobKey, waitLimit);

        result.Status.Should().Be(FiringWaitStatus.Ended);
        result.Outcome.Should().Be(ExecutionOutcome.Succeeded);
        result.Execution!.JobName.Should().Be(jobKey.Name);
        result.Execution.Manual.Should().BeTrue();
        result.Execution.EntryId.Should().NotBeNullOrEmpty("the row is the one the host's history recorded");
        (await host.GetTriggerState(result.TriggerKey)).Should().Be(TriggerState.None);
    }

    [Test]
    public async Task AWaitForAFiringThatEndedIsReadFromTheHostsHistory()
    {
        (IScheduler host, HttpScheduler client) = await Create();
        await host.Start();
        FiringResult first = await client.TriggerJobAndWait(jobKey, waitLimit);

        FiringResult again = await client.WaitForFiring(first.TriggerKey, waitLimit);

        again.Status.Should().Be(FiringWaitStatus.Ended);
        again.Execution!.EntryId.Should().Be(first.Execution!.EntryId);
    }

    /// <summary>
    /// A host that serves no history routes answers them <c>404</c> without problem details, as an API from
    /// before the history did; the client reads that as the host keeping none it can read.
    /// </summary>
    [Test]
    public async Task AHostThatServesNoHistoryIsRefusedBeforeAnythingIsStored()
    {
        (IScheduler host, HttpScheduler client) = await Create(withoutHistoryRoutes: true);
        await host.Start();

        Func<Task> act = async () => await client.TriggerJobAndWait(jobKey, waitLimit);

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*does not serve history*");
        (await host.GetTriggersOfJob(jobKey)).Should().BeEmpty("the client read the host's history before it stored the trigger");
    }

    private async Task<(IScheduler Host, HttpScheduler Client)> Create(bool withoutHistoryRoutes = false)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        string schedulerName = $"{SchedulerName}-{factories.Count}";
        WebApplicationFactory<Program> application = root.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddQuartz(schedulerName, quartz =>
                quartz.ConfigureScheduler(options => options.IdleWaitTime = TimeSpan.FromSeconds(1)))));
        factories.Add(application);

        IScheduler scheduler = await application.Services
            .GetRequiredKeyedService<ISchedulerFactory>(schedulerName)
            .GetScheduler();
        schedulers.Add(scheduler);

        await scheduler.AddJob(JobBuilder.Create<ReportJob>().WithIdentity(jobKey).StoreDurably().Build());

        HttpClient http = withoutHistoryRoutes
            ? application.CreateDefaultClient(new Uri("http://localhost/"), new NoHistoryRoutes())
            : application.CreateClient();

        return (scheduler, new HttpScheduler(schedulerName, http));
    }

    /// <summary>Answers every history route <c>404</c> with no body, as a host without them does.</summary>
    private sealed class NoHistoryRoutes : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return request.RequestUri!.AbsolutePath.Contains("/history/", StringComparison.Ordinal)
                ? Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound))
                : base.SendAsync(request, cancellationToken);
        }
    }

    public sealed class ReportJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
