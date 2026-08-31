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

using System.Collections.Concurrent;
using System.Diagnostics;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

using Quartz.Diagnostics;

namespace Quartz.Tests.Integration.Scenarios;

/// <summary>
/// Which job store the scenario runs against.
/// </summary>
/// <remarks>
/// Both run without a container, so both run in the <c>basic</c> integration leg — this fixture
/// carries none of the <c>db-*</c> categories, which is what that leg selects by. The in-memory store
/// is what a bus gets by default; the SQLite file is the one where everything between a scheduling
/// call and a firing is rows, which is where a payload that was never serialized, or a trace context
/// that only existed in memory, stops working.
/// </remarks>
public enum BusStore
{
    /// <summary><see cref="Quartz.Impl.RAMJobStore" />.</summary>
    InMemory,

    /// <summary>The ADO.NET store on a SQLite file, whose schema the store creates for itself.</summary>
    SqliteFile
}

/// <summary>
/// The sequence a message bus performs when it embeds Quartz, start to finish, on one scheduler.
/// </summary>
/// <remarks>
/// <para>
/// Every primitive the 4.0 integrator work added is tested on its own somewhere else. This is the test
/// for the place they meet: a scheduler the host builds but does not start, a typed payload put on a
/// trigger under the bus's own message id, that id scheduled over atomically, a whole correlation
/// called off in one call, a firing that links back to the request that asked for it, and middleware
/// around all of it. A bus author should be able to read this top to bottom as "how to embed Quartz",
/// which is the other reason it is one method rather than eight.
/// </para>
/// <para>
/// It is one sequence and not eight independent tests because the sequence is the subject: step 3
/// replaces what step 2 scheduled, and step 8 has to find nothing left running from steps 2 to 6.
/// Each assertion carries a reason naming the primitive it is about, so a failure says which one
/// regressed rather than only that the scenario broke.
/// </para>
/// <para>
/// The clock is a <see cref="FakeTimeProvider" />, so nothing here waits out a real delay. Two facts
/// about the scheduling loop shape how it is driven, both of which
/// <see cref="Core.SchedulerAcrossDstTransitionTest" /> writes up as well: advancing a fake clock does
/// not wake the loop, which waits on a semaphore that only knows real elapsed time, so every advance
/// is followed by something that signals a scheduling change; and the misfire threshold is set wider
/// than any jump made here, so a fire the clock stepped over is fired rather than treated as missed.
/// The only real time anywhere is a deadline, so a firing that never happens fails instead of hanging.
/// </para>
/// </remarks>
[TestFixture(BusStore.InMemory)]
[TestFixture(BusStore.SqliteFile)]
[NonParallelizable]
public sealed class BusIntegrationScenarioTest
{
    /// <summary>How long a firing has to arrive before the scenario gives up on it. Real time.</summary>
    private static readonly TimeSpan FireDeadline = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Far wider than the largest jump the scenario makes on the fake clock — twenty minutes — so no
    /// advance ever looks like a missed fire, and the misfire handler's scans stay out of the way.
    /// </summary>
    /// <remarks>
    /// A day rather than something absurd: the ADO store's misfire handler sleeps for this between
    /// scans, and <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)" /> refuses a delay
    /// beyond about forty-nine days, so a frequency wider than that faults the handler.
    /// </remarks>
    private static readonly TimeSpan MisfireThreshold = TimeSpan.FromDays(1);

    /// <summary>Short enough that the loop re-reads the clock promptly after being signalled.</summary>
    private static readonly TimeSpan IdleWaitTime = TimeSpan.FromSeconds(1);

    /// <summary>Where the fake clock starts. An arbitrary instant, fixed so the test reads the same every run.</summary>
    private static readonly DateTimeOffset Origin = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The name the health check registers under when nothing renames it.</summary>
    private const string HealthCheckName = "quartz-scheduler";

    private readonly BusStore store;
    private readonly List<Activity> executeActivities = [];

    private string run;
    private string databaseFile;
    private FakeTimeProvider clock;
    private BusRecorder recorder;
    private ActivityListener listener;
    private IHost host;
    private IScheduler scheduler;

    public BusIntegrationScenarioTest(BusStore store)
    {
        this.store = store;
    }

