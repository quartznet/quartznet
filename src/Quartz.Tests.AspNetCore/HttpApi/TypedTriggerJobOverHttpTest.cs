using AwesomeAssertions.Execution;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// <c>TriggerJob&lt;TInput&gt;</c> through <see cref="HttpScheduler" /> (#4138): the manual firing is stored on
/// the host under the key the client answered with, and an input crosses the wire as a job data value.
/// </summary>
/// <remarks>
/// A SQLite store on an unstarted scheduler, so the trigger is still there to read back.
/// </remarks>
[NonParallelizable]
public class TypedTriggerJobOverHttpTest
{
    private const string SchedulerName = "typed-trigger-job";

    private static readonly JobKey jobKey = new("send-invoice", "billing");

    private readonly SqliteStores stores = new("typed-trigger-job-over-http");
    private readonly List<WebApplicationFactory<Program>> factories = [];

    [OneTimeTearDown]
    public async Task TearDown()
    {
        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
        stores.Dispose();
    }

    [Test]
    public async Task AStringInputIsStoredOnTheHostAsTheManualFiringUnderTheKeyAnsweredWith()
    {
        (IScheduler scheduler, HttpScheduler client) = await CreateScheduler();

        try
        {
            const string payload = "{\"InvoiceId\":42}";

            TriggerKey key = await client.TriggerJob(jobKey, payload);

            ITrigger stored = (await scheduler.GetTrigger(key))!;
            using (new AssertionScope())
            {
                stored.Should().NotBeNull("the client built the key before the host stored the trigger");
                stored.JobKey.Should().Be(jobKey);
                stored.JobDataMap.GetString(SchedulerConstants.JobInput).Should().Be(payload,
                    "a string crosses the wire unchanged and is stored as it is, which the host's serializer reads back");
                stored.JobDataMap.GetString(SchedulerConstants.ManualTrigger).Should().Be("true",
                    "the marker travels on the trigger's map, so history on the host records a manual run");
            }
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// A structured input is a job data value no reader can produce, so the client's serializer refuses it
    /// before anything is sent: the limit the remarks document.
    /// </summary>
    [Test]
    public async Task AStructuredInputIsRefusedBeforeItLeavesTheClient()
    {
        (IScheduler scheduler, HttpScheduler client) = await CreateScheduler();

        try
        {
            Func<Task> act = async () => await client.TriggerJob(jobKey, new Invoice(42));

            await act.Should().ThrowAsync<JsonSerializationException>().WithMessage($"*{SchedulerConstants.JobInput}*");
            (await scheduler.GetTriggersOfJob(jobKey)).Should().BeEmpty("nothing reached the host");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    private async Task<(IScheduler Scheduler, HttpScheduler Client)> CreateScheduler()
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        string schedulerName = $"{SchedulerName}-{factories.Count}";
        WebApplicationFactory<Program> application = root.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddQuartz(schedulerName, quartz => stores.Configure(quartz, schedulerName))));
        factories.Add(application);

        IScheduler scheduler = await application.Services
            .GetRequiredKeyedService<ISchedulerFactory>(schedulerName)
            .GetScheduler();

        await scheduler.AddJob(JobBuilder.Create<InvoiceJob>().WithIdentity(jobKey).StoreDurably().Build());

        return (scheduler, new HttpScheduler(schedulerName, application.CreateClient()));
    }

    public sealed record Invoice(int InvoiceId);

    public sealed class InvoiceJob : IJob<Invoice>
    {
        public ValueTask Execute(IJobExecutionContext context, Invoice input, CancellationToken cancellationToken = default) => default;
    }
}
