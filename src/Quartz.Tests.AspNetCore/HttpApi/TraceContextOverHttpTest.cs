using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The HTTP API gets trace propagation for nothing, because the endpoint runs inside the request's own
/// span and the scheduler reads <see cref="Activity.Current" />.
/// </summary>
/// <remarks>
/// <para>
/// This is the arrangement the feature was written for: a request arrives carrying a
/// <c>traceparent</c>, asks for a job to be fired, and returns long before the job runs. Nothing in
/// <c>Quartz.AspNetCore</c> mentions tracing — the whole of the connection is that ASP.NET Core has made
/// the server span current by the time the endpoint calls the scheduler — so this test exists to keep
/// that free ride from quietly ending.
/// </para>
/// <para>
/// The scheduler behind the API is a real one rather than this folder's fake, because a fake would
/// record the call without any of the code under test running. It is bound into the application's
/// repository the same way the fake is, and is deliberately never started: the trigger has to still be
/// there to be read back.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class TraceContextOverHttpTest
{
    private const string CallerTraceId = "0af7651916cd43dd8448eb211c80319c";

    private WebApplicationFactory<Program> factory;
    private ServiceProvider provider;
    private IScheduler scheduler;
    private ActivityListener listener;
    private string schedulerName;
    private JobKey jobKey;

    [SetUp]
    public async Task SetUp()
    {
        // ASP.NET Core only creates the request activity when something is listening, so this listener
        // is what makes Activity.Current non-null inside the endpoint. Listening to every source rather
        // than to one name keeps the test from depending on what the hosting layer calls its own.
        listener = new ActivityListener
        {
            ShouldListenTo = static _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        TestContentRoot.Apply();
        factory = new WebApplicationFactory<Program>();

        schedulerName = $"trace-over-http-{Guid.NewGuid():N}";
        jobKey = new JobKey("fire-me", "http");

        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = schedulerName);
            quartz.AddJob<DummyJob>(job => job.WithIdentity(jobKey).StoreDurably());
        });

        provider = services.BuildServiceProvider();
        scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        factory.Services.GetRequiredService<ISchedulerRepository>().Bind(scheduler);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (factory is not null)
        {
            factory.Services.GetRequiredService<ISchedulerRepository>().Remove(schedulerName);
            await factory.DisposeAsync();
        }

        if (scheduler is not null)
        {
            await scheduler.Shutdown();
        }

        if (provider is not null)
        {
            await provider.DisposeAsync();
        }

        listener?.Dispose();
    }

    [Test]
    public async Task TriggeringAJobOverHttpCarriesTheRequestsTraceOntoTheTrigger()
    {
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            $"schedulers/{schedulerName}/jobs/{jobKey.Group}/{jobKey.Name}/trigger");

        // The caller's own span, spelled the way it goes on the wire.
        string callerSpanId = "b7ad6b7169203331";
        request.Headers.Add("traceparent", $"00-{CallerTraceId}-{callerSpanId}-01");
        request.Content = JsonContent.Create(new { });

        using HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        ITrigger trigger = (await scheduler.GetTriggersOfJob(jobKey)).Should().ContainSingle(
            "the request asked for one firing").Subject;

        string traceParent = trigger.JobDataMap.Should().ContainKey(SchedulerConstants.TraceParent)
            .WhoseValue.Should().BeOfType<string>().Subject;

        ActivityContext.TryParse(traceParent, traceState: null, isRemote: true, out ActivityContext scheduledBy)
            .Should().BeTrue("what is stored is a W3C traceparent, readable by anything that reads the header");

        scheduledBy.TraceId.ToHexString().Should().Be(CallerTraceId,
            "the caller's trace reaches the trigger through the server span the endpoint runs inside — "
            + "that is the whole of what makes the HTTP API traceable across the scheduled gap");
        scheduledBy.SpanId.ToHexString().Should().NotBe(callerSpanId,
            "the firing links back to the server span that handled the request, not to the client span "
            + "that sent it, so the link points at work this process actually did");
    }
}