    [SetUp]
    public void SetUp()
    {
        run = Guid.NewGuid().ToString("N")[..8];
        clock = new FakeTimeProvider(Origin);
        recorder = new BusRecorder();

        if (store == BusStore.SqliteFile)
        {
            // Nothing creates the file and nothing runs a script against it: the store provisions its
            // own schema the first time it is built, which is step 1's business.
            databaseFile = $"bus-scenario-{run}.db";
        }

        lock (executeActivities)
        {
            executeActivities.Clear();
        }

        // Only the execute spans are kept. The listener makes every Quartz activity recorded, including
        // the job store's, and collecting all of them would be thousands of objects nothing reads.
        listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == QuartzInstrumentation.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName != OperationName.Job.Execute)
                {
                    return;
                }

                lock (executeActivities)
                {
                    executeActivities.Add(activity);
                }
            }
        };

        ActivitySource.AddActivityListener(listener);
    }

    [TearDown]
    public async Task TearDown()
    {
        listener?.Dispose();

        if (host is not null)
        {
            // StopAsync is the scenario's last step, but a failure earlier leaves the host running with
            // a scheduler thread in it.
            try
            {
                await host.StopAsync();
            }
            catch (Exception)
            {
                // the assertion that already failed is the one worth reading
            }

            host.Dispose();
            host = null;
        }

        scheduler = null;

        if (databaseFile is null)
        {
            return;
        }

        // Pools first, or the handle the store left behind keeps the file locked on Windows.
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            try
            {
                File.Delete(databaseFile);
            }
            catch (IOException)
            {
                // scratch space; leaving one behind is not worth failing a passing test over
            }
        }

        databaseFile = null;
    }

    /// <summary>
    /// The whole sequence, in the order a bus performs it.
    /// </summary>
    [Test]
    public async Task ABusEmbedsQuartzAndEveryStepOfTheSequenceHolds()
    {
        await TheHostBuildsTheSchedulerAndLeavesItForTheBusToStart();
        await TheBusStartsTheSchedulerAndTheProbeTurnsHealthy();
        await AMessageIsScheduledByItsOwnIdWithATypedPayload();
        await TheSameMessageIdIsScheduledOverInOneStoreOperation();
        await AWholeCorrelationIsCalledOffInOneCall();
        await AFiringLinksBackToTheActivityThatScheduledIt();
        await MiddlewareWrapsEveryFiringAndSeesTheAmbientContext();

        // TODO(#3520): a trigger's retry policy. RetryPolicy and the RETRY_POLICY / RETRY_ATTEMPT
        // columns exist, but nothing on TriggerBuilder reaches them yet, so there is no call for this
        // step to make. It belongs here, between the middleware step and shutdown:
        //
        //   a firing that throws is retried on the trigger's own policy rather than by holding a
        //   thread-pool slot, and the attempt count is what the bus reads to give up.

        await TheBusStopsTheSchedulerAndTheHostStopsCleanly();
    }

    // -------------------------------------------------------------------------------------------
    // 1. Deferred start. A bus owns its own readiness - leader election, a transport connection, a
    //    migration - so it wants the container to produce a scheduler without the host pressing start.
    // -------------------------------------------------------------------------------------------

    private async Task TheHostBuildsTheSchedulerAndLeavesItForTheBusToStart()
    {
        host = BuildHost();
        await host.StartAsync();

        scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();

        scheduler.Should().NotBeNull(
            "AutoStart = false opts out of the start, not out of the scheduler: the hosted service still "
            + "resolves and binds one, or a bus that starts its own scheduler would have nothing to start");

        scheduler.Status.Should().Be(SchedulerStatus.Created,
            "the host was told not to start this scheduler, so it must be built and waiting rather than running");

        (await scheduler.Exists(new JobKey("nothing-is-stored-yet", run))).Should().BeFalse(
            "the scheduler is initialized as well as constructed, so its store answers questions before "
            + "anything starts - which for the SQLite case is also the proof that ProvisionSchema() "
            + "already created the tables, since a missing schema is an exception rather than a false");

        List<SchedulerRegistration> registrations = await host.Services
            .GetRequiredService<ISchedulerRegistry>()
            .QuerySchedulers();

        registrations.Should().ContainSingle(registration => registration.Name == SchedulerName)
            .Which.Status.Should().Be(SchedulerStatus.Created,
                "a scheduler that is created but not started is still one the registry, the dashboard and "
                + "the HTTP API can see - being unstarted is not being invisible");

        (await Probe()).Status.Should().Be(HealthStatus.Degraded,
            "a scheduler whose application presses start is neither healthy nor broken while it waits: "
            + "healthy would hide an application that never started it, and unhealthy would take a "
            + "correctly configured node out of rotation for doing what it was told");
    }

    private async Task TheBusStartsTheSchedulerAndTheProbeTurnsHealthy()
    {
        await scheduler.Start();

        scheduler.Status.Should().Be(SchedulerStatus.Running,
            "the bus pressing start is the whole of what AutoStart = false left for it to do");

        (await Probe()).Status.Should().Be(HealthStatus.Healthy,
            "once the scheduler is running and its store answers, the node is ready to take traffic - and "
            + "the probe flipping is how an orchestrator learns that without the bus telling it");
    }

    // -------------------------------------------------------------------------------------------
    // 2. A scheduled message: a typed payload, a delay, and the bus's own id as the handle.
    // -------------------------------------------------------------------------------------------

    private async Task AMessageIsScheduledByItsOwnIdWithATypedPayload()
    {
        string messageId = $"receipt-{run}-1";
        string correlationId = $"order-{run}";
        SendReceipt payload = new SendReceipt(correlationId, "customer@example.org", Attempt: 1);

        TriggerKey key = await scheduler.ScheduleJob<ReceiptJob, SendReceipt>(
            payload,
            TimeSpan.FromMinutes(5),
            new OneOffJobOptions { Name = messageId, Group = correlationId });

        key.Should().Be(new TriggerKey(messageId, correlationId),
            "the bus named the firing after its own message id and correlation, and the key it gets back "
            + "is the handle it will cancel or replace with - a generated one would be a second id to keep");

        ITrigger stored = await scheduler.GetTrigger(key);

        stored.Should().NotBeNull("the trigger the call answered for has to be in the store");
        stored.StartTimeUtc.Should().Be(Origin + TimeSpan.FromMinutes(5),
            "a delay is measured against IScheduler.TimeProvider, which is the clock the scheduling loop "
            + "will compare the fire time to - measuring it against the machine's would put the firing "
            + "somewhere neither of them agrees on");

        stored.JobDataMap[SchedulerConstants.JobInput].Should().BeOfType<string>(
                "the payload has to reach the store as a string, or it only works on the in-memory store, "
                + "which hands objects straight back and would mask a serializer that never ran")
            .Which.Should().Contain("customer@example.org",
                "the string is the serialized payload rather than a placeholder");

        (await scheduler.Exists(DurableJobKey)).Should().BeTrue(
            "one durable job per job type and a trigger per message: a bus schedules thousands of firings "
            + "and must not write a job row for each of them");

        Task<Firing> firing = recorder.Expect(key);
        await AdvanceTo(stored.StartTimeUtc);
        Firing fired = await Fires(key, firing);

        fired.Input.Should().Be(payload,
            "the job is handed the payload it was scheduled with, deserialized into the record it declared "
            + "- reaching into the JobDataMap by key is the thing IJob<TInput> exists to replace");
    }

    // -------------------------------------------------------------------------------------------
    // 3. The same id again. A bus that reschedules a timeout, or redelivers, must not have to run
    //    CheckExists / Unschedule / Schedule and lose the race between them.
    // -------------------------------------------------------------------------------------------

    private async Task TheSameMessageIdIsScheduledOverInOneStoreOperation()
    {
        string messageId = $"receipt-{run}-2";
        string correlationId = $"order-{run}";

        DateTimeOffset first = Now + TimeSpan.FromMinutes(10);
        SendReceipt draft = new SendReceipt(correlationId, "draft@example.org", Attempt: 1);

        TriggerKey key = await scheduler.ScheduleJob<ReceiptJob, SendReceipt>(
            draft,
            first,
            new OneOffJobOptions { Name = messageId, Group = correlationId });

        Func<Task> withoutReplace = async () => await scheduler.ScheduleJob<ReceiptJob, SendReceipt>(
            draft,
            first,
            new OneOffJobOptions { Name = messageId, Group = correlationId });

        await withoutReplace.Should().ThrowAsync<ObjectAlreadyExistsException>(
            "replacing is opt-in: an id that is already scheduled is a conflict the bus is told about, "
            + "rather than a silent over-write of whichever of the two the store happened to keep");

        DateTimeOffset later = Now + TimeSpan.FromMinutes(20);
        SendReceipt corrected = new SendReceipt(correlationId, "billing@example.org", Attempt: 2);

        TriggerKey replaced = await scheduler.ScheduleJob<ReceiptJob, SendReceipt>(
            corrected,
            later,
            new OneOffJobOptions { Name = messageId, Group = correlationId, Replace = true });

        replaced.Should().Be(key, "replacing is the same key by definition, or it is not a replacement");

        IReadOnlyList<TriggerHeader> underTheKey = await TriggersNamed(correlationId, messageId);

        underTheKey.Should().ContainSingle(
                "scheduling over an existing id leaves one trigger, not two - the upsert is one store "
                + "operation under the store's own lock rather than a delete and an insert")
            .Which.NextFireTimeUtc.Should().Be(later,
                "the replacement's time is the one that survives; a bus moving a timeout out has to be "
                + "able to trust that the earlier one is gone");

        ITrigger stored = await scheduler.GetTrigger(key);
        stored.JobDataMap[SchedulerConstants.JobInput].Should().BeOfType<string>(
                "a replacement's payload goes through the same serializer the first one did, and a store "
                + "that was handed an object here would fail on the write rather than on the read")
            .Which.Should().Contain("billing@example.org",
                "the replacement's payload is what is stored - a replaced trigger that kept the old "
                + "payload would deliver a message the bus has already corrected");

        Task<Firing> firing = recorder.Expect(key);
        await AdvanceTo(later);
        Firing fired = await Fires(key, firing);

        fired.Input.Should().Be(corrected,
            "the firing carries the payload the replacement was scheduled with, all the way to the job");
    }

    // -------------------------------------------------------------------------------------------
    // 4. Calling off a correlation. The group the one-liner put the firings in is the handle for
    //    cancelling all of them, and the count that comes back is what the bus logs.
    // -------------------------------------------------------------------------------------------

    private async Task AWholeCorrelationIsCalledOffInOneCall()
    {
        string cancelled = $"saga-cancelled-{run}";
        string survivor = $"saga-live-{run}";

        // Far enough out that no later advance in this scenario reaches it: the survivor's staying
        // scheduled is the assertion, so it must not quietly fire instead.
        DateTimeOffset far = Now + TimeSpan.FromDays(30);

        List<TriggerKey> correlated = [];
        for (int step = 1; step <= 3; step++)
        {
            correlated.Add(await scheduler.ScheduleJob<ReceiptJob, SendReceipt>(
                new SendReceipt(cancelled, "customer@example.org", step),
                far,
                new OneOffJobOptions { Name = $"step-{step}", Group = cancelled }));
        }

        TriggerKey other = await scheduler.ScheduleJob<ReceiptJob, SendReceipt>(
            new SendReceipt(survivor, "someone-else@example.org", Attempt: 1),
            far,
            new OneOffJobOptions { Name = "step-1", Group = survivor });

        List<TriggerKey> removed = await scheduler.UnscheduleJobs(GroupMatcher<TriggerKey>.GroupEquals(cancelled));

        removed.Should().BeEquivalentTo(correlated,
            "the answer names exactly what went, so 'there was nothing left to cancel' is a count rather "
            + "than a guess - and the store resolves the group inside the lock that empties it, so there "
            + "is no listing step for another node to schedule one more thing into");

        (await TriggersIn(cancelled)).Should().BeEmpty(
            "every firing in the cancelled correlation is gone, not merely the ones a caller had listed");

        (await TriggersIn(survivor)).Should().ContainSingle(
                "a matcher on one group must not reach into another - correlations are how a bus keeps "
                + "one conversation's firings apart from the next one's")
            .Which.Key.Should().Be(other,
                "the survivor is the trigger the other correlation scheduled, untouched");

        (await scheduler.Exists(DurableJobKey)).Should().BeTrue(
            "the durable job is shared by every firing of its type, so cancelling one correlation must "
            + "leave it standing - taking it would unschedule every other correlation with it");
    }

    // -------------------------------------------------------------------------------------------
    // 5. The trace across the gap. The call that asked for a firing and the firing itself are minutes
    //    or days apart, usually on different nodes, and everything they share went through the store.
    // -------------------------------------------------------------------------------------------

    private async Task AFiringLinksBackToTheActivityThatScheduledIt()
    {
        string correlationId = $"traced-{run}";
        DateTimeOffset at = Now + TimeSpan.FromMinutes(5);

        TriggerKey traced;
        ActivityTraceId schedulingTraceId;
        ActivitySpanId schedulingSpanId;

        // Started rather than merely constructed, because Activity.Current refuses a finished activity -
        // and put back to null as soon as the scheduling call returns, so nothing after it inherits it.
        using (Activity publishing = new Activity("bus.publish").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            schedulingTraceId = publishing.TraceId;
            schedulingSpanId = publishing.SpanId;

            try
            {
                traced = await scheduler.ScheduleJob<ReceiptJob, SendReceipt>(
                    new SendReceipt(correlationId, "traced@example.org", Attempt: 1),
                    at,
                    new OneOffJobOptions { Name = "traced", Group = correlationId });
            }
            finally
            {
                Activity.Current = null;
            }
        }

        Activity.Current.Should().BeNull(
            "the second trigger is the control, so it has to be scheduled from outside any activity");

        TriggerKey untraced = await scheduler.ScheduleJob<ReceiptJob, SendReceipt>(
            new SendReceipt(correlationId, "untraced@example.org", Attempt: 1),
            at,
            new OneOffJobOptions { Name = "untraced", Group = correlationId });

        Task<Firing> tracedFiring = recorder.Expect(traced);
        Task<Firing> untracedFiring = recorder.Expect(untraced);

        await AdvanceTo(at);
        await Fires(traced, tracedFiring);
        await Fires(untraced, untracedFiring);

        Activity tracedExecution = await ExecuteActivityFor(traced);

        ActivityLink link = tracedExecution.Links.Should().ContainSingle(
            "the firing links back to the one call that scheduled it - a link and not a parent, because a "
            + "trace held open from the scheduling call to the firing is one no backend renders").Subject;

        link.Context.TraceId.Should().Be(schedulingTraceId,
            "walking from a firing back to the request that asked for it is the point, and the trace id is "
            + "what an operator searches a backend by");
        link.Context.SpanId.Should().Be(schedulingSpanId,
            "the link points at the scheduling span itself, not merely at its trace");

        Activity untracedExecution = await ExecuteActivityFor(untraced);

        untracedExecution.Links.Should().BeEmpty(
            "a firing scheduled outside any activity has nothing to link back to, and inventing a link "
            + "would be worse than having none - a stale link reads exactly like a true one");
    }

    // -------------------------------------------------------------------------------------------
    // 6. Middleware. A log scope, a tenant, a transport's own context: things that have to surround
    //    the call to the job, which a listener cannot do because it is only notified either side of it.
    // -------------------------------------------------------------------------------------------

    private async Task MiddlewareWrapsEveryFiringAndSeesTheAmbientContext()
    {
        string correlationId = $"wrapped-{run}";
        DateTimeOffset at = Now + TimeSpan.FromMinutes(5);

        TriggerKey key = await scheduler.ScheduleJob<ReceiptJob, SendReceipt>(
            new SendReceipt(correlationId, "wrapped@example.org", Attempt: 1),
            at,
            new OneOffJobOptions { Name = "wrapped", Group = correlationId });

        Task<Firing> firing = recorder.Expect(key);
        await AdvanceTo(at);
        Firing fired = await Fires(key, firing);

        recorder.Timeline.Should().ContainInOrder(
            [$"middleware entered {key.Name}", $"job ran {key.Name}", $"middleware left {key.Name}"],
            "a middleware surrounds the job rather than being notified either side of it - which is the "
            + "whole difference from a listener, and the reason a wrapper job was the only place this "
            + "code could live before");

        recorder.MiddlewareSawTheFiring.Should().ContainKey(key)
            .WhoseValue.Should().BeTrue(
                "IJobExecutionContextAccessor.Current is how code that was never handed the context reaches "
                + "the firing it is part of, and a middleware - which is handed one - proves the ambient "
                + "value is the same firing rather than some other one");

        fired.AmbientContextWasThisFiring.Should().BeTrue(
            "the job sees the same ambient firing the middleware did, which is what a scoped service three "
            + "calls below Execute will read");

        recorder.MiddlewareWrapped.Should().BeEquivalentTo(recorder.JobRan,
            "middleware wraps every firing the scheduler performs, including the ones scheduled before it "
            + "was ever asked about - a pipeline that only covered what a caller routed through it would "
            + "be a wrapper job by another name");
    }

    // -------------------------------------------------------------------------------------------
    // 8. Shutdown. The bus stops its scheduler, then the host stops - and the host shutting down a
    //    scheduler it never started must not be what fails.
    // -------------------------------------------------------------------------------------------

    private async Task TheBusStopsTheSchedulerAndTheHostStopsCleanly()
    {
        await scheduler.Shutdown(waitForJobsToComplete: true);

        scheduler.Status.Should().Be(SchedulerStatus.Shutdown,
            "the bus stops its own scheduler when its transport goes away, without waiting for the host");

        List<string> entered = [.. recorder.Timeline.Where(entry => entry.StartsWith("middleware entered"))];
        List<string> left = [.. recorder.Timeline.Where(entry => entry.StartsWith("middleware left"))];

        entered.Should().NotBeEmpty(
            "the steps above fired jobs on the way here, and a balance of nothing against nothing would "
            + "prove nothing about the shutdown");

        left.Should().HaveCount(entered.Count,
            "every firing that started also finished before the shutdown returned - one entered and never "
            + "left is a job the shutdown walked away from");

        Func<Task> stopping = () => host.StopAsync();

        await stopping.Should().NotThrowAsync(
            "the hosted service shuts down every scheduler it created, started or not, so a scheduler the "
            + "bus already stopped is a no-op rather than an error out of host shutdown");

        List<SchedulerRegistration> registrations = await host.Services
            .GetRequiredService<ISchedulerRegistry>()
            .QuerySchedulers();

        registrations.Should().NotContain(registration => registration.Name == SchedulerName && registration.IsCreated,
            "a shut-down scheduler is dropped from the repository, so the registry reports the registration "
            + "with no live scheduler behind it rather than one an operator could still act on");
    }

    // -------------------------------------------------------------------------------------------
    // The container, as a bus would configure it.
    // -------------------------------------------------------------------------------------------

    private IHost BuildHost()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.Services.AddLogging();

        // The bus's own services. Registering the job type is what gives it constructor injection,
        // which is most of the reason a framework embeds a container-configured scheduler at all.
        builder.Services.AddSingleton(recorder);
        builder.Services.AddTransient<ReceiptJob>();

        builder.Services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = SchedulerName;
                options.IdleWaitTime = IdleWaitTime;
            });

            quartz.UseTimeProvider(clock);
            quartz.AddJobMiddleware<BusMiddleware>();

            if (store == BusStore.InMemory)
            {
                quartz.UseInMemoryStore(options => options.MisfireThreshold = MisfireThreshold);
            }
            else
            {
                quartz.UsePersistentStore(persistent =>
                {
                    persistent.UseSqlite(ConnectionString);

                    // No script to run first and no container to wait for: the store creates whatever
                    // its schema is missing when it is built.
                    persistent.ProvisionSchema();
                    persistent.UseSystemTextJsonSerializer();

                    persistent.ConfigureStore(options =>
                    {
                        options.MisfireThreshold = MisfireThreshold;

                        // The handler scans on the store's own clock, which is the fake one; a frequency
                        // wider than the scenario keeps its scans out of the way of the advances below.
                        options.MisfireHandlerFrequency = MisfireThreshold;
                    });
                });
            }
        });

        // The scheduler is built, initialized and bound, and left in Created for the bus to start.
        builder.Services.AddQuartzHostedService(options => options.AutoStart = false);
        builder.Services.AddQuartzHealthChecks();

        return builder.Build();
    }

    // -------------------------------------------------------------------------------------------
    // Driving the fake clock, and the reads the steps share.
    // -------------------------------------------------------------------------------------------

    private string SchedulerName => $"bus-{run}";

    private string ConnectionString => $"Data Source={databaseFile};";

    private DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>The single durable job every firing of <see cref="ReceiptJob" /> hangs off.</summary>
    private static JobKey DurableJobKey => new JobKey(nameof(ReceiptJob), SchedulerConstants.ScheduledJobGroup);

    /// <summary>
    /// Moves the scheduler's clock to just past <paramref name="instant" /> and wakes the loop.
    /// </summary>
    /// <remarks>
    /// One second past, so a fire time compared with "now" is unambiguously due. Advancing a fake clock
    /// does not by itself wake the scheduling loop, which waits on a semaphore that only knows real
    /// elapsed time; <see cref="IScheduler.ResumeAll" /> resumes nothing here - nothing is paused - and
    /// is called for the scheduling signal it sends, which is what makes the new "now" visible at once.
    /// </remarks>
    private async Task AdvanceTo(DateTimeOffset instant)
    {
        clock.SetUtcNow(instant + TimeSpan.FromSeconds(1));
        await scheduler.ResumeAll();
    }

    /// <summary>
    /// Waits for a firing the scheduler was expected to perform, with a real deadline so a firing that
    /// never happens fails rather than hangs.
    /// </summary>
    private static async Task<Firing> Fires(TriggerKey key, Task<Firing> firing)
    {
        Task first = await Task.WhenAny(firing, Task.Delay(FireDeadline));

        first.Should().BeSameAs(firing,
            "trigger '{0}' was due on the scheduler's clock and the loop was signalled, so its job must "
            + "have run within {1}",
            key,
            FireDeadline);

        return await firing;
    }

    /// <summary>
    /// The <c>Quartz.Job.Execute</c> activity of one firing.
    /// </summary>
    /// <remarks>
    /// Polled rather than awaited: the job signals from inside <c>Execute</c> and the span is closed
    /// after <c>Execute</c> returns, so the span can still be open when the firing is already known to
    /// have happened. The deadline is real time, and failing it is a failure rather than a hang.
    /// </remarks>
    private async Task<Activity> ExecuteActivityFor(TriggerKey key)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + FireDeadline;

        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (executeActivities)
            {
                Activity found = executeActivities.Find(activity =>
                    Equals(activity.GetTagItem(ActivityTags.TriggerName), key.Name)
                    && Equals(activity.GetTagItem(ActivityTags.TriggerGroup), key.Group));

                if (found is not null)
                {
                    return found;
                }
            }

            await Task.Delay(50);
        }

        Assert.Fail($"No {OperationName.Job.Execute} activity was recorded for trigger {key}.");
        return null;
    }

    private async Task<IReadOnlyList<TriggerHeader>> TriggersIn(string group)
    {
        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery
        {
            Group = GroupMatcher<TriggerKey>.GroupEquals(group)
        });

        page.HasMore.Should().BeFalse("a correlation in this scenario is a handful of firings, not a page of them");
        return page.Items;
    }

    private async Task<IReadOnlyList<TriggerHeader>> TriggersNamed(string group, string name)
    {
        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery
        {
            Group = GroupMatcher<TriggerKey>.GroupEquals(group),
            Name = NameMatcher<TriggerKey>.NameEquals(name)
        });

        return page.Items;
    }

    private async Task<HealthReportEntry> Probe()
    {
        HealthReport report = await host.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == HealthCheckName);

        report.Entries.Should().ContainKey(HealthCheckName,
            "AddQuartzHealthChecks() registers the default scheduler's check under this name, and it ships "
            + "in the core package rather than needing ASP.NET Core");

        return report.Entries[HealthCheckName];
    }
}

