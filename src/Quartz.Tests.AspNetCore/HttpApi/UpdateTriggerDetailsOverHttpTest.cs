using AwesomeAssertions.Execution;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// <see cref="IScheduler.UpdateTriggerDetails" /> end to end: a real scheduler over a real store,
/// edited through <see cref="HttpScheduler" /> and read back the same way.
/// </summary>
/// <remarks>
/// <para>
/// Every other test of this endpoint stands a fake scheduler behind it, which proves the request
/// arrived and nothing about the trigger changing. This one goes the whole way, over a persistent store
/// — the one deployments that reach for a remote scheduler are running, and the one whose update is an
/// <c>UPDATE</c> statement per field rather than a mutation of an object already in memory.
/// </para>
/// <para>
/// A file-backed SQLite database rather than <c>:memory:</c>, for the reason
/// <see cref="SqliteStores" /> gives: the store opens a connection per operation, and an in-memory
/// database belongs to the connection that made it.
/// </para>
/// </remarks>
[NonParallelizable]
public class UpdateTriggerDetailsOverHttpTest
{
    private const string SchedulerName = "details";

    private readonly SqliteStores stores = new("update-trigger-details");
    private readonly List<WebApplicationFactory<Program>> factories = [];

    private static readonly JobKey jobKey = new("job", "group");
    private static readonly TriggerKey triggerKey = new("trigger", "group");

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
    public async Task EveryFieldAnUpdateCarriesLandsOnTheStoredTrigger()
    {
        (IScheduler scheduler, HttpScheduler client) = await CreateScheduler();

        try
        {
            await scheduler.AddCalendar("HolidayCalendar", new HolidayCalendar(), AddCalendarOptions.Replacing);
            await scheduler.ScheduleJob(
                JobBuilder.Create<DummyJob>().WithIdentity(jobKey).Build(),
                TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).WithCronSchedule("0 0 12 * * ?").Build());

            ITrigger before = (await client.GetTrigger(triggerKey))!;

            bool applied = await client.UpdateTriggerDetails(triggerKey, new TriggerDetailsUpdate()
                .WithDescription("nightly export")
                .WithPriority(8)
                .WithJobDataMap(new JobDataMap { { "region", "eu-west" } })
                .WithCalendarName("HolidayCalendar")
                .WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)
                .WithPreferredNode(PreferredNode.For("node-a"))
                .WithExecutionGroup("imports")
                .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromSeconds(30))));

            applied.Should().BeTrue("the trigger exists, so the update applied");

            ITrigger after = (await client.GetTrigger(triggerKey))!;

            using (new AssertionScope())
            {
                after.Description.Should().Be("nightly export");
                after.Priority.Should().Be(8);
                after.JobDataMap.Should().ContainKey("region").WhoseValue.Should().Be("eu-west");
                after.CalendarName.Should().Be("HolidayCalendar");
                after.MisfireInstructionCode.Should().Be((int) CronTriggerMisfireInstruction.DoNothing);
                after.PreferredNode.Should().Be(PreferredNode.For("node-a"));
                after.ExecutionGroup.Should().Be("imports");
                after.RetryPolicy.Should().Be(RetryPolicy.Fixed(3, TimeSpan.FromSeconds(30)));

                after.StartTimeUtc.Should().Be(before.StartTimeUtc, "an update is not a reschedule");
                after.NextFireTimeUtc.Should().Be(before.NextFireTimeUtc, "the fire times are what this call leaves alone");
            }
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// A second update naming one field leaves the seven the first one set where they were, and a member
    /// sent as <c>null</c> clears its own field and only its own.
    /// </summary>
    [Test]
    public async Task AnUpdateChangesWhatItNamesAndNothingElse()
    {
        (IScheduler scheduler, HttpScheduler client) = await CreateScheduler();

        try
        {
            await scheduler.ScheduleJob(
                JobBuilder.Create<DummyJob>().WithIdentity(jobKey).Build(),
                TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey)
                    .WithDescription("as scheduled")
                    .WithPriority(4)
                    .WithCronSchedule("0 0 12 * * ?")
                    .Build());

            await client.UpdateTriggerDetails(triggerKey, new TriggerDetailsUpdate().WithExecutionGroup("imports"));
            await client.UpdateTriggerDetails(triggerKey, new TriggerDetailsUpdate().WithPriority(9));

            ITrigger after = (await client.GetTrigger(triggerKey))!;

            using (new AssertionScope())
            {
                after.Priority.Should().Be(9);
                after.Description.Should().Be("as scheduled", "a member the body never carried is a member the store never touched");
                after.ExecutionGroup.Should().Be("imports", "and the earlier update stands");
            }

            await client.UpdateTriggerDetails(triggerKey, new TriggerDetailsUpdate().WithDescription(null));

            after = (await client.GetTrigger(triggerKey))!;

            using (new AssertionScope())
            {
                after.Description.Should().BeNull("null is carried, and clears");
                after.ExecutionGroup.Should().Be("imports", "clearing one field is not clearing the rest");
                after.Priority.Should().Be(9);
            }
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task ATriggerThatDoesNotExistIsReportedRatherThanRefused()
    {
        (IScheduler scheduler, HttpScheduler client) = await CreateScheduler();

        try
        {
            bool applied = await client.UpdateTriggerDetails(
                new TriggerKey("no-such-trigger", "group"),
                new TriggerDetailsUpdate().WithPriority(1));

            applied.Should().BeFalse(
                "IScheduler.UpdateTriggerDetails answers false for a key that resolves to nothing, and the "
                + "endpoint says the same rather than turning it into a 404 the client would have to catch");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// The misfire instruction's schedule family, which is the one part of an update the wire could have
    /// dropped without anything else noticing: the store refuses an instruction stated in a family other
    /// than the stored trigger's, and it can only do that if the family got here.
    /// </summary>
    [Test]
    public async Task AMisfireInstructionStatedInTheWrongFamilyIsRefused()
    {
        (IScheduler scheduler, HttpScheduler client) = await CreateScheduler();

        try
        {
            await scheduler.ScheduleJob(
                JobBuilder.Create<DummyJob>().WithIdentity(jobKey).Build(),
                TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey).WithCronSchedule("0 0 12 * * ?").Build());

            Func<Task> update = async () => await client.UpdateTriggerDetails(
                triggerKey,
                new TriggerDetailsUpdate().WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow));

            await update.Should().ThrowAsync<SchedulerException>(
                "the same number means a different policy in each family, so a simple trigger's instruction "
                + "aimed at a cron trigger has to be refused here exactly as it is in process");

            ITrigger after = (await client.GetTrigger(triggerKey))!;
            after.MisfireInstructionCode.Should().Be((int) CronTriggerMisfireInstruction.SmartPolicy,
                "a refused update changes nothing");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// One application per test, each with its own SQLite file: these tests write to the store, and a
    /// shared database would make the order they run in part of what they assert.
    /// </summary>
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

        // Deliberately not started: these tests are about what an update writes, and a running scheduler
        // would fire the trigger they are editing.
        return (scheduler, new HttpScheduler(schedulerName, application.CreateClient()));
    }
}