/// <summary>
/// The bus's message: an ordinary record, which is what a typed job declares it is scheduled with.
/// </summary>
public sealed record SendReceipt(string CorrelationId, string To, int Attempt);

/// <summary>
/// What one firing did, as the scenario's assertions read it.
/// </summary>
/// <param name="TriggerKey">The trigger that fired.</param>
/// <param name="Input">The payload the job was handed.</param>
/// <param name="AmbientContextWasThisFiring">
/// Whether <see cref="IJobExecutionContextAccessor.Current" /> was this firing while the job ran.
/// </param>
public sealed record Firing(TriggerKey TriggerKey, SendReceipt Input, bool AmbientContextWasThisFiring);

/// <summary>
/// What the job and the middleware saw, shared with the test through the container.
/// </summary>
/// <remarks>
/// A registered singleton rather than a static, so nothing has to be reset between runs and two
/// fixtures cannot read each other's firings.
/// </remarks>
public sealed class BusRecorder
{
    private readonly ConcurrentDictionary<TriggerKey, TaskCompletionSource<Firing>> firings = new();

    /// <summary>Every entered / ran / left event, in the order they happened.</summary>
    public ConcurrentQueue<string> Timeline { get; } = new();

    /// <summary>The triggers whose firings ran the job.</summary>
    public ConcurrentBag<TriggerKey> JobRan { get; } = new();

    /// <summary>The triggers whose firings went through the middleware.</summary>
    public ConcurrentBag<TriggerKey> MiddlewareWrapped { get; } = new();

    /// <summary>
    /// Per trigger, whether the middleware's ambient <see cref="IJobExecutionContext" /> was the firing
    /// it was handed.
    /// </summary>
    public ConcurrentDictionary<TriggerKey, bool> MiddlewareSawTheFiring { get; } = new();

    /// <summary>
    /// Registers interest in a firing before the clock is advanced, so the wait can never miss one that
    /// happened between scheduling and waiting.
    /// </summary>
    public Task<Firing> Expect(TriggerKey key) => Slot(key).Task;

    public void Ran(TriggerKey key, SendReceipt input, bool ambientContextWasThisFiring)
    {
        JobRan.Add(key);
        Timeline.Enqueue($"job ran {key.Name}");
        Slot(key).TrySetResult(new Firing(key, input, ambientContextWasThisFiring));
    }

    public void MiddlewareEntered(TriggerKey key, bool ambientContextWasThisFiring)
    {
        MiddlewareWrapped.Add(key);
        MiddlewareSawTheFiring[key] = ambientContextWasThisFiring;
        Timeline.Enqueue($"middleware entered {key.Name}");
    }

    public void MiddlewareLeft(TriggerKey key) => Timeline.Enqueue($"middleware left {key.Name}");

    private TaskCompletionSource<Firing> Slot(TriggerKey key)
    {
        return firings.GetOrAdd(
            key,
            static _ => new TaskCompletionSource<Firing>(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}

/// <summary>
/// The job a bus schedules: it declares its payload's type and is handed one, rather than reading a
/// <see cref="JobDataMap" /> by key.
/// </summary>
/// <remarks>
/// Public, like every other job in this project, because on the SQLite half the store hands the job
/// factory nothing but the type name it read back out of <c>JOB_CLASS_NAME</c>. Its dependencies are
/// ordinary constructor parameters, which is what registering the type in the container buys.
/// </remarks>
public sealed class ReceiptJob(BusRecorder recorder, IJobExecutionContextAccessor accessor) : IJob<SendReceipt>
{
    public ValueTask Execute(IJobExecutionContext context, SendReceipt input, CancellationToken cancellationToken = default)
    {
        recorder.Ran(context.Trigger.Key, input, accessor.Current?.FireInstanceId == context.FireInstanceId);
        return default;
    }
}

/// <summary>
/// The one place a bus's cross-cutting concern lives: around the call to the job, which is where a
/// listener cannot be.
/// </summary>
public sealed class BusMiddleware(BusRecorder recorder, IJobExecutionContextAccessor accessor) : IJobExecutionMiddleware
{
    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        recorder.MiddlewareEntered(context.Trigger.Key, accessor.Current?.FireInstanceId == context.FireInstanceId);

        await next(context, cancellationToken);

        recorder.MiddlewareLeft(context.Trigger.Key);
    }
}
